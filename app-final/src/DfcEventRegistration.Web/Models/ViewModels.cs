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
