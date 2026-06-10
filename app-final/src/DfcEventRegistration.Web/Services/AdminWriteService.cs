using DfcEventRegistration.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace DfcEventRegistration.Web.Services;

/// <summary>
/// Все мутации/удаления. FK в схеме = NO ACTION (multiple cascade paths не дают
/// поставить ON DELETE CASCADE), поэтому каскады делаем вручную в нужном порядке
/// внутри транзакции и set-based через ExecuteDelete (без загрузки в память).
/// </summary>
public class AdminWriteService
{
    private readonly AppDbContext _db;
    public AdminWriteService(AppDbContext db) => _db = db;

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

        var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == e.UserId, ct);
        var reg = await _db.EventRegistrations.FirstOrDefaultAsync(r => r.RegistrationId == e.RegistrationId, ct);
        if (user is null || reg is null) return (false, "Record not found.");

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

        var u = new User { FirstName = firstName, LastName = lastName, Email = email, Phone = phone, DateOfBirth = dob };
        _db.Users.Add(u);
        await _db.SaveChangesAsync(ct);
        return (true, null, u.UserId);
    }

    public async Task<(bool ok, string? error)> UpdateUserAsync(UserEdit e, CancellationToken ct = default)
    {
        bool dupe = await _db.Users.AnyAsync(u => u.Email == e.Email && u.UserId != e.UserId, ct);
        if (dupe) return (false, "This email is already used by another user.");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == e.UserId, ct);
        if (user is null) return (false, "User not found.");

        // Правка персоны отражается во всех её регистрациях.
        user.FirstName = e.FirstName;
        user.LastName = e.LastName;
        user.Email = e.Email;
        user.Phone = e.Phone;
        user.DateOfBirth = e.DateOfBirth;

        await _db.SaveChangesAsync(ct);
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
        if (e.Id is Guid id && id != Guid.Empty)
        {
            var existing = await _db.Events.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (existing is null) return (false, "Event not found.", Guid.Empty);
            ev = existing;
        }
        else
        {
            // Id "из внешней системы" — при ручном создании генерим новый GUID.
            ev = new Event { Id = Guid.NewGuid() };
            _db.Events.Add(ev);
        }

        ev.Name = e.Name;
        ev.Description = e.Description;
        ev.StartDate = e.StartDate;
        ev.EndDate = e.EndDate;
        ev.RegistrationOpeningDate = e.RegistrationOpeningDate;

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
        await _db.SaveChangesAsync(ct);
        return (true, null);
    }

    /// <summary>Убрать участника ИЗ ЭТОЙ регистрации (член семьи в ростере остаётся).</summary>
    public async Task RemoveParticipantAsync(long participantId, CancellationToken ct = default)
        => await _db.RegistrationParticipants
            .Where(p => p.ParticipantId == participantId)
            .ExecuteDeleteAsync(ct);

    public async Task SetParticipantTshirtAsync(long participantId, string? size, CancellationToken ct = default)
    {
        var p = await _db.RegistrationParticipants.FirstOrDefaultAsync(x => x.ParticipantId == participantId, ct);
        if (p is null) return;
        p.TshirtSize = NormalizeSize(size);
        await _db.SaveChangesAsync(ct);
    }

    // ============================ Family members ============================

    public record FamilyMemberEdit(int FamilyMemberId, string FirstName, string LastName, DateTime? DateOfBirth);

    public async Task<(bool ok, string? error)> UpdateFamilyMemberAsync(FamilyMemberEdit e, CancellationToken ct = default)
    {
        var fm = await _db.FamilyMembers.FirstOrDefaultAsync(f => f.FamilyMemberId == e.FamilyMemberId, ct);
        if (fm is null) return (false, "Family member not found.");

        fm.FirstName = e.FirstName;
        fm.LastName = e.LastName;
        fm.DateOfBirth = e.DateOfBirth;

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

        _db.FamilyMembers.Add(new FamilyMember
        {
            UserId = userId,
            FirstName = firstName,
            LastName = lastName,
            DateOfBirth = dateOfBirth
        });
        await _db.SaveChangesAsync(ct);
        return (true, null);
    }

    /// <summary>Удалить члена семьи: сперва убрать его из всех регистраций (участники), затем строку.</summary>
    public async Task DeleteFamilyMemberAsync(int familyMemberId, CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        await _db.RegistrationParticipants.Where(p => p.FamilyMemberId == familyMemberId).ExecuteDeleteAsync(ct);
        await _db.FamilyMembers.Where(f => f.FamilyMemberId == familyMemberId).ExecuteDeleteAsync(ct);

        await tx.CommitAsync(ct);
    }

    public async Task<int> UserIdOfFamilyMemberAsync(int familyMemberId, CancellationToken ct = default)
        => await _db.FamilyMembers.Where(f => f.FamilyMemberId == familyMemberId)
                   .Select(f => f.UserId).FirstOrDefaultAsync(ct);
}
