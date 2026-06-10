using DfcEventRegistration.Web.Data;
using DfcEventRegistration.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace DfcEventRegistration.Web.Services;

/// <summary>
/// Грейн = пользователь (персона), не регистрация. Listing для вкладки Users.
/// </summary>
public class UserQueryService
{
    private readonly AppDbContext _db;
    private readonly UserSearchService _search;

    public UserQueryService(AppDbContext db, UserSearchService search)
    {
        _db = db;
        _search = search;
    }

    private IQueryable<User> Base(UserFilter f)
    {
        var q = _db.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(f.Q))
        {
            // Грейн = персона: фильтруем Users по набору совпавших UserId из общего хелпера
            // (имя FTS/LIKE по флагу Search:UseFullText + email/телефон LIKE).
            var userIds = _search.MatchUserIds(f.Q.Trim());
            q = q.Where(u => userIds.Contains(u.UserId));
        }

        return q;
    }

    public Task<int> CountAsync(UserFilter f, CancellationToken ct = default)
        => Base(f).CountAsync(ct);

    public IQueryable<UserRow> Query(UserFilter f)
        => Base(f).Select(u => new UserRow
        {
            UserId = u.UserId,
            FirstName = u.FirstName,
            LastName = u.LastName,
            Email = u.Email,
            Phone = u.Phone,
            DateOfBirth = u.DateOfBirth,
            // Коррелированные подзапросы — материализуются только для строк страницы.
            FamilyCount = _db.FamilyMembers.Count(fm => fm.UserId == u.UserId),
            RegistrationCount = _db.EventRegistrations.Count(r => r.UserId == u.UserId)
        });

    public IQueryable<UserRow> ApplySort(IQueryable<UserRow> q, string? sort, string? dir)
    {
        bool desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);

        IOrderedQueryable<UserRow> o = (sort?.ToLowerInvariant()) switch
        {
            "first"  => desc ? q.OrderByDescending(x => x.FirstName)         : q.OrderBy(x => x.FirstName),
            "email"  => desc ? q.OrderByDescending(x => x.Email)             : q.OrderBy(x => x.Email),
            "regs"   => desc ? q.OrderByDescending(x => x.RegistrationCount) : q.OrderBy(x => x.RegistrationCount),
            "family" => desc ? q.OrderByDescending(x => x.FamilyCount)       : q.OrderBy(x => x.FamilyCount),
            _        => desc ? q.OrderByDescending(x => x.LastName)          : q.OrderBy(x => x.LastName),
        };

        return o.ThenBy(x => x.UserId);
    }
}
