using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

namespace DfcEventRegistration.Web.Auth;

/// <summary>
/// ШОВ ДЛЯ РЕАЛЬНОГО IdP. Внешний провайдер (OIDC/SAML/UAE Pass) аутентифицирует
/// сотрудника и отдаёт email, но НЕ роль нашего приложения. Здесь по email из
/// allowlist (конфиг Auth:Users) добавляем роль приложения.
///
/// Идемпотентно и безопасно для dev-логина: если роль уже есть в principal
/// (dev-логин её ставит) — ничего не делаем. Запускается на каждый запрос, поэтому
/// держим дёшево и без обращений к БД (позже источник можно заменить на таблицу
/// AdminUsers за тем же интерфейсом).
/// </summary>
public sealed class AllowlistRoleTransformation : IClaimsTransformation
{
    private readonly IConfiguration _config;
    public AllowlistRoleTransformation(IConfiguration config) => _config = config;

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
            return Task.FromResult(principal);

        // Роль уже назначена (dev-логин или прошлый проход) — выходим.
        if (principal.FindFirst(ClaimTypes.Role) is not null)
            return Task.FromResult(principal);

        var email = principal.FindFirst(ClaimTypes.Email)?.Value
                 ?? principal.FindFirst("email")?.Value
                 ?? principal.FindFirst(ClaimTypes.Name)?.Value;
        if (string.IsNullOrWhiteSpace(email))
            return Task.FromResult(principal);

        // Allowlist: Auth:Users:<email> = "Admin" | "Partner".
        // ':' — разделитель ключей конфигурации; точки/'@' внутри email допустимы как литерал.
        var mapped = Roles.Normalize(_config[$"Auth:Users:{email}"]);
        if (mapped is not null && principal.Identity is ClaimsIdentity id)
            id.AddClaim(new Claim(ClaimTypes.Role, mapped));

        return Task.FromResult(principal);
    }
}
