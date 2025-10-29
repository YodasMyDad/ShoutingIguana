using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShoutingIguana.Core.Models;

namespace ShoutingIguana.Data.Configurations;

public class LinkConfiguration : IEntityTypeConfiguration<Link>
{
    public void Configure(EntityTypeBuilder<Link> builder)
    {
        builder.ToTable("Links");
        builder.HasKey(e => e.Id);
        
        builder.Property(e => e.AnchorText)
            .HasMaxLength(500);
        
        builder.Property(e => e.RelAttribute)
            .HasMaxLength(255);
        
        // Diagnostic fields
        builder.Property(e => e.DomPath)
            .HasMaxLength(1000);
        
        builder.Property(e => e.ElementTag)
            .HasMaxLength(50);
        
        builder.Property(e => e.HtmlSnippet)
            .HasMaxLength(1000);
        
        builder.Property(e => e.ParentTag)
            .HasMaxLength(100);

        builder.HasIndex(e => e.FromUrlId);
        builder.HasIndex(e => e.ToUrlId);
        builder.HasIndex(e => new { e.ProjectId, e.LinkType });
        builder.HasIndex(e => e.IsNofollow);

        builder.HasOne(e => e.Project)
            .WithMany(p => p.Links)
            .HasForeignKey(e => e.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.FromUrl)
            .WithMany(u => u.LinksFrom)
            .HasForeignKey(e => e.FromUrlId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.ToUrl)
            .WithMany(u => u.LinksTo)
            .HasForeignKey(e => e.ToUrlId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

