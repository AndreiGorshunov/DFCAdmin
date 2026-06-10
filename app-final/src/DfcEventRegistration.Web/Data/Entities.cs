namespace DfcEventRegistration.Web.Data;

/// <summary>
/// Семантика поля EventRegistrations.Status (tinyint). В схеме это просто число —
/// здесь фиксируем смысл. CheckedIn используется и как "bib collected"
/// (Run/Ride/Speed Laps), и как "checked in at venue" (SUP/Yoga): в схеме нет
/// EventType, который разделил бы эти два сценария.
/// </summary>
public enum RegistrationStatus : byte
{
    Pending = 0,
    Confirmed = 1,
    CheckedIn = 2,
    Cancelled = 3
}

public class User
{
    public int UserId { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public string? Phone { get; set; }
    public DateTime? DateOfBirth { get; set; }
}

public class Event
{
    public Guid Id { get; set; }                      // из внешней системы
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public DateTime? RegistrationOpeningDate { get; set; }
}

public class FamilyMember
{
    public int FamilyMemberId { get; set; }
    public int UserId { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public DateTime? DateOfBirth { get; set; }
}

public class EventRegistration
{
    public long RegistrationId { get; set; }
    public Guid EventId { get; set; }
    public int UserId { get; set; }
    public string? GroupCode { get; set; }
    public string? EmergencyContactFirstName { get; set; }
    public string? EmergencyContactLastName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public DateTime RegistrationDate { get; set; }
    public RegistrationStatus Status { get; set; }
    public string? QRCode { get; set; }
    //public string RegistrantLastName { get; set; } // если включаешь денормализацию

    // Навигации (нужны для фильтрации по пользователю и подсчёта детей)
    public User User { get; set; } = null!;
    public Event Event { get; set; } = null!;
    public ICollection<RegistrationParticipant> Participants { get; set; } = new List<RegistrationParticipant>();
}

public class RegistrationParticipant
{
    public long ParticipantId { get; set; }
    public long RegistrationId { get; set; }
    public Guid EventId { get; set; }                 // денормализован
    public int? FamilyMemberId { get; set; }          // NULL = родитель, NOT NULL = ребёнок
    public string? TshirtSize { get; set; }

    public EventRegistration Registration { get; set; } = null!;
    public FamilyMember? FamilyMember { get; set; }
}
