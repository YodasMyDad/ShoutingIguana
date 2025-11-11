using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShoutingIguana.Core.Models;

namespace ShoutingIguana.Data.Configurations;

public class ReportSchemaConfiguration : IEntityTypeConfiguration<ReportSchema>
{
    public void Configure(EntityTypeBuilder<ReportSchema> builder)
    {
        builder.ToTable("ReportSchemas");
        
        builder.HasKey(rs => rs.Id);
        
        builder.Property(rs => rs.TaskKey)
            .IsRequired()
            .HasMaxLength(100);
        
        builder.Property(rs => rs.SchemaVersion)
            .IsRequired();
        
        builder.Property(rs => rs.ColumnsJson)
            .IsRequired();
        
        builder.Property(rs => rs.IsUrlBased)
            .IsRequired();
        
        builder.Property(rs => rs.CreatedUtc)
            .IsRequired();
        
        // Create unique index on TaskKey
        builder.HasIndex(rs => rs.TaskKey)
            .IsUnique();
    }
}

