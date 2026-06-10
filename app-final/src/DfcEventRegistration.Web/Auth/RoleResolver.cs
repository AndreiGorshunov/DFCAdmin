using DfcEventRegistration.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace DfcEventRegistration.Web.Auth;

/// <summary>
/// Резолвит роль приложения по email из таблицы AdminUsers (источник истины).
/// Используется в момент ВХОДА (dev-логин сейчас; OIDC OnTokenValidated позже) —
/// роль кладётся в claim и далее живёт в cookie, без обращения к БД на каждый запрос.
/// </summary>
public interface IRoleResolver
{
    Task<string?> ResolveRoleAsync(string? email, CancellationToken ct = default);
}

public sealed class RoleResolver : IRoleResolver
{
    private readonly AppDbContext _db;
    public RoleResolver(AppDbContext db) => _db = db;

    public async Task<string?> ResolveRoleAsync(string? email, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;

        var now = DateTime.UtcNow;
        var role = await _db.AdminUsers.AsNoTracking()
            .Where(a => a.Email == email
                     && a.IsActive
                     && (a.ExpiresAtUtc == null || a.ExpiresAtUtc > now))
            .Select(a => a.Role)
            .FirstOrDefaultAsync(ct);

        // Нормализуем к канонической роли (или null, если в таблице мусор/нет записи).
        return Roles.Normalize(role);
    }
}
