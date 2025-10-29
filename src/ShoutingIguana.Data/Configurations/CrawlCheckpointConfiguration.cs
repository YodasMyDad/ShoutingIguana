using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShoutingIguana.Core.Models;

namespace ShoutingIguana.Data.Configurations;

public class CrawlCheckpointConfiguration : IEntityTypeConfiguration<CrawlCheckpoint>
{
    public void Configure(EntityTypeBuilder<CrawlCheckpoint> builder)
    {
        builder.ToTable("CrawlCheckpoints");
        
        builder.HasKey(c => c.Id);
        
        builder.Property(c => c.ProjectId).IsRequired();
        builder.Property(c => c.CreatedAt).IsRequired();
        builder.Property(c => c.Phase).HasMaxLength(50).IsRequired();
        builder.Property(c => c.Status).HasMaxLength(50).IsRequired();
        builder.Property(c => c.LastCrawledUrl).HasMaxLength(2048);
        
        builder.HasOne(c => c.Project)
            .WithMany()
            .HasForeignKey(c => c.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
            
        builder.HasIndex(c => new { c.ProjectId, c.IsActive });
    }
}

