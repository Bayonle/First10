using First10.Domain.Conversations;
using First10.Domain.Incidents;
using Microsoft.EntityFrameworkCore;

namespace First10.Infrastructure.Persistence;

public class First10DbContext(DbContextOptions<First10DbContext> options) : DbContext(options)
{
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<IncidentTicket> Tickets => Set<IncidentTicket>();
    public DbSet<TimelineEntry> TimelineEntries => Set<TimelineEntry>();

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
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.CreatedAt);
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
