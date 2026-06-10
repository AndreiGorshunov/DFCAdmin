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

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.ToTable("Users", "dbo");
            e.HasKey(x => x.UserId);
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
    }
}
