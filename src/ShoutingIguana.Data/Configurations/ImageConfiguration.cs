using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShoutingIguana.Core.Models;

namespace ShoutingIguana.Data.Configurations;

public class ImageConfiguration : IEntityTypeConfiguration<Image>
{
    public void Configure(EntityTypeBuilder<Image> builder)
    {
        builder.ToTable("Images");
        
        builder.HasKey(i => i.Id);
        
        builder.Property(i => i.SrcUrl)
            .IsRequired()
            .HasMaxLength(2048);
        
        builder.Property(i => i.AltText)
            .HasMaxLength(1000);
        
        // Indexes
        builder.HasIndex(i => i.UrlId);
        builder.HasIndex(i => i.SrcUrl);
        
        // Relationships
        builder.HasOne(i => i.Url)
            .WithMany(u => u.Images)
            .HasForeignKey(i => i.UrlId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

