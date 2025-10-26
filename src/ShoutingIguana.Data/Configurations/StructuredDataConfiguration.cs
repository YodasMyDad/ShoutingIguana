using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShoutingIguana.Core.Models;

namespace ShoutingIguana.Data.Configurations;

public class StructuredDataConfiguration : IEntityTypeConfiguration<StructuredData>
{
    public void Configure(EntityTypeBuilder<StructuredData> builder)
    {
        builder.ToTable("StructuredData");
        builder.HasKey(e => e.Id);
        
        builder.Property(e => e.Type)
            .IsRequired()
            .HasMaxLength(50);
        
        builder.Property(e => e.SchemaType)
            .IsRequired()
            .HasMaxLength(100);
        
        builder.Property(e => e.RawData)
            .IsRequired();
        
        builder.Property(e => e.ValidationErrors)
            .HasMaxLength(4000);
        
        builder.HasIndex(e => e.UrlId);
        builder.HasIndex(e => e.Type);
        builder.HasIndex(e => e.SchemaType);
        builder.HasIndex(e => e.IsValid);
        builder.HasIndex(e => new { e.UrlId, e.Type });
        
        builder.HasOne(e => e.Url)
            .WithMany(u => u.StructuredData)
            .HasForeignKey(e => e.UrlId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

