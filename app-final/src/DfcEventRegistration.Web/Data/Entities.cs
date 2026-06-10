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

    // Вычисляемая (PERSISTED) колонка в БД: цифры телефона в обратном порядке.
    // Делает поиск «телефон оканчивается на N цифр» префиксным (seek по IX_Users_PhoneDigitsRev).
    // Только чтение: значение поддерживает БД (HasComputedColumnSql), EF не пишет.
    public string? PhoneDigitsRev { get; private set; }
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
    public string RegistrantLastName { get; set; } = ""; // денормализация (Вариант B): синхронизируется из Users.LastName при записи

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

/// <summary>
/// Кто имеет доступ в админку и с какой ролью. Источник истины для ролей
/// (IdP аутентифицирует личность, роль приложения назначается здесь).
/// Сотрудник DFC выдаёт доступ партнёрам (GrantedBy/ExpiresAtUtc).
/// </summary>
public class AdminUser
{
    public int AdminUserId { get; set; }
    public string Email { get; set; } = "";
    public string Role { get; set; } = "";            // 'Admin' | 'Partner'
    public string? DisplayName { get; set; }
    public bool IsActive { get; set; } = true;
    public string? GrantedBy { get; set; }            // email сотрудника, выдавшего доступ
    public DateTime GrantedAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }        // null = бессрочно (для партнёров обычно ограничивают)
}

/// <summary>Журнал аудита: кто/когда/что сделал (все мутации/удаления).</summary>
public class AuditEntry
{
    public long AuditId { get; set; }
    public DateTime WhenUtc { get; set; }
    public string? ActorEmail { get; set; }
    public string Action { get; set; } = "";          // напр. 'Registration.Delete'
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public string? Details { get; set; }
}
