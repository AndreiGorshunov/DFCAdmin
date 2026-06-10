using DfcEventRegistration.Web.Auth;
using DfcEventRegistration.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace DfcEventRegistration.Web.Services;

/// <summary>
/// Все мутации/удаления. FK в схеме = NO ACTION (multiple cascade paths не дают
/// поставить ON DELETE CASCADE), поэтому каскады делаем вручную в нужном порядке
/// внутри транзакции и set-based через ExecuteDelete (без загрузки в память).
///
/// Аудит: каждая мутация стейджит запись в AuditLog ВНУТРИ той же транзакции
/// (для tx-методов — перед Commit), чтобы «кто/что сделал» было атомарно с изменением.
/// </summary>
public class AdminWriteService
{
    private readonly AppDbContext _db;
    private readonly AuditService _audit;

    public AdminWriteService(AppDbContext db, AuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    /// <summary>Канонические размеры футболок (UI-список). В схеме TshirtSize — свободная строка.</summary>
    public static readonly string[] TshirtSizes = { "XS", "S", "M", "L", "XL", "XXL" };

    /// <summary>Бизнес-правило: не более 3 членов семьи (детей) на одного пользователя.
    /// Источник истины — серверная проверка в AddFamilyMemberAsync; UI лишь дублирует подсказкой/блокировкой.</summary>
    public const int MaxChildrenPerUser = 3;

    private static string? NormalizeSize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = s.Trim();
        var upper = t.ToUpperInvariant();
        return TshirtSizes.Contains(upper) ? upper : t;
    }

    /// <summary>Возраст совершеннолетия родителя/пользователя.</summary>
    public const int AdultAge = 18;

    /// <summary>Валидация даты рождения: не в будущем; для пользователя (родителя) — 18+ на сегодня.
    /// Null допускается (DOB необязателен). Возвращает текст ошибки или null.</summary>
    private static string? ValidateDob(DateTime? dob, bool requireAdult)
    {
        if (dob is null) return null;
        var today = DateTime.Today;
        if (dob.Value.Date > today) return "Date of birth cannot be in the future.";
        if (requireAdult && dob.Value.Date > today.AddYears(-AdultAge))
            return $"The user (parent) must be at least {AdultAge} years old.";
        return null;
    }

    // ======================= Registrant (User + Registration) =======================

    public record RegistrantEdit(
        int UserId, string FirstName, string LastName, string Email, string? Phone, DateTime? DateOfBirth,
        long RegistrationId, string? GroupCode, RegistrationStatus Status,
        string? EmergencyContactFirstName, string? EmergencyContactLastName, string? EmergencyContactPhone);

    public async Task<(bool ok, string? error)> UpdateRegistrantAsync(RegistrantEdit e, CancellationToken ct = default)
    {
        // UX_Users_Email — проверяем до сохранения, чтобы вернуть дружелюбную ошибку.
        bool dupe = await _db.Users.AnyAsync(u => u.Email == e.Email && u.UserId != e.UserId, ct);
        if (dupe) return (false, "This email is already used by another registrant.");

        if (ValidateDob(e.DateOfBirth, requireAdult: true) is string dobErr) return (false, dobErr);

        var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == e.UserId, ct);
        var reg = await _db.EventRegistrations.FirstOrDefaultAsync(r => r.RegistrationId == e.RegistrationId, ct);
        if (user is null || reg is null) return (false, "Record not found.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // ВАЖНО: правка имени/email/телефона меняет ПЕРСОНУ — отражается во всех её регистрациях.
        user.FirstName = e.FirstName;
        user.LastName = e.LastName;
        user.Email = e.Email;
        user.Phone = e.Phone;
        user.DateOfBirth = e.DateOfBirth;

        reg.GroupCode = e.GroupCode;
        reg.Status = e.Status;
        reg.EmergencyContactFirstName = e.EmergencyContactFirstName;
        reg.EmergencyContactLastName = e.EmergencyContactLastName;
        reg.EmergencyContactPhone = e.EmergencyContactPhone;

        await _db.SaveChangesAsync(ct);

        // Денормализация (Вариант B): держим RegistrantLastName в актуальном состоянии
        // во ВСЕХ регистрациях персоны (листинг сортируется/сикается по этой колонке). Set-based.
        await _db.EventRegistrations
            .Where(r => r.UserId == e.UserId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.RegistrantLastName, e.LastName), ct);

        _audit.Stage("Registrant.Update", "EventRegistration", e.RegistrationId.ToString(), $"userId={e.UserId}");
        await _db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);
        return (true, null);
    }

    /// <summary>Удалить ОДНУ регистрацию: участники -> регистрация.</summary>
    public async Task DeleteRegistrationAsync(long registrationId, CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        await _db.RegistrationParticipants
            .Where(p => p.RegistrationId == registrationId)
            .ExecuteDeleteAsync(ct);

        await _db.EventRegistrations
            .Where(r => r.RegistrationId == registrationId)
            .ExecuteDeleteAsync(ct);

        _audit.Stage("Registration.Delete", "EventRegistration", registrationId.ToString());
        await _db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);
    }

    /// <summary>Удалить ПЕРСОНУ целиком со всеми зависимостями.</summary>
    public async Task DeleteUserCascadeAsync(int userId, CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var regIds = _db.EventRegistrations.Where(r => r.UserId == userId).Select(r => r.RegistrationId);
        var fmIds = _db.FamilyMembers.Where(f => f.UserId == userId).Select(f => f.FamilyMemberId);

        // 1) участники регистраций этого пользователя
        await _db.RegistrationParticipants
            .Where(p => regIds.Contains(p.RegistrationId))
            .ExecuteDeleteAsync(ct);

        // 2) защитно: участники, ссылающиеся на его членов семьи из ЧУЖИХ регистраций
        //    (инвариант "ребёнок только в регистрации своего родителя" схемой не гарантирован)
        await _db.RegistrationParticipants
            .Where(p => p.FamilyMemberId != null && fmIds.Contains(p.FamilyMemberId.Value))
            .ExecuteDeleteAsync(ct);

        // 3) регистрации -> 4) члены семьи -> 5) сам пользователь
        await _db.EventRegistrations.Where(r => r.UserId == userId).ExecuteDeleteAsync(ct);
        await _db.FamilyMembers.Where(f => f.UserId == userId).ExecuteDeleteAsync(ct);
        await _db.Users.Where(u => u.UserId == userId).ExecuteDeleteAsync(ct);

        _audit.Stage("User.DeleteCascade", "User", userId.ToString());
        await _db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);
    }

    public async Task<int> UserIdOfRegistrationAsync(long registrationId, CancellationToken ct = default)
        => await _db.EventRegistrations.Where(r => r.RegistrationId == registrationId)
                   .Select(r => r.UserId).FirstOrDefaultAsync(ct);

    // ============================= Users (CRUD) =============================

    public record UserEdit(int UserId, string FirstName, string LastName, string Email, string? Phone, DateTime? DateOfBirth);

    public async Task<(bool ok, string? error, int id)> CreateUserAsync(
        string firstName, string lastName, string email, string? phone, DateTime? dob, CancellationToken ct = default)
    {
        bool dupe = await _db.Users.AnyAsync(u => u.Email == email, ct);  // UX_Users_Email
        if (dupe) return (false, "This email is already in use.", 0);

        if (ValidateDob(dob, requireAdult: true) is string dobErr) return (false, dobErr, 0);

        var u = new User { FirstName = firstName, LastName = lastName, Email = email, Phone = phone, DateOfBirth = dob };
        _db.Users.Add(u);
        await _db.SaveChangesAsync(ct);

        _audit.Stage("User.Create", "User", u.UserId.ToString(), $"email={email}");
        await _db.SaveChangesAsync(ct);
        return (true, null, u.UserId);
    }

    public async Task<(bool ok, string? error)> UpdateUserAsync(UserEdit e, CancellationToken ct = default)
    {
        bool dupe = await _db.Users.AnyAsync(u => u.Email == e.Email && u.UserId != e.UserId, ct);
        if (dupe) return (false, "This email is already used by another user.");

        if (ValidateDob(e.DateOfBirth, requireAdult: true) is string dobErr) return (false, dobErr);

        var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == e.UserId, ct);
        if (user is null) return (false, "User not found.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // Правка персоны отражается во всех её регистрациях.
        user.FirstName = e.FirstName;
        user.LastName = e.LastName;
        user.Email = e.Email;
        user.Phone = e.Phone;
        user.DateOfBirth = e.DateOfBirth;

        await _db.SaveChangesAsync(ct);

        // Денормализация (Вариант B): синхронизируем RegistrantLastName во всех регистрациях персоны.
        await _db.EventRegistrations
            .Where(r => r.UserId == e.UserId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.RegistrantLastName, e.LastName), ct);

        _audit.Stage("User.Update", "User", e.UserId.ToString());
        await _db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);
        return (true, null);
    }

    // ============================= Events (CRUD) =============================

    public record EventEdit(Guid? Id, string Name, string? Description,
        DateTime StartDate, DateTime EndDate, DateTime? RegistrationOpeningDate);

    public async Task<(bool ok, string? error, Guid id)> UpsertEventAsync(EventEdit e, CancellationToken ct = default)
    {
        if (e.EndDate < e.StartDate)
            return (false, "End date must be on or after the start date.", Guid.Empty);
        if (e.RegistrationOpeningDate is DateTime open && open > e.StartDate)
            return (false, "Registration opening must be on or before the start date.", Guid.Empty);

        Event ev;
        bool isNew;
        if (e.Id is Guid id && id != Guid.Empty)
        {
            var existing = await _db.Events.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (existing is null) return (false, "Event not found.", Guid.Empty);
            ev = existing;
            isNew = false;
        }
        else
        {
            // Id "из внешней системы" — при ручном создании генерим новый GUID.
            ev = new Event { Id = Guid.NewGuid() };
            _db.Events.Add(ev);
            isNew = true;
        }

        ev.Name = e.Name;
        ev.Description = e.Description;
        ev.StartDate = e.StartDate;
        ev.EndDate = e.EndDate;
        ev.RegistrationOpeningDate = e.RegistrationOpeningDate;

        await _db.SaveChangesAsync(ct);

        _audit.Stage(isNew ? "Event.Create" : "Event.Update", "Event", ev.Id.ToString(), $"name={e.Name}");
        await _db.SaveChangesAsync(ct);
        return (true, null, ev.Id);
    }

    public async Task<(int registrations, int participants)> EventImpactAsync(Guid eventId, CancellationToken ct = default)
    {
        int regs = await _db.EventRegistrations.CountAsync(r => r.EventId == eventId, ct);
        int parts = await _db.RegistrationParticipants.CountAsync(p => p.EventId == eventId, ct);
        return (regs, parts);
    }

    /// <summary>Каскадное удаление события: участники (по денормализованному EventId) -> регистрации -> событие.</summary>
    public async Task DeleteEventCascadeAsync(Guid eventId, CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        await _db.RegistrationParticipants.Where(p => p.EventId == eventId).ExecuteDeleteAsync(ct);
        await _db.EventRegistrations.Where(r => r.EventId == eventId).ExecuteDeleteAsync(ct);
        await _db.Events.Where(e => e.Id == eventId).ExecuteDeleteAsync(ct);

        _audit.Stage("Event.DeleteCascade", "Event", eventId.ToString());
        await _db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);
    }

    // ===================== Participants of a registration =====================
    // RegistrationParticipants = кто реально идёт на КОНКРЕТНУЮ регистрацию.
    // Это НЕ ростер семьи: ребёнок может идти только на одно событие/регистрацию.

    public async Task<(bool ok, string? error)> AddParticipantAsync(
        long registrationId, int familyMemberId, string? tshirtSize, CancellationToken ct = default)
    {
        var reg = await _db.EventRegistrations.AsNoTracking()
            .Where(r => r.RegistrationId == registrationId)
            .Select(r => new { r.EventId, r.UserId })
            .FirstOrDefaultAsync(ct);
        if (reg is null) return (false, "Registration not found.");

        var fmOwner = await _db.FamilyMembers.Where(f => f.FamilyMemberId == familyMemberId)
            .Select(f => (int?)f.UserId).FirstOrDefaultAsync(ct);
        if (fmOwner is null) return (false, "Family member not found.");
        if (fmOwner.Value != reg.UserId) return (false, "Family member belongs to a different registrant.");

        bool already = await _db.RegistrationParticipants
            .AnyAsync(p => p.RegistrationId == registrationId && p.FamilyMemberId == familyMemberId, ct);
        if (already) return (false, "This family member is already a participant of this registration.");

        _db.RegistrationParticipants.Add(new RegistrationParticipant
        {
            RegistrationId = registrationId,
            EventId = reg.EventId,                 // денормализованный EventId
            FamilyMemberId = familyMemberId,
            TshirtSize = NormalizeSize(tshirtSize)
        });
        _audit.Stage("Participant.Add", "Registration", registrationId.ToString(), $"familyMemberId={familyMemberId}");
        await _db.SaveChangesAsync(ct);
        return (true, null);
    }

    /// <summary>Убрать участника ИЗ ЭТОЙ регистрации (член семьи в ростере остаётся).</summary>
    public async Task RemoveParticipantAsync(long participantId, CancellationToken ct = default)
    {
        await _db.RegistrationParticipants
            .Where(p => p.ParticipantId == participantId)
            .ExecuteDeleteAsync(ct);

        _audit.Stage("Participant.Remove", "Participant", participantId.ToString());
        await _db.SaveChangesAsync(ct);
    }

    public async Task SetParticipantTshirtAsync(long participantId, string? size, CancellationToken ct = default)
    {
        var p = await _db.RegistrationParticipants.FirstOrDefaultAsync(x => x.ParticipantId == participantId, ct);
        if (p is null) return;
        p.TshirtSize = NormalizeSize(size);
        _audit.Stage("Participant.SetTshirt", "Participant", participantId.ToString(), $"size={p.TshirtSize}");
        await _db.SaveChangesAsync(ct);
    }

    // ============================ Family members ============================

    public record FamilyMemberEdit(int FamilyMemberId, string FirstName, string LastName, DateTime? DateOfBirth);

    public async Task<(bool ok, string? error)> UpdateFamilyMemberAsync(FamilyMemberEdit e, CancellationToken ct = default)
    {
        var fm = await _db.FamilyMembers.FirstOrDefaultAsync(f => f.FamilyMemberId == e.FamilyMemberId, ct);
        if (fm is null) return (false, "Family member not found.");

        if (ValidateDob(e.DateOfBirth, requireAdult: false) is string dobErr) return (false, dobErr);

        fm.FirstName = e.FirstName;
        fm.LastName = e.LastName;
        fm.DateOfBirth = e.DateOfBirth;

        _audit.Stage("FamilyMember.Update", "FamilyMember", e.FamilyMemberId.ToString());
        await _db.SaveChangesAsync(ct);
        return (true, null);
    }

    public async Task<int> FamilyCountAsync(int userId, CancellationToken ct = default)
        => await _db.FamilyMembers.CountAsync(f => f.UserId == userId, ct);

    public async Task<(bool ok, string? error)> AddFamilyMemberAsync(int userId, string firstName, string lastName,
        DateTime? dateOfBirth, CancellationToken ct = default)
    {
        // Серверный гард на лимит — единственный надёжный (UI можно обойти).
        int count = await _db.FamilyMembers.CountAsync(f => f.UserId == userId, ct);
        if (count >= MaxChildrenPerUser)
            return (false, $"This person already has the maximum of {MaxChildrenPerUser} family members. " +
                           "Remove one before adding another.");

        if (ValidateDob(dateOfBirth, requireAdult: false) is string dobErr) return (false, dobErr);

        _db.FamilyMembers.Add(new FamilyMember
        {
            UserId = userId,
            FirstName = firstName,
            LastName = lastName,
            DateOfBirth = dateOfBirth
        });
        _audit.Stage("FamilyMember.Add", "User", userId.ToString());
        await _db.SaveChangesAsync(ct);
        return (true, null);
    }

    /// <summary>Удалить члена семьи: сперва убрать его из всех регистраций (участники), затем строку.</summary>
    public async Task DeleteFamilyMemberAsync(int familyMemberId, CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        await _db.RegistrationParticipants.Where(p => p.FamilyMemberId == familyMemberId).ExecuteDeleteAsync(ct);
        await _db.FamilyMembers.Where(f => f.FamilyMemberId == familyMemberId).ExecuteDeleteAsync(ct);

        _audit.Stage("FamilyMember.Delete", "FamilyMember", familyMemberId.ToString());
        await _db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);
    }

    public async Task<int> UserIdOfFamilyMemberAsync(int familyMemberId, CancellationToken ct = default)
        => await _db.FamilyMembers.Where(f => f.FamilyMemberId == familyMemberId)
                   .Select(f => f.UserId).FirstOrDefaultAsync(ct);

    // ============================ Admin users (доступ) ============================
    // Сотрудник DFC выдаёт/отзывает доступ (в т.ч. партнёрам). Всё пишется в аудит.

    public record AdminGrant(string Email, string Role, string? DisplayName, DateTime? ExpiresAtUtc);

    public async Task<(bool ok, string? error)> GrantAccessAsync(AdminGrant g, CancellationToken ct = default)
    {
        var role = Roles.Normalize(g.Role);
        if (role is null) return (false, "Unknown role.");

        var email = g.Email?.Trim();
        if (string.IsNullOrWhiteSpace(email)) return (false, "Email is required.");

        var existing = await _db.AdminUsers.FirstOrDefaultAsync(a => a.Email == email, ct);
        if (existing is null)
        {
            _db.AdminUsers.Add(new AdminUser
            {
                Email = email,
                Role = role,
                DisplayName = string.IsNullOrWhiteSpace(g.DisplayName) ? null : g.DisplayName!.Trim(),
                IsActive = true,
                GrantedBy = _audit.ActorEmail,
                GrantedAtUtc = DateTime.UtcNow,
                ExpiresAtUtc = g.ExpiresAtUtc
            });
            _audit.Stage("AdminUser.Grant", "AdminUser", email, $"role={role}");
        }
        else
        {
            existing.Role = role;
            existing.DisplayName = string.IsNullOrWhiteSpace(g.DisplayName) ? null : g.DisplayName!.Trim();
            existing.IsActive = true;
            existing.GrantedBy = _audit.ActorEmail;
            existing.GrantedAtUtc = DateTime.UtcNow;
            existing.ExpiresAtUtc = g.ExpiresAtUtc;
            _audit.Stage("AdminUser.Update", "AdminUser", email, $"role={role}");
        }

        await _db.SaveChangesAsync(ct);
        return (true, null);
    }

    public async Task SetAdminActiveAsync(int adminUserId, bool active, CancellationToken ct = default)
    {
        var a = await _db.AdminUsers.FirstOrDefaultAsync(x => x.AdminUserId == adminUserId, ct);
        if (a is null) return;
        a.IsActive = active;
        _audit.Stage(active ? "AdminUser.Reactivate" : "AdminUser.Revoke", "AdminUser", a.Email);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAdminUserAsync(int adminUserId, CancellationToken ct = default)
    {
        var email = await _db.AdminUsers.Where(x => x.AdminUserId == adminUserId)
            .Select(x => x.Email).FirstOrDefaultAsync(ct);
        if (email is null) return;

        await _db.AdminUsers.Where(x => x.AdminUserId == adminUserId).ExecuteDeleteAsync(ct);
        _audit.Stage("AdminUser.Delete", "AdminUser", email);
        await _db.SaveChangesAsync(ct);
    }
}
