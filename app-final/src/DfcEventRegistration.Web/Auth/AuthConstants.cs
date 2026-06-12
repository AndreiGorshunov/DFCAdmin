namespace DfcEventRegistration.Web.Auth;

/// <summary>
/// Роли приложения. НЕ зависят от провайдера: IdP отдаёт личность (email/имя/EID),
/// а роль приложения мы назначаем сами (dev-логин — вручную, прод — allowlist email->роль).
/// </summary>
public static class Roles
{
    /// <summary>Полный доступ: просмотр, поиск, отчёты, создание/правка/удаление.</summary>
    public const string Admin = "Admin";

    /// <summary>Партнёр (спонсор и т.п.): только просмотр/поиск и выгрузка отчётов.</summary>
    public const string Partner = "Partner";

    /// <summary>Персонал чек-ина на площадке: просмотр/поиск + отметка чек-ина на сессии.
    /// Без права правок/удаления (это CanManage — только Admin).</summary>
    public const string Steward = "Steward";

    public static readonly string[] All = { Admin, Partner, Steward };

    /// <summary>Привести ввод к канонической роли (case-insensitive) или null, если не из списка.</summary>
    public static string? Normalize(string? role) 
        => All.FirstOrDefault(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Политики по ДЕЙСТВИЯМ (а не по ролям) — страницы/хендлеры ссылаются на политику,
/// а маппинг "политика -> роли" живёт в одном месте (Program.cs). При появлении новых
/// ролей правим только маппинг, а не разметку страниц.
/// </summary>
public static class Policies
{
    /// <summary>Просмотр/поиск/отчёты. Admin + Partner + Steward.</summary>
    public const string CanView = "CanView";

    /// <summary>Создание/правка/удаление (вся danger-zone). Только Admin.</summary>
    public const string CanManage = "CanManage";

    /// <summary>Отметка чек-ина на сессии (отдельно от CanManage). Admin + Steward.</summary>
    public const string CanCheckIn = "CanCheckIn";
}
