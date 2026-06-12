using Microsoft.EntityFrameworkCore;

namespace DfcEventRegistration.Web.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<FamilyMember> FamilyMembers => Set<FamilyMember>();
    public DbSet<EventRegistration> EventRegistrations => Set<EventRegistration>();
    public DbSet<RegistrationParticipant> RegistrationParticipants => Set<RegistrationParticipant>();
    public DbSet<EventSession> EventSessions => Set<EventSession>();
    public DbSet<EventStartPoint> EventStartPoints => Set<EventStartPoint>();
    public DbSet<RegistrationSession> RegistrationSessions => Set<RegistrationSession>();
    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();
    public DbSet<AuditEntry> AuditLog => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.ToTable("Users", "dbo");
            e.HasKey(x => x.UserId);
            // PERSISTED-вычисляемая колонка (создаётся в 01/04). EF трактует как store-generated:
            // не пишет при INSERT/UPDATE, только читает. SQL дублирует определение в схеме.
            e.Property(x => x.PhoneDigitsRev)
             .HasComputedColumnSql(
                 "REVERSE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE([Phone],'+',''),' ',''),'-',''),'(',''),')',''))",
                 stored: true);
        });

        b.Entity<Event>(e =>
        {
            e.ToTable("Events", "dbo");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();   // GUID приходит извне
        });

        b.Entity<FamilyMember>(e =>
        {
            e.ToTable("FamilyMembers", "dbo");
            e.HasKey(x => x.FamilyMemberId);
        });

        b.Entity<EventRegistration>(e =>
        {
            e.ToTable("EventRegistrations", "dbo");
            e.HasKey(x => x.RegistrationId);
            e.Property(x => x.Status); // enum:byte -> tinyint, маппится автоматически
            e.Property(x => x.RegistrantLastName).HasMaxLength(100); // денормализованная фамилия (Вариант B, синхрон из Users)

            e.HasOne(x => x.User)
             .WithMany()
             .HasForeignKey(x => x.UserId)
             .OnDelete(DeleteBehavior.NoAction);

            e.HasOne(x => x.Event)
             .WithMany()
             .HasForeignKey(x => x.EventId)
             .OnDelete(DeleteBehavior.NoAction);

            e.HasMany(x => x.Participants)
             .WithOne(p => p.Registration)
             .HasForeignKey(p => p.RegistrationId)
             .OnDelete(DeleteBehavior.NoAction);
        });

        b.Entity<RegistrationParticipant>(e =>
        {
            e.ToTable("RegistrationParticipants", "dbo");
            e.HasKey(x => x.ParticipantId);

            e.HasOne(x => x.FamilyMember)
             .WithMany()
             .HasForeignKey(x => x.FamilyMemberId)
             .OnDelete(DeleteBehavior.NoAction);
        });

        b.Entity<EventSession>(e =>
        {
            e.ToTable("EventSessions", "dbo");
            e.HasKey(x => x.SessionId);
            e.Property(x => x.Name).HasMaxLength(256).IsRequired();

            e.HasOne(x => x.Event)
             .WithMany()
             .HasForeignKey(x => x.EventId)
             .OnDelete(DeleteBehavior.NoAction);

            e.HasMany(x => x.StartPoints)
             .WithOne(p => p.Session)
             .HasForeignKey(p => p.SessionId)
             .OnDelete(DeleteBehavior.NoAction);
        });

        b.Entity<EventStartPoint>(e =>
        {
            e.ToTable("EventStartPoints", "dbo");
            e.HasKey(x => x.StartPointId);
            e.Property(x => x.Name).HasMaxLength(256).IsRequired();
            // TimeOnly? -> SQL TIME(0); EF Core 8+ маппит автоматически.
        });

        b.Entity<RegistrationSession>(e =>
        {
            e.ToTable("RegistrationSessions", "dbo");
            e.HasKey(x => x.RegistrationSessionId);

            e.HasOne(x => x.Registration)
             .WithMany()
             .HasForeignKey(x => x.RegistrationId)
             .OnDelete(DeleteBehavior.NoAction);

            e.HasOne(x => x.Session)
             .WithMany()
             .HasForeignKey(x => x.SessionId)
             .OnDelete(DeleteBehavior.NoAction);

            e.HasOne(x => x.StartPoint)
             .WithMany()
             .HasForeignKey(x => x.StartPointId)
             .OnDelete(DeleteBehavior.NoAction);
        });

        b.Entity<AdminUser>(e =>
        {
            e.ToTable("AdminUsers", "dbo");
            e.HasKey(x => x.AdminUserId);
            e.Property(x => x.Email).HasMaxLength(256).IsRequired();
            e.Property(x => x.Role).HasMaxLength(32).IsRequired();
            e.Property(x => x.DisplayName).HasMaxLength(200);
            e.Property(x => x.GrantedBy).HasMaxLength(256);
            e.HasIndex(x => x.Email).IsUnique();
        });

        b.Entity<AuditEntry>(e =>
        {
            e.ToTable("AuditLog", "dbo");
            e.HasKey(x => x.AuditId);
            e.Property(x => x.ActorEmail).HasMaxLength(256);
            e.Property(x => x.Action).HasMaxLength(64).IsRequired();
            e.Property(x => x.EntityType).HasMaxLength(64);
            e.Property(x => x.EntityId).HasMaxLength(64);
            e.Property(x => x.Details).HasMaxLength(1024);
            e.HasIndex(x => x.WhenUtc);
        });
    }
}
