using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShoutingIguana.Core.Models;

namespace ShoutingIguana.Data.Configurations;

public class HeaderConfiguration : IEntityTypeConfiguration<Header>
{
    public void Configure(EntityTypeBuilder<Header> builder)
    {
        builder.ToTable("Headers");
        builder.HasKey(e => e.Id);
        
        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(255);
            
        builder.Property(e => e.Value)
            .IsRequired();

        builder.HasIndex(e => e.UrlId);

        builder.HasOne(e => e.Url)
            .WithMany(u => u.Headers)
            .HasForeignKey(e => e.UrlId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

