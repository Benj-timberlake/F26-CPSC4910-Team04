using Microsoft.EntityFrameworkCore;

namespace BackEnd.Models;

// sponsors and login_attempts tables, see db/schema.sql
public partial class AppDbContext
{
    public virtual DbSet<Sponsor> Sponsors { get; set; }

    public virtual DbSet<LoginAttempt> LoginAttempts { get; set; }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasIndex(e => e.Username).IsUnique();
            entity.HasIndex(e => e.Email).IsUnique();
        });

        modelBuilder.Entity<Sponsor>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("sponsors");

            entity.HasIndex(e => e.UserId).IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.CompanyName)
                .HasMaxLength(255)
                .HasColumnName("company_name");

            entity.HasOne(d => d.User)
                .WithOne()
                .HasForeignKey<Sponsor>(d => d.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LoginAttempt>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("login_attempts");

            entity.HasIndex(e => e.UserId);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Username)
                .HasMaxLength(255)
                .HasColumnName("username");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.Succeeded).HasColumnName("succeeded");
            entity.Property(e => e.IpAddress)
                .HasMaxLength(45)
                .HasColumnName("ip_address");
            entity.Property(e => e.AttemptedAt)
                .HasColumnType("datetime")
                .HasColumnName("attempted_at");
        });
    }
}
