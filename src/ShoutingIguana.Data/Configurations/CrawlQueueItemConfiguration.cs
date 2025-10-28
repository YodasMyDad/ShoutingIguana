using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShoutingIguana.Core.Models;

namespace ShoutingIguana.Data.Configurations;

public class CrawlQueueItemConfiguration : IEntityTypeConfiguration<CrawlQueueItem>
{
    public void Configure(EntityTypeBuilder<CrawlQueueItem> builder)
    {
        builder.ToTable("CrawlQueue");
        builder.HasKey(e => e.Id);
        
        builder.Property(e => e.Address)
            .IsRequired()
            .HasMaxLength(2048);
            
        builder.Property(e => e.HostKey)
            .IsRequired()
            .HasMaxLength(255);

        builder.HasIndex(e => e.HostKey);
        builder.HasIndex(e => new { e.ProjectId, e.State, e.Priority });
        
        // Unique constraint to prevent duplicate URLs in queue (prevents race conditions)
        builder.HasIndex(e => new { e.ProjectId, e.Address })
            .IsUnique();

        builder.HasOne(e => e.Project)
            .WithMany(p => p.CrawlQueue)
            .HasForeignKey(e => e.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

