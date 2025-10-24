using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShoutingIguana.Core.Models;

namespace ShoutingIguana.Data.Configurations;

public class FindingConfiguration : IEntityTypeConfiguration<Finding>
{
    public void Configure(EntityTypeBuilder<Finding> builder)
    {
        builder.ToTable("Findings");
        
        builder.HasKey(f => f.Id);
        
        builder.Property(f => f.TaskKey)
            .IsRequired()
            .HasMaxLength(100);
        
        builder.Property(f => f.Code)
            .IsRequired()
            .HasMaxLength(100);
        
        builder.Property(f => f.Message)
            .IsRequired()
            .HasMaxLength(2000);
        
        builder.Property(f => f.DataJson);
        
        builder.Property(f => f.Severity)
            .HasConversion<int>();
        
        builder.Property(f => f.CreatedUtc)
            .IsRequired();
        
        // Indexes
        builder.HasIndex(f => f.TaskKey);
        builder.HasIndex(f => f.Severity);
        builder.HasIndex(f => f.UrlId);
        
        // Relationships
        builder.HasOne(f => f.Project)
            .WithMany()
            .HasForeignKey(f => f.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
        
        builder.HasOne(f => f.Url)
            .WithMany(u => u.Findings)
            .HasForeignKey(f => f.UrlId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

