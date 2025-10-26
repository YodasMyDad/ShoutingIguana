using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShoutingIguana.Core.Models;

namespace ShoutingIguana.Data.Configurations;

public class CustomExtractionRuleConfiguration : IEntityTypeConfiguration<CustomExtractionRule>
{
    public void Configure(EntityTypeBuilder<CustomExtractionRule> builder)
    {
        builder.ToTable("CustomExtractionRules");
        
        builder.HasKey(r => r.Id);
        
        builder.Property(r => r.Name)
            .IsRequired()
            .HasMaxLength(200);
        
        builder.Property(r => r.FieldName)
            .IsRequired()
            .HasMaxLength(200);
        
        builder.Property(r => r.Selector)
            .IsRequired()
            .HasMaxLength(1000);
        
        builder.Property(r => r.SelectorType)
            .IsRequired();
        
        builder.Property(r => r.IsEnabled)
            .IsRequired()
            .HasDefaultValue(true);
        
        builder.Property(r => r.CreatedUtc)
            .IsRequired();
        
        // Foreign key
        builder.HasOne(r => r.Project)
            .WithMany()
            .HasForeignKey(r => r.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
        
        // Index for performance
        builder.HasIndex(r => r.ProjectId);
    }
}

