using System.ComponentModel.DataAnnotations;
using DfcEventRegistration.Web.Data;
using DfcEventRegistration.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace DfcEventRegistration.Web.Pages.Registrants;

public class EditModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly AdminWriteService _write;

    public EditModel(AppDbContext db, AdminWriteService write)
    {
        _db = db;
        _write = write;
    }

    [BindProperty(SupportsGet = true)] public long Id { get; set; }   // RegistrationId
    [BindProperty] public InputModel Input { get; set; } = new();

    public string EventName { get; private set; } = "";
    public int UserRegistrationCount { get; private set; }

    // Участники ИМЕННО ЭТОЙ регистрации (RegistrationParticipants по RegistrationId).
    public IReadOnlyList<ParticipantRow> Participants { get; private set; } = Array.Empty<ParticipantRow>();
    // Ростер семьи персоны (общий для всех её регистраций).
    public IReadOnlyList<FamilyMember> Family { get; private set; } = Array.Empty<FamilyMember>();
    // Члены ростера, которых ещё нет среди участников этой регистрации.
    public IReadOnlyList<FamilyMember> AvailableToAdd { get; private set; } = Array.Empty<FamilyMember>();

    // Сессии/точки старта: выборы этой регистрации и список точек события для добавления.
    public Guid EventId { get; private set; }
    public IReadOnlyList<SessionChoiceRow> SessionChoices { get; private set; } = Array.Empty<SessionChoiceRow>();
    public IReadOnlyList<StartPointOption> StartPointOptions { get; private set; } = Array.Empty<StartPointOption>();

    public record SessionChoiceRow(long RegistrationSessionId, string SessionName, string StartPointName,
        TimeOnly? StartTime, TimeOnly? EndTime, bool CheckedIn, DateTime? CheckInTime);
    public record StartPointOption(int StartPointId, string Label);

    public string[] Sizes => AdminWriteService.TshirtSizes;

    public int MaxFamily => AdminWriteService.MaxChildrenPerUser;
    public bool FamilyLimitReached => Family.Count >= MaxFamily;

    public string? Error { get; set; }
    [TempData] public string? Notice { get; set; }

    public record ParticipantRow(long ParticipantId, int? FamilyMemberId, string? Name, bool IsSelf, string? TshirtSize);

    public class InputModel
    {
        public int UserId { get; set; }
        public long RegistrationId { get; set; }

        [Required, StringLength(100)] public string FirstName { get; set; } = "";
        [Required, StringLength(100)] public string LastName { get; set; } = "";
        [Required, EmailAddress, StringLength(256)] public string Email { get; set; } = "";
        [StringLength(32)] public string? Phone { get; set; }
        public DateTime? DateOfBirth { get; set; }

        [StringLength(64)] public string? GroupCode { get; set; }
        public RegistrationStatus Status { get; set; }
        [StringLength(100)] public string? EmergencyContactFirstName { get; set; }
        [StringLength(100)] public string? EmergencyContactLastName { get; set; }
        [StringLength(32)] public string? EmergencyContactPhone { get; set; }

        // inline "add family member to roster"
        [StringLength(100)] public string? NewFamilyFirstName { get; set; }
        [StringLength(100)] public string? NewFamilyLastName { get; set; }
        public DateTime? NewFamilyDateOfBirth { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
        => await LoadAsync(ct, fillInput: true) ? Page() : NotFound();

    private async Task<bool> LoadAsync(CancellationToken ct, bool fillInput)
    {
        var reg = await _db.EventRegistrations.AsNoTracking()
            .Include(r => r.User)
            .Include(r => r.Event)
            .FirstOrDefaultAsync(r => r.RegistrationId == Id, ct);

        if (reg is null) return false;

        EventName = reg.Event.Name;
        UserRegistrationCount = await _db.EventRegistrations.CountAsync(r => r.UserId == reg.UserId, ct);

        // Участники этой регистрации: self (FamilyMemberId == null) сверху, потом дети.
        Participants = await _db.RegistrationParticipants.AsNoTracking()
            .Where(p => p.RegistrationId == Id)
            .OrderBy(p => p.FamilyMemberId == null ? 0 : 1)
            .ThenBy(p => p.ParticipantId)
            .Select(p => new ParticipantRow(
                p.ParticipantId,
                p.FamilyMemberId,
                p.FamilyMemberId == null ? null : (p.FamilyMember!.FirstName + " " + p.FamilyMember!.LastName),
                p.FamilyMemberId == null,
                p.TshirtSize))
            .ToListAsync(ct);

        // Ростер семьи персоны.
        Family = await _db.FamilyMembers.AsNoTracking()
            .Where(f => f.UserId == reg.UserId)
            .OrderBy(f => f.FamilyMemberId)
            .ToListAsync(ct);

        var participatingFmIds = Participants
            .Where(p => p.FamilyMemberId != null)
            .Select(p => p.FamilyMemberId!.Value)
            .ToHashSet();
        AvailableToAdd = Family.Where(f => !participatingFmIds.Contains(f.FamilyMemberId)).ToList();

        EventId = reg.EventId;

        // Выбранные сессии/точки этой регистрации (одну сессию можно выбирать несколько раз).
        SessionChoices = await _db.RegistrationSessions.AsNoTracking()
            .Where(rs => rs.RegistrationId == Id)
            .OrderBy(rs => rs.Session.Name).ThenBy(rs => rs.StartPoint.DisplayOrder)
            .Select(rs => new SessionChoiceRow(
                rs.RegistrationSessionId, rs.Session.Name, rs.StartPoint.Name,
                rs.StartPoint.StartTime, rs.StartPoint.EndTime, rs.CheckedIn, rs.CheckInTime))
            .ToListAsync(ct);

        // Все точки старта события (для выпадающего списка «добавить»; точка однозначно задаёт сессию).
        StartPointOptions = await _db.EventStartPoints.AsNoTracking()
            .Where(sp => sp.Session.EventId == reg.EventId)
            .OrderBy(sp => sp.Session.Name).ThenBy(sp => sp.DisplayOrder)
            .Select(sp => new StartPointOption(sp.StartPointId, sp.Session.Name + " — " + sp.Name))
            .ToListAsync(ct);

        if (fillInput)
        {
            Input = new InputModel
            {
                UserId = reg.UserId,
                RegistrationId = reg.RegistrationId,
                FirstName = reg.User.FirstName,
                LastName = reg.User.LastName,
                Email = reg.User.Email,
                Phone = reg.User.Phone,
                DateOfBirth = reg.User.DateOfBirth,
                GroupCode = reg.GroupCode,
                Status = reg.Status,
                EmergencyContactFirstName = reg.EmergencyContactFirstName,
                EmergencyContactLastName = reg.EmergencyContactLastName,
                EmergencyContactPhone = reg.EmergencyContactPhone,
            };
        }
        return true;
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid) { await LoadAsync(ct, fillInput: false); return Page(); }

        var (ok, err) = await _write.UpdateRegistrantAsync(new(
            Input.UserId, Input.FirstName.Trim(), Input.LastName.Trim(), Input.Email.Trim(),
            Input.Phone, Input.DateOfBirth, Input.RegistrationId, Input.GroupCode, Input.Status,
            Input.EmergencyContactFirstName, Input.EmergencyContactLastName, Input.EmergencyContactPhone), ct);

        if (!ok) { Error = err; await LoadAsync(ct, fillInput: false); return Page(); }

        Notice = "Saved.";
        return RedirectToPage(new { Id });
    }

    // ----- Участники этой регистрации -----

    public async Task<IActionResult> OnPostAddParticipantAsync(int familyMemberId, string? tshirtSize, CancellationToken ct)
    {
        var (ok, err) = await _write.AddParticipantAsync(Id, familyMemberId, tshirtSize, ct);
        if (!ok) { ModelState.Clear(); Error = err; await LoadAsync(ct, fillInput: true); return Page(); }
        Notice = "Added to this registration.";
        return RedirectToPage(new { Id });
    }

    public async Task<IActionResult> OnPostRemoveParticipantAsync(long participantId, CancellationToken ct)
    {
        await _write.RemoveParticipantAsync(participantId, ct);
        Notice = "Removed from this registration.";
        return RedirectToPage(new { Id });
    }

    public async Task<IActionResult> OnPostSetTshirtAsync(long participantId, string? tshirtSize, CancellationToken ct)
    {
        await _write.SetParticipantTshirtAsync(participantId, tshirtSize, ct);
        Notice = "T-shirt size updated.";
        return RedirectToPage(new { Id });
    }

    // ----- Сессии и чек-ин этой регистрации -----

    public async Task<IActionResult> OnPostAddSessionAsync(int startPointId, CancellationToken ct)
    {
        // Точка старта однозначно задаёт сессию — выводим sessionId из неё.
        var sessionId = await _write.SessionIdOfStartPointAsync(startPointId, ct);
        if (sessionId == 0)
        {
            ModelState.Clear(); Error = "Start point not found.";
            await LoadAsync(ct, fillInput: true); return Page();
        }
        var (ok, err) = await _write.AssignSessionAsync(Id, sessionId, startPointId, ct);
        if (!ok)
        {
            ModelState.Clear(); Error = err;
            await LoadAsync(ct, fillInput: true); return Page();
        }
        Notice = "Session selection added.";
        return RedirectToPage(new { Id });
    }

    public async Task<IActionResult> OnPostRemoveSessionAsync(long registrationSessionId, CancellationToken ct)
    {
        await _write.RemoveRegistrationSessionAsync(registrationSessionId, ct);
        Notice = "Session selection removed.";
        return RedirectToPage(new { Id });
    }

    public async Task<IActionResult> OnPostSetSessionCheckedInAsync(long registrationSessionId, bool checkedIn, CancellationToken ct)
    {
        await _write.SetSessionCheckedInAsync(registrationSessionId, checkedIn, ct);
        Notice = checkedIn ? "Checked in." : "Check-in undone.";
        return RedirectToPage(new { Id });
    }

    // ----- Ростер семьи (персона) -----

    public async Task<IActionResult> OnPostAddFamilyAsync(CancellationToken ct)
    {
        // Эта форма НЕ редактирует персону. Чистим ModelState, иначе [Required] полей персоны
        // (их нет в этой форме) дадут ложные ошибки и заставят asp-for показать пустые поля.
        ModelState.Clear();

        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Input.NewFamilyFirstName)) errors.Add("First name is required.");
        if (string.IsNullOrWhiteSpace(Input.NewFamilyLastName))  errors.Add("Last name is required.");
        if (Input.NewFamilyDateOfBirth is null)                  errors.Add("Date of birth is required.");

        if (errors.Count == 0)
        {
            var (ok, err) = await _write.AddFamilyMemberAsync(Input.UserId,
                Input.NewFamilyFirstName!.Trim(), Input.NewFamilyLastName!.Trim(), Input.NewFamilyDateOfBirth, ct);
            if (ok) { Notice = "Family member added to the roster."; return RedirectToPage(new { Id }); }
            errors.Add(err!);
        }

        Error = string.Join(" ", errors);
        var keep = (Input.NewFamilyFirstName, Input.NewFamilyLastName, Input.NewFamilyDateOfBirth);
        await LoadAsync(ct, fillInput: true);                 // вернуть поля персоны/регистрации из БД
        (Input.NewFamilyFirstName, Input.NewFamilyLastName, Input.NewFamilyDateOfBirth) = keep;  // сохранить ввод
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteFamilyAsync(int familyMemberId, CancellationToken ct)
    {
        await _write.DeleteFamilyMemberAsync(familyMemberId, ct);
        Notice = "Family member removed from the roster and all registrations.";
        return RedirectToPage(new { Id });
    }

    // ----- Удаление регистрации / персоны -----

    public async Task<IActionResult> OnPostDeleteRegistrationAsync(CancellationToken ct)
    {
        await _write.DeleteRegistrationAsync(Id, ct);
        Notice = "Registration deleted.";
        return RedirectToPage("/Registrants/Index");
    }

    public async Task<IActionResult> OnPostDeleteUserAsync(int userId, CancellationToken ct)
    {
        await _write.DeleteUserCascadeAsync(userId, ct);
        Notice = "Registrant and all related data deleted.";
        return RedirectToPage("/Registrants/Index");
    }
}
