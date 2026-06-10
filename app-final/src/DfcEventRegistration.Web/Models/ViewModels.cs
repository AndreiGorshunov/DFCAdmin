using DfcEventRegistration.Web.Data;

namespace DfcEventRegistration.Web.Models;

/// <summary>Одна строка листинга — грейн = регистрация (registrant на событие).</summary>
public class RegistrantRow
{
    public long RegistrationId { get; set; }
    public string Email { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string? Mobile { get; set; }
    public string? GroupCode { get; set; }
    public string EventName { get; set; } = "";
    public RegistrationStatus Status { get; set; }

    /// <summary>Status == CheckedIn. В спеке = "Collected bibs" / "Checked in at venue".</summary>
    public bool CheckedIn { get; set; }

    public int KidsBelow13 { get; set; }
    public int KidsAbove13 { get; set; }
}

public class RegistrantFilter
{
    public string? Q { get; set; }
    public Guid? EventId { get; set; }
    public RegistrationStatus? Status { get; set; }

    /// <summary>Фильтр по типу участника регистрации (EXISTS по RegistrationParticipants).</summary>
    public ParticipantKind? ParticipantType { get; set; }
}

/// <summary>
/// Тип участника в рамках регистрации (по RegistrationParticipants.FamilyMemberId):
/// Adults = есть участник с NULL (сам регистрант), Children = есть участник с NOT NULL (ребёнок).
/// </summary>
public enum ParticipantKind
{
    Adults,
    Children
}

/// <summary>Возрастная группа ребёнка (на дату начала события).</summary>
public enum AgeBand
{
    Under13,
    From13
}

/// <summary>Строка вкладки «Children» — грейн = участник-ребёнок (RegistrationParticipants, FamilyMemberId != null).</summary>
public class ChildRow
{
    public long ParticipantId { get; set; }
    public long RegistrationId { get; set; }
    public string ChildFirstName { get; set; } = "";
    public string ChildLastName { get; set; } = "";
    public int? Age { get; set; }
    public string EventName { get; set; } = "";
    public string? TshirtSize { get; set; }
    public string ParentFirstName { get; set; } = "";
    public string ParentLastName { get; set; } = "";
    public string ParentEmail { get; set; } = "";
    public string? GroupCode { get; set; }
}

public class ChildFilter
{
    public string? Q { get; set; }
    public Guid? EventId { get; set; }
    public AgeBand? Age { get; set; }
}

public record EventOption(Guid Id, string Name);

public class TshirtReportRow
{
    public string Size { get; set; } = "";
    public int Requested { get; set; }
    public int Collected { get; set; }
    public int? Stock { get; set; }

    public int OutstandingToCollect => Requested - Collected;
    public int? StockAfterCollection => Stock.HasValue ? Stock.Value - Collected : null;
}

// ===================== Users tab (грейн = пользователь/персона) =====================

public class UserRow
{
    public int UserId { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public string? Phone { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public int FamilyCount { get; set; }        // членов семьи в ростере
    public int RegistrationCount { get; set; }  // регистраций персоны
}

public class UserFilter
{
    public string? Q { get; set; }
}
