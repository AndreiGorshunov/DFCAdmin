using System.Text.RegularExpressions;
using DfcEventRegistration.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace DfcEventRegistration.Web.Services;

/// <summary>
/// Единая стратегия поиска персон для всех листингов (Registrants/Users/Children).
/// Возвращает UserId подходящих персон — каждый сервис фильтрует свой корень по своему FK
/// (r.UserId / u.UserId / p.Registration.UserId), один сарджабельный IN, не ломает keyset.
///
/// Имя ищется по флагу Search:UseFullText:
///   true  -> full-text CONTAINS по Users.FirstName/LastName (нужен FTS-индекс из 04_keyset_indexes.sql;
///            EF.Functions.Contains транслируется в SQL CONTAINS, не в LIKE). Префиксно, токены AND.
///   false -> токенизированный LIKE (подстрока, в т.ч. внутри слова; работает без FTS-фичи).
/// Email/телефон всегда LIKE (в full-text индекс не входят).
///
/// FTS есть только на Users — поэтому имена ДЕТЕЙ (FamilyMembers) ищутся LIKE в самом
/// ChildQueryService; сюда заведён только матч по персоне (родителю/регистранту).
/// </summary>
public sealed class UserSearchService
{
    private readonly AppDbContext _db;
    private readonly bool _useFullText;

    public UserSearchService(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _useFullText = config.GetValue<bool>("Search:UseFullText");
    }

    public bool UseFullText => _useFullText;

    /// <summary>Токены поиска: руны букв/цифр (Unicode, вкл. арабский), не более 4.</summary>
    public static List<string> Tokenize(string raw)
        => Regex.Matches(raw, @"[\p{L}\p{N}]+").Select(m => m.Value).Take(4).ToList();

    /// <summary>UserId персон, подходящих под строку поиска. Используется как подзапрос: &lt;fk&gt; IN (...).</summary>
    public IQueryable<int> MatchUserIds(string raw)
        => _useFullText ? FullTextIds(raw) : LikeIds(raw);

    // FTS: имена -> CONTAINS (токены AND, префиксно, First|Last). Контакт (email/телефон) —
    // несарджабельный LIKE-скан, поэтому включаем его ТОЛЬКО когда ввод похож на контакт
    // (есть '@' или цифра). Иначе для именного запроса это лишний скан ~всех Users
    // (имена и так покрыты full-text'ом, email у нас производный от имени), из-за которого
    // «fatima archebe» (0 совпадений) сканит таблицу целиком, чтобы доказать пустоту.
    private IQueryable<int> FullTextIds(string raw)
    {
        var tokens = Tokenize(raw);

        IQueryable<int>? nameIds = null;
        if (tokens.Count > 0)
        {
            var names = _db.Users.AsQueryable();
            foreach (var tok in tokens)
            {
                var term = "\"" + tok + "*\"";   // префиксный full-text терм, параметризуется EF
                names = names.Where(u => EF.Functions.Contains(u.FirstName, term)
                                      || EF.Functions.Contains(u.LastName, term));
            }
            nameIds = names.Select(u => u.UserId);
        }

        var looksLikeContact = raw.Contains('@') || raw.Any(char.IsDigit);
        IQueryable<int>? contactIds = looksLikeContact
            ? _db.Users.Where(u => u.Email.Contains(raw) || (u.Phone != null && u.Phone.Contains(raw)))
                       .Select(u => u.UserId)
            : null;

        return (nameIds, contactIds) switch
        {
            (not null, not null) => nameIds.Union(contactIds),
            (not null, null)     => nameIds,
            (null, not null)     => contactIds,
            _                    => _db.Users.Where(u => false).Select(u => u.UserId)   // мусорный ввод -> ничего
        };
    }

    // LIKE-fallback: токенизированный поиск по имени/фамилии/email/телефону (AND токены, OR поля).
    private IQueryable<int> LikeIds(string raw)
    {
        var tokens = Tokenize(raw);
        if (tokens.Count == 0)
            return _db.Users.Where(u => false).Select(u => u.UserId);   // мусорный ввод -> ничего

        var q = _db.Users.AsQueryable();
        foreach (var tok in tokens)
        {
            var t = tok;
            q = q.Where(u =>
                u.FirstName.Contains(t) ||
                u.LastName.Contains(t) ||
                u.Email.Contains(t) ||
                (u.Phone != null && u.Phone.Contains(t)));
        }
        return q.Select(u => u.UserId);
    }
}
