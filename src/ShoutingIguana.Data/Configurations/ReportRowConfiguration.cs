using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShoutingIguana.Core.Models;

namespace ShoutingIguana.Data.Configurations;

public class ReportRowConfiguration : IEntityTypeConfiguration<ReportRow>
{
    public void Configure(EntityTypeBuilder<ReportRow> builder)
    {
        builder.ToTable("ReportRows");
        
        builder.HasKey(rr => rr.Id);
        
        builder.Property(rr => rr.ProjectId)
            .IsRequired();
        
        builder.Property(rr => rr.TaskKey)
            .IsRequired()
            .HasMaxLength(100);
        
        builder.Property(rr => rr.UrlId)
            .IsRequired(false); // Nullable for aggregate reports
        
        builder.Property(rr => rr.RowDataJson)
            .IsRequired();
        
        builder.Property(rr => rr.CreatedUtc)
            .IsRequired();

        builder.Property(rr => rr.Severity)
            .HasConversion<int?>()
            .IsRequired(false);

        builder.Property(rr => rr.IssueText)
            .HasMaxLength(512)
            .IsRequired(false);
        
        // Relationships
        builder.HasOne(rr => rr.Project)
            .WithMany()
            .HasForeignKey(rr => rr.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
        
        builder.HasOne(rr => rr.ReportSchema)
            .WithMany(rs => rs.ReportRows)
            .HasForeignKey(rr => rr.TaskKey)
            .HasPrincipalKey(rs => rs.TaskKey)
            .OnDelete(DeleteBehavior.Restrict);
        
        builder.HasOne(rr => rr.Url)
            .WithMany()
            .HasForeignKey(rr => rr.UrlId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired(false);
        
        // Indexes for efficient querying
        builder.HasIndex(rr => rr.ProjectId);
        builder.HasIndex(rr => rr.TaskKey);
        builder.HasIndex(rr => rr.UrlId);
        builder.HasIndex(rr => rr.CreatedUtc);
        
        // Composite index for common query pattern
        builder.HasIndex(rr => new { rr.ProjectId, rr.TaskKey });
        builder.HasIndex(rr => new { rr.ProjectId, rr.TaskKey, rr.Severity, rr.Id });
        builder.HasIndex(rr => new { rr.ProjectId, rr.TaskKey, rr.IssueText });
    }
}
