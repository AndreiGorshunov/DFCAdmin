using DfcEventRegistration.Web.Auth;
using DfcEventRegistration.Web.Data;
using DfcEventRegistration.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace DfcEventRegistration.Web.Pages.Registrants;

/// <summary>
/// Read-only просмотр регистранта для роли Partner (политика CanView, без CanManage).
/// Без форм/действий. Цель — партнёр понимает «где юзеры (регистрант/родитель),
/// где дети (участники-члены семьи)». Admin тоже может открыть, но правит через Edit.
/// Страница остаётся CanView: AuthorizePage CanManage в Program.cs таргетит только /Registrants/Edit.
/// </summary>
public class DetailsModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly AdminWriteService _write;
    public DetailsModel(AppDbContext db, AdminWriteService write)
    {
        _db = db;
        _write = write;
    }

    [BindProperty(SupportsGet = true)] public long Id { get; set; }   // RegistrationId

    [TempData] public string? Notice { get; set; }

    // Стьюард/админ могут чек-инить прямо отсюда (CanCheckIn = Admin + Steward); партнёр — нет.
    public bool CanCheckIn => User.IsInRole(Roles.Admin) || User.IsInRole(Roles.Steward);

    public string EventName { get; private set; } = "";
    public Guid EventId { get; private set; }
    public int UserRegistrationCount { get; private set; }

    public IReadOnlyList<SessionChoiceRow> SessionChoices { get; private set; } = Array.Empty<SessionChoiceRow>();

    public record SessionChoiceRow(long RegistrationSessionId, string SessionName, string StartPointName,
        TimeOnly? StartTime, TimeOnly? EndTime, bool CheckedIn, DateTime? CheckInTime);

    public PersonView Person { get; private set; } = new();
    public RegistrationView Reg { get; private set; } = new();
    public IReadOnlyList<ParticipantRow> Participants { get; private set; } = Array.Empty<ParticipantRow>();
    public IReadOnlyList<FamilyMember> Family { get; private set; } = Array.Empty<FamilyMember>();

    public int ChildCount => Participants.Count(p => !p.IsSelf);

    public record ParticipantRow(string? Name, bool IsSelf, string? TshirtSize);

    public class PersonView
    {
        public int UserId { get; set; }
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public string Email { get; set; } = "";
        public string? Phone { get; set; }
        public DateTime? DateOfBirth { get; set; }
    }

    public class RegistrationView
    {
        public string? GroupCode { get; set; }
        public RegistrationStatus Status { get; set; }
        public DateTime RegistrationDate { get; set; }
        public string? EmergencyContactFirstName { get; set; }
        public string? EmergencyContactLastName { get; set; }
        public string? EmergencyContactPhone { get; set; }
        public string? QRCode { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var reg = await _db.EventRegistrations.AsNoTracking()
            .Include(r => r.User)
            .Include(r => r.Event)
            .FirstOrDefaultAsync(r => r.RegistrationId == Id, ct);
        if (reg is null) return NotFound();

        EventName = reg.Event.Name;
        EventId = reg.EventId;
        UserRegistrationCount = await _db.EventRegistrations.CountAsync(r => r.UserId == reg.UserId, ct);

        Person = new PersonView
        {
            UserId = reg.UserId,
            FirstName = reg.User.FirstName,
            LastName = reg.User.LastName,
            Email = reg.User.Email,
            Phone = reg.User.Phone,
            DateOfBirth = reg.User.DateOfBirth,
        };

        Reg = new RegistrationView
        {
            GroupCode = reg.GroupCode,
            Status = reg.Status,
            RegistrationDate = reg.RegistrationDate,
            EmergencyContactFirstName = reg.EmergencyContactFirstName,
            EmergencyContactLastName = reg.EmergencyContactLastName,
            EmergencyContactPhone = reg.EmergencyContactPhone,
            QRCode = reg.QRCode,
        };

        // self (FamilyMemberId == null) сверху, затем дети.
        Participants = await _db.RegistrationParticipants.AsNoTracking()
            .Where(p => p.RegistrationId == Id)
            .OrderBy(p => p.FamilyMemberId == null ? 0 : 1)
            .ThenBy(p => p.ParticipantId)
            .Select(p => new ParticipantRow(
                p.FamilyMemberId == null ? null : (p.FamilyMember!.FirstName + " " + p.FamilyMember!.LastName),
                p.FamilyMemberId == null,
                p.TshirtSize))
            .ToListAsync(ct);

        Family = await _db.FamilyMembers.AsNoTracking()
            .Where(f => f.UserId == reg.UserId)
            .OrderBy(f => f.FamilyMemberId)
            .ToListAsync(ct);

        // Выбранные сессии и точки старта + статус чек-ина (read-only).
        SessionChoices = await _db.RegistrationSessions.AsNoTracking()
            .Where(rs => rs.RegistrationId == Id)
            .OrderBy(rs => rs.Session.Name).ThenBy(rs => rs.StartPoint.DisplayOrder)
            .Select(rs => new SessionChoiceRow(
                rs.RegistrationSessionId, rs.Session.Name, rs.StartPoint.Name, rs.StartPoint.StartTime, rs.StartPoint.EndTime,
                rs.CheckedIn, rs.CheckInTime))
            .ToListAsync(ct);

        return Page();
    }

    /// <summary>Чек-ин/снятие прямо из просмотра — для стьюарда/админа (CanCheckIn).
    /// Страница под CanView, поэтому хендлер явно проверяет роль: партнёр не пройдёт даже сырым POST.</summary>
    public async Task<IActionResult> OnPostSetSessionCheckedInAsync(long registrationSessionId, bool checkedIn, CancellationToken ct)
    {
        if (!CanCheckIn) return Forbid();
        await _write.SetSessionCheckedInAsync(registrationSessionId, checkedIn, ct);
        Notice = checkedIn ? "Checked in." : "Check-in undone.";
        return RedirectToPage(new { Id });
    }
}
