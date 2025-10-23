using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShoutingIguana.Core.Models;

namespace ShoutingIguana.Data.Configurations;

public class UrlConfiguration : IEntityTypeConfiguration<Url>
{
    public void Configure(EntityTypeBuilder<Url> builder)
    {
        builder.ToTable("Urls");
        builder.HasKey(e => e.Id);
        
        builder.Property(e => e.Address)
            .IsRequired()
            .HasMaxLength(2048);
            
        builder.Property(e => e.NormalizedUrl)
            .IsRequired()
            .HasMaxLength(2048);
            
        builder.Property(e => e.Scheme)
            .HasMaxLength(10);
            
        builder.Property(e => e.Host)
            .HasMaxLength(255);
            
        builder.Property(e => e.Path)
            .HasMaxLength(2048);
            
        builder.Property(e => e.ContentType)
            .HasMaxLength(255);
        
        builder.HasIndex(e => e.Address);
        builder.HasIndex(e => e.NormalizedUrl);
        builder.HasIndex(e => e.Host);
        builder.HasIndex(e => new { e.ProjectId, e.Status });

        builder.HasOne(e => e.Project)
            .WithMany(p => p.Urls)
            .HasForeignKey(e => e.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.DiscoveredFromUrl)
            .WithMany()
            .HasForeignKey(e => e.DiscoveredFromUrlId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

