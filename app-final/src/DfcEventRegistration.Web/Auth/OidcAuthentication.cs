// =============================================================================
//  OIDC-СКАФФОЛД (черновик подключения реального IdP)
// -----------------------------------------------------------------------------
//  Сейчас вход — dev-заглушка (cookie). Когда появится реальный провайдер
//  (Entra ID / UAE Pass / любой OIDC), подключение СВОДИТСЯ к шагам ниже.
//  Код намеренно ЗАКОММЕНТИРОВАН, чтобы сборка не требовала NuGet-пакета,
//  пока IdP не выбран. Провайдеро-независимая часть (роль из таблицы AdminUsers
//  через IRoleResolver) уже готова и переиспользуется здесь.
//
//  ШАГ 1. Пакет:
//      dotnet add package Microsoft.AspNetCore.Authentication.OpenIdConnect
//
//  ШАГ 2. appsettings: заполнить секцию Auth:Oidc (Authority/ClientId/ClientSecret),
//          выставить Auth:Oidc:Enabled = true.
//
//  ШАГ 3. В Program.cs заменить вызов .AddCookie(...) на схему с двумя обработчиками:
//          cookie (сессия) + OpenIdConnect (challenge), и раскомментировать метод ниже.
//
//  ШАГ 4. Login.cshtml.cs: в проде вместо dev-формы делать Challenge("oidc").
// =============================================================================

/*
using DfcEventRegistration.Web.Auth;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using System.Security.Claims;

namespace DfcEventRegistration.Web.Auth;

public static class OidcAuthentication
{
    // Вызвать из Program.cs ВМЕСТО текущего AddAuthentication(...).AddCookie(...),
    // когда Auth:Oidc:Enabled = true.
    public static void AddDfcOidc(this IServiceCollection services, IConfiguration config)
    {
        services
            .AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme; // сессия
                options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme; // вход
            })
            .AddCookie(options =>
            {
                options.AccessDeniedPath = "/Account/Denied";
                options.Cookie.Name = "dfc.admin.auth";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = true;
            })
            .AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, options =>
            {
                options.Authority = config["Auth:Oidc:Authority"];          // напр. Entra/UAE Pass issuer
                options.ClientId = config["Auth:Oidc:ClientId"];
                options.ClientSecret = config["Auth:Oidc:ClientSecret"];
                options.ResponseType = "code";                              // authorization code + PKCE
                options.UsePkce = true;
                options.SaveTokens = true;
                options.GetClaimsFromUserInfoEndpoint = true;
                options.Scope.Clear();
                options.Scope.Add("openid");
                options.Scope.Add("profile");
                options.Scope.Add("email");
                options.CallbackPath = "/signin-oidc";
                options.SignedOutCallbackPath = "/signout-callback-oidc";

                // КЛЮЧЕВОЙ ШОВ: IdP отдаёт личность, роль приложения берём из AdminUsers.
                options.Events = new OpenIdConnectEvents
                {
                    OnTokenValidated = async ctx =>
                    {
                        var email = ctx.Principal?.FindFirst(ClaimTypes.Email)?.Value
                                 ?? ctx.Principal?.FindFirst("preferred_username")?.Value
                                 ?? ctx.Principal?.FindFirst("email")?.Value;

                        var resolver = ctx.HttpContext.RequestServices.GetRequiredService<IRoleResolver>();
                        var role = await resolver.ResolveRoleAsync(email, ctx.HttpContext.RequestAborted);

                        if (role is null)
                        {
                            // Нет записи в AdminUsers / истёк срок / деактивирован -> вход запрещён.
                            ctx.Fail("No application access for this account.");
                            return;
                        }

                        if (ctx.Principal!.Identity is ClaimsIdentity id)
                        {
                            id.AddClaim(new Claim(ClaimTypes.Role, role));
                            if (!string.IsNullOrEmpty(email) && id.FindFirst(ClaimTypes.Email) is null)
                                id.AddClaim(new Claim(ClaimTypes.Email, email));
                        }
                    }
                };
            });
    }
}
*/
