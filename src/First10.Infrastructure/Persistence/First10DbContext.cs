using First10.Domain.Conversations;
using First10.Domain.Incidents;
using Microsoft.EntityFrameworkCore;

namespace First10.Infrastructure.Persistence;

public class First10DbContext(DbContextOptions<First10DbContext> options) : DbContext(options)
{
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<IncidentTicket> Tickets => Set<IncidentTicket>();
    public DbSet<TimelineEntry> TimelineEntries => Set<TimelineEntry>();
    public DbSet<ReporterReputation> ReporterReputations => Set<ReporterReputation>();
    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();
    public DbSet<MicroInstructionTemplate> MicroInstructionTemplates => Set<MicroInstructionTemplate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Conversation>(e =>
        {
            e.ToTable("conversations");
            e.HasKey(x => x.Id);
            e.Property(x => x.ExternalUserId).HasMaxLength(256);
            // Identity rule (D-005): one conversation per (channel, external user).
            e.HasIndex(x => new { x.Channel, x.ExternalUserId }).IsUnique();
        });

        modelBuilder.Entity<IncidentTicket>(e =>
        {
            e.ToTable("incident_tickets");
            e.HasKey(x => x.Id);
            e.Property(x => x.Summary).HasMaxLength(2048);
            e.Property(x => x.Language).HasMaxLength(32);
            e.Property(x => x.Flags).HasMaxLength(1024);
            e.Property(x => x.ClassifierVersion).HasMaxLength(128);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => x.Disposition);
        });

        modelBuilder.Entity<ReporterReputation>(e =>
        {
            e.ToTable("reporter_reputations");
            e.HasKey(x => x.Id);
            e.Property(x => x.ExternalUserId).HasMaxLength(256);
            e.Property(x => x.Note).HasMaxLength(1024);
            e.HasIndex(x => new { x.Channel, x.ExternalUserId }).IsUnique();
        });

        modelBuilder.Entity<MediaAsset>(e =>
        {
            e.ToTable("media_assets");
            e.HasKey(x => x.Id);
            e.Property(x => x.MediaRef).HasMaxLength(512);
            e.HasIndex(x => x.CreatedAt);
        });

        modelBuilder.Entity<MicroInstructionTemplate>(e =>
        {
            e.ToTable("micro_instruction_templates");
            e.HasKey(x => x.Id);
            e.Property(x => x.Key).HasMaxLength(64);
            e.Property(x => x.Language).HasMaxLength(32);
            e.Property(x => x.Text).HasMaxLength(2048);
            e.Property(x => x.AudioMediaRef).HasMaxLength(512);
            e.Property(x => x.ApprovedBy).HasMaxLength(256);
            e.HasIndex(x => new { x.Key, x.Language, x.Version }).IsUnique();
        });

        modelBuilder.Entity<TimelineEntry>(e =>
        {
            e.ToTable("timeline_entries");
            e.HasKey(x => x.Id);
            e.Property(x => x.Text).HasMaxLength(8192);
            e.Property(x => x.ExternalMessageId).HasMaxLength(256);
            e.HasIndex(x => new { x.TicketId, x.OccurredAt });
            // Dedup rule (D-005): every channel redelivers; one unique index handles all of them.
            e.HasIndex(x => new { x.Channel, x.ExternalMessageId })
                .IsUnique()
                .HasFilter("\"ExternalMessageId\" IS NOT NULL");
        });
    }
}
