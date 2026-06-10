using DfcEventRegistration.Web.Auth;
using DfcEventRegistration.Web.Data;
using DfcEventRegistration.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ----------------------------- Razor Pages + RBAC-гейтинг --------------------------------
// Гейтинг страниц держим в ОДНОМ месте (конвенции), а не атрибутами по классам:
// проще аудитить и видно всю карту доступа сразу.
var razorPages = builder.Services.AddRazorPages(options =>
{
    // Secure-by-default: каждая страница требует минимум CanView (просмотр/поиск/отчёты).
    options.Conventions.AuthorizeFolder("/", Policies.CanView);

    // Управление (Admin): создание/правка/удаление. Накладывается поверх CanView (AND);
    // Admin входит в обе политики, Partner — только в CanView, поэтому сюда не пройдёт.
    options.Conventions.AuthorizePage("/Registrants/Edit", Policies.CanManage);
    options.Conventions.AuthorizePage("/Users/Create", Policies.CanManage);
    options.Conventions.AuthorizePage("/Users/Edit", Policies.CanManage);
    options.Conventions.AuthorizeFolder("/Events", Policies.CanManage);
    options.Conventions.AuthorizePage("/FamilyMembers/Edit", Policies.CanManage);
    options.Conventions.AuthorizeFolder("/Admins", Policies.CanManage);
    options.Conventions.AuthorizeFolder("/Audit", Policies.CanManage);

    // Вход/отказ/выход — без авторизации (иначе редирект-петля на логине).
    options.Conventions.AllowAnonymousToPage("/Account/Login");
    options.Conventions.AllowAnonymousToPage("/Account/Denied");
    options.Conventions.AllowAnonymousToPage("/Account/Logout");
});

// В Development включаем Razor Runtime Compilation: правки .cshtml применяются
// без пересборки/перезапуска (refresh в браузере достаточно). В проде НЕ включаем —
// там вьюхи компилируются в сборку (быстрее старт, нет лишней зависимости и file watcher'а).
if (builder.Environment.IsDevelopment())
{
    razorPages.AddRazorRuntimeCompilation();
}

// ----------------------------- Аутентификация (cookie-сессия) ----------------------------
// Cookie — это ЛОКАЛЬНАЯ сессия приложения. Она не зависит от провайдера: когда появится
// реальный IdP (OIDC/SAML/UAE Pass), он добавится отдельной внешней схемой, а cookie
// останется как сессия (внешняя схема нужна только на этапе challenge/login).
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/Denied";
        options.Cookie.Name = "dfc.admin.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        // В Development допускаем http (иначе secure-cookie не отправляется по http и вход зацикливается).
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });

// Резолв роли по email из таблицы AdminUsers (при входе: dev-логин и будущий OIDC — см. OidcAuthentication.cs).
builder.Services.AddScoped<IRoleResolver, RoleResolver>();

// Аудит мутаций (актёр берётся из claims текущего запроса).
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AuditService>();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(Policies.CanView, p => p.RequireRole(Roles.Admin, Roles.Partner));
    options.AddPolicy(Policies.CanManage, p => p.RequireRole(Roles.Admin));

    // Любой не покрытый конвенцией эндпоинт всё равно требует входа.
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

// ----------------------------- Данные и сервисы ------------------------------------------
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("Default"),
        sql => sql.CommandTimeout(120)));

builder.Services.AddScoped<UserSearchService>();
builder.Services.AddScoped<RegistrantQueryService>();
builder.Services.AddScoped<UserQueryService>();
builder.Services.AddScoped<ChildQueryService>();
builder.Services.AddScoped<TshirtReportService>();
builder.Services.AddScoped<ExcelExportService>();
builder.Services.AddScoped<StreamingExportService>();
builder.Services.AddScoped<AdminWriteService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();

app.Run();
