using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShoutingIguana.Core.Models;

namespace ShoutingIguana.Data.Configurations;

public class HreflangConfiguration : IEntityTypeConfiguration<Hreflang>
{
    public void Configure(EntityTypeBuilder<Hreflang> builder)
    {
        builder.ToTable("Hreflangs");
        builder.HasKey(e => e.Id);
        
        builder.Property(e => e.LanguageCode)
            .IsRequired()
            .HasMaxLength(20);
        
        builder.Property(e => e.TargetUrl)
            .IsRequired()
            .HasMaxLength(2048);
        
        builder.Property(e => e.Source)
            .IsRequired()
            .HasMaxLength(20);
        
        builder.HasIndex(e => e.UrlId);
        builder.HasIndex(e => e.LanguageCode);
        builder.HasIndex(e => e.IsXDefault);
        builder.HasIndex(e => new { e.UrlId, e.LanguageCode });
        
        builder.HasOne(e => e.Url)
            .WithMany(u => u.Hreflangs)
            .HasForeignKey(e => e.UrlId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

