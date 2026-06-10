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
/// FTS-индексы есть на Users (имена персон) и на FamilyMembers (имена детей):
/// MatchUserIds — по персоне (родитель/регистрант, + email/телефон),
/// MatchFamilyMemberIds — по имени ребёнка (без контактов). Оба уважают флаг.
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

    /// <summary>
    /// FamilyMemberId детей, чьё имя/фамилия подходят под строку (по флагу: full-text CONTAINS
    /// или LIKE). Контактов у детей нет — только имя. При UseFullText требует FTS-индекса
    /// на FamilyMembers (04_keyset_indexes.sql). Пустые токены -> пустой набор.
    /// </summary>
    public IQueryable<int> MatchFamilyMemberIds(string raw)
    {
        var tokens = Tokenize(raw);
        if (tokens.Count == 0)
            return _db.FamilyMembers.Where(m => false).Select(m => m.FamilyMemberId);

        var q = _db.FamilyMembers.AsQueryable();
        if (_useFullText)
        {
            foreach (var tok in tokens)
            {
                var term = "\"" + tok + "*\"";   // префиксный full-text терм
                q = q.Where(m => EF.Functions.Contains(m.FirstName, term)
                              || EF.Functions.Contains(m.LastName, term));
            }
        }
        else
        {
            foreach (var tok in tokens)
            {
                var t = tok;
                q = q.Where(m => m.FirstName.Contains(t) || m.LastName.Contains(t));
            }
        }
        return q.Select(m => m.FamilyMemberId);
    }

    // Маршрутизация по форме ввода (чтобы не сканировать всё подряд и быть сарджабельным):
    //   '@'            -> email: префикс по UX_Users_Email (index seek);
    //   цифры без букв -> телефон: подстрока по узкому IX_Users_Phone (не кластерный скан);
    //   иначе          -> имя: full-text CONTAINS (токены AND, префиксно, First|Last).
    // Контактные и именной пути не смешиваем: запрос обычно одного намерения, а раздельность
    // даёт сарджабельность и убирает лишние сканы ~всех Users.
    private IQueryable<int> FullTextIds(string raw)
    {
        if (raw.Contains('@'))
            return _db.Users.Where(u => u.Email.StartsWith(raw)).Select(u => u.UserId);

        if (raw.Any(char.IsDigit) && !raw.Any(char.IsLetter))
            return _db.Users.Where(u => u.Phone != null && u.Phone.Contains(raw)).Select(u => u.UserId);

        var tokens = Tokenize(raw);
        if (tokens.Count == 0)
            return _db.Users.Where(u => false).Select(u => u.UserId);   // мусорный ввод -> ничего

        var names = _db.Users.AsQueryable();
        foreach (var tok in tokens)
        {
            var term = "\"" + tok + "*\"";   // префиксный full-text терм, параметризуется EF
            names = names.Where(u => EF.Functions.Contains(u.FirstName, term)
                                  || EF.Functions.Contains(u.LastName, term));
        }
        return names.Select(u => u.UserId);
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
