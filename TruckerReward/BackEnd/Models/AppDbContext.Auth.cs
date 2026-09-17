using Microsoft.EntityFrameworkCore;

namespace BackEnd.Models;

// sponsors, login_attempts, password_changes and password_resets, see db/schema.sql
public partial class AppDbContext
{
    public virtual DbSet<Sponsor> Sponsors { get; set; }

    public virtual DbSet<LoginAttempt> LoginAttempts { get; set; }

    public virtual DbSet<PasswordChange> PasswordChanges { get; set; }

    public virtual DbSet<PasswordReset> PasswordResets { get; set; }

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

        modelBuilder.Entity<PasswordChange>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");
            entity.ToTable("password_changes");
            entity.HasIndex(e => e.UserId);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.ChangeType)
                .HasColumnType("enum('changed','reset_requested','reset_completed')")
                .HasColumnName("change_type");
            entity.Property(e => e.IpAddress)
                .HasMaxLength(45)
                .HasColumnName("ip_address");
            entity.Property(e => e.ChangedAt)
                .HasColumnType("datetime")
                .HasColumnName("changed_at");
        });

        modelBuilder.Entity<PasswordReset>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");
            entity.ToTable("password_resets");
            entity.HasIndex(e => e.TokenHash).IsUnique();
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.TokenHash)
                .HasMaxLength(64)
                .IsFixedLength()
                .HasColumnName("token_hash");
            entity.Property(e => e.ExpiresAt)
                .HasColumnType("datetime")
                .HasColumnName("expires_at");
            entity.Property(e => e.UsedAt)
                .HasColumnType("datetime")
                .HasColumnName("used_at");
        });
    }
}
