using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShoutingIguana.Core.Models;

namespace ShoutingIguana.Data.Configurations;

public class RedirectConfiguration : IEntityTypeConfiguration<Redirect>
{
    public void Configure(EntityTypeBuilder<Redirect> builder)
    {
        builder.ToTable("Redirects");
        
        builder.HasKey(r => r.Id);
        
        builder.Property(r => r.FromUrl)
            .IsRequired()
            .HasMaxLength(2048);
        
        builder.Property(r => r.ToUrl)
            .IsRequired()
            .HasMaxLength(2048);
        
        builder.Property(r => r.StatusCode)
            .IsRequired();
        
        builder.Property(r => r.Position)
            .IsRequired();
        
        // Indexes
        builder.HasIndex(r => r.UrlId);
        builder.HasIndex(r => new { r.UrlId, r.Position });
        
        // Relationships
        builder.HasOne(r => r.Url)
            .WithMany(u => u.Redirects)
            .HasForeignKey(r => r.UrlId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

