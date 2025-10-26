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
        
        // Enhanced canonical fields
        builder.Property(e => e.CanonicalHtml)
            .HasMaxLength(2048);
        
        builder.Property(e => e.CanonicalHttp)
            .HasMaxLength(2048);
        
        builder.Property(e => e.CanonicalIssues)
            .HasMaxLength(4000);
        
        // Robots directive fields
        builder.Property(e => e.RobotsSource)
            .HasMaxLength(50);
        
        builder.Property(e => e.XRobotsTag)
            .HasMaxLength(500);
        
        // Language fields
        builder.Property(e => e.HtmlLang)
            .HasMaxLength(50);
        
        builder.Property(e => e.ContentLanguageHeader)
            .HasMaxLength(100);
        
        // Meta refresh fields
        builder.Property(e => e.MetaRefreshTarget)
            .HasMaxLength(2048);
        
        // JavaScript changes
        builder.Property(e => e.JsChangedElements)
            .HasMaxLength(4000);
        
        // HTTP headers
        builder.Property(e => e.CacheControl)
            .HasMaxLength(500);
        
        builder.Property(e => e.Vary)
            .HasMaxLength(500);
        
        builder.Property(e => e.ContentEncoding)
            .HasMaxLength(100);
        
        builder.Property(e => e.LinkHeader)
            .HasMaxLength(4000);
        
        builder.HasIndex(e => e.Address);
        builder.HasIndex(e => e.NormalizedUrl);
        builder.HasIndex(e => e.Host);
        builder.HasIndex(e => new { e.ProjectId, e.Status });
        builder.HasIndex(e => e.RobotsNoindex);
        builder.HasIndex(e => e.HasMultipleCanonicals);
        builder.HasIndex(e => e.HtmlLang);

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

