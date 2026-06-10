using System.Security.Claims;
using DfcEventRegistration.Web.Data;

namespace DfcEventRegistration.Web.Services;

/// <summary>
/// Аудит мутаций. Запись СТЕЙДЖИТСЯ в текущий AppDbContext (тот же scope, что и
/// AdminWriteService), поэтому сохраняется ВМЕСТЕ с изменением — в одной транзакции,
/// если вызвать до Commit/SaveChanges. Актёр берётся из claims текущего запроса.
/// </summary>
public sealed class AuditService
{
    private readonly AppDbContext _db;
    private readonly IHttpContextAccessor _http;

    public AuditService(AppDbContext db, IHttpContextAccessor http)
    {
        _db = db;
        _http = http;
    }

    /// <summary>Email текущего пользователя (для записи «кто сделал»).</summary>
    public string ActorEmail
    {
        get
        {
            var user = _http.HttpContext?.User;
            return user?.FindFirst(ClaimTypes.Email)?.Value
                ?? user?.Identity?.Name
                ?? "system";
        }
    }

    /// <summary>
    /// Добавить запись аудита в текущий DbContext (НЕ сохраняет — сохранит вызывающий
    /// своим SaveChanges/Commit, чтобы аудит был атомарен с изменением).
    /// </summary>
    public void Stage(string action, string? entityType = null, string? entityId = null, string? details = null)
    {
        _db.AuditLog.Add(new AuditEntry
        {
            WhenUtc = DateTime.UtcNow,
            ActorEmail = ActorEmail,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = Trim(details, 1024)
        });
    }

    private static string? Trim(string? s, int max)
        => s is { Length: > 0 } && s.Length > max ? s[..max] : s;
}
