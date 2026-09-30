using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace BackEnd.Models;

public partial class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<AccountsHistory> AccountsHistories { get; set; }

    public virtual DbSet<AboutPage> AboutPages { get; set; }

    public virtual DbSet<Application> Applications { get; set; }

    public virtual DbSet<AuditHistory> AuditHistories { get; set; }

    public virtual DbSet<CartItem> CartItems { get; set; }

    public virtual DbSet<Company> Companies { get; set; }

    public virtual DbSet<LoginAttempt> LoginAttempts { get; set; }

    public virtual DbSet<NotificationType> NotificationTypes { get; set; }

    public virtual DbSet<NotificationsHistory> NotificationsHistories { get; set; }

    public virtual DbSet<PasswordChange> PasswordChanges { get; set; }

    public virtual DbSet<PasswordReset> PasswordResets { get; set; }

    public virtual DbSet<PointsHistory> PointsHistories { get; set; }

    public virtual DbSet<Sponsor> Sponsors { get; set; }

    public virtual DbSet<User> Users { get; set; }
    public DbSet<PointsHistory> PointsHistory { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AboutPage>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");
            entity.ToTable("about_page");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Type).HasMaxLength(32).HasColumnName("type");
            entity.Property(e => e.Title).HasMaxLength(255).HasColumnName("title");
            entity.Property(e => e.Body).HasColumnType("text").HasColumnName("body");
            entity.Property(e => e.CreatedAt).HasColumnType("datetime").HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnType("datetime").HasColumnName("updated_at");
        });

        modelBuilder.Entity<AccountsHistory>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("accounts_history");

            entity.HasIndex(e => e.UserId, "fk_accounts_history_user");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Address)
                .HasMaxLength(255)
                .HasColumnName("address");
            entity.Property(e => e.CompanyId).HasColumnName("company_id");
            entity.Property(e => e.Email)
                .HasMaxLength(255)
                .HasColumnName("email");
            entity.Property(e => e.FirstName)
                .HasMaxLength(100)
                .HasColumnName("first_name");
            entity.Property(e => e.LastName)
                .HasMaxLength(100)
                .HasColumnName("last_name");
            entity.Property(e => e.Password)
                .HasMaxLength(255)
                .HasColumnName("password");
            entity.Property(e => e.PhoneNumber)
                .HasMaxLength(20)
                .HasColumnName("phone_number");
            entity.Property(e => e.Points).HasColumnName("points");
            entity.Property(e => e.Timestamp)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("datetime")
                .HasColumnName("timestamp");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.UserType)
                .HasColumnType("enum('admin','sponsor','driver')")
                .HasColumnName("user_type");
            entity.Property(e => e.Username)
                .HasMaxLength(255)
                .HasColumnName("username");

            entity.HasOne(d => d.User).WithMany(p => p.AccountsHistories)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_accounts_history_user");
        });

        modelBuilder.Entity<Application>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("applications");

            entity.HasIndex(e => e.CompanyId, "fk_applications_company");

            entity.HasIndex(e => e.SponsorId, "fk_applications_sponsor");

            entity.HasIndex(e => e.DriverId, "uq_applications_driver_id").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ApplicationDate)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("datetime")
                .HasColumnName("application_date");
            entity.Property(e => e.CompanyId).HasColumnName("company_id");
            entity.Property(e => e.DriverId).HasColumnName("driver_id");
            entity.Property(e => e.ExtraInfo)
                .HasColumnType("text")
                .HasColumnName("extra_info");
            entity.Property(e => e.SponsorId).HasColumnName("sponsor_id");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'created'")
                .HasColumnType("enum('created','approved','rejected')")
                .HasColumnName("status");

            entity.HasOne(d => d.Company).WithMany(p => p.Applications)
                .HasForeignKey(d => d.CompanyId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_applications_company");

            entity.HasOne(d => d.Driver).WithOne(p => p.ApplicationDriver)
                .HasForeignKey<Application>(d => d.DriverId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_applications_driver");

            entity.HasOne(d => d.Sponsor).WithMany(p => p.ApplicationSponsors)
                .HasForeignKey(d => d.SponsorId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_applications_sponsor");
        });

        modelBuilder.Entity<AuditHistory>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("audit_history");

            entity.HasIndex(e => e.PointsHistoryId, "fk_audit_history_points_history");

            entity.HasIndex(e => e.UserId, "fk_audit_history_user");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.PointsHistoryId).HasColumnName("points_history_id");
            entity.Property(e => e.Resoning)
                .HasColumnType("text")
                .HasColumnName("resoning");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.PointsHistory).WithMany(p => p.AuditHistories)
                .HasForeignKey(d => d.PointsHistoryId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_audit_history_points_history");

            entity.HasOne(d => d.User).WithMany(p => p.AuditHistories)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_audit_history_user");
        });

        modelBuilder.Entity<CartItem>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("cart");

            entity.HasIndex(e => e.PointsHistoryId, "fk_cart_points_history");

            entity.HasIndex(e => e.UserId, "fk_cart_user");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Description)
                .HasColumnType("text")
                .HasColumnName("description");
            entity.Property(e => e.InCart).HasColumnName("in_cart");
            entity.Property(e => e.Name)
                .HasMaxLength(255)
                .HasColumnName("name");
            entity.Property(e => e.PointsHistoryId).HasColumnName("points_history_id");
            entity.Property(e => e.Price)
                .HasPrecision(10)
                .HasColumnName("price");
            entity.Property(e => e.Quantity).HasColumnName("quantity");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.WasOrdered).HasColumnName("was_ordered");

            entity.HasOne(d => d.PointsHistory).WithMany(p => p.Carts)
                .HasForeignKey(d => d.PointsHistoryId)
                .HasConstraintName("fk_cart_points_history");

            entity.HasOne(d => d.User).WithMany(p => p.Carts)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_cart_user");
        });

        modelBuilder.Entity<Company>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("companies");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Description)
                .HasColumnType("text")
                .HasColumnName("description");
            entity.Property(e => e.Name)
                .HasMaxLength(255)
                .HasColumnName("name");
        });

        modelBuilder.Entity<LoginAttempt>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("login_attempts");

            entity.HasIndex(e => e.UserId, "ix_login_attempts_user");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AttemptedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("datetime")
                .HasColumnName("attempted_at");
            entity.Property(e => e.IpAddress)
                .HasMaxLength(45)
                .HasColumnName("ip_address");
            entity.Property(e => e.Succeeded).HasColumnName("succeeded");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.Username)
                .HasMaxLength(255)
                .HasColumnName("username");

            entity.HasOne(d => d.User).WithMany(p => p.LoginAttempts)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_login_attempts_user");
        });

        modelBuilder.Entity<NotificationType>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("notification_types");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Body)
                .HasColumnType("text")
                .HasColumnName("body");
            entity.Property(e => e.Subject)
                .HasMaxLength(100)
                .HasColumnName("subject");
        });

        modelBuilder.Entity<NotificationsHistory>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("notifications_history");

            entity.HasIndex(e => e.NotificationTypeId, "fk_notifications_history_type");

            entity.HasIndex(e => e.UserId, "fk_notifications_history_user");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.BeenRead).HasColumnName("been_read");
            entity.Property(e => e.Body)
                .HasColumnType("text")
                .HasColumnName("body");
            entity.Property(e => e.NotificationTypeId).HasColumnName("notification_type_id");
            entity.Property(e => e.Subject)
                .HasMaxLength(100)
                .HasColumnName("subject");
            entity.Property(e => e.Timestamp)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("datetime")
                .HasColumnName("timestamp");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.NotificationType).WithMany(p => p.NotificationsHistories)
                .HasForeignKey(d => d.NotificationTypeId)
                .HasConstraintName("fk_notifications_history_type");

            entity.HasOne(d => d.User).WithMany(p => p.NotificationsHistories)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_notifications_history_user");
        });

        modelBuilder.Entity<PasswordChange>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("password_changes");

            entity.HasIndex(e => e.UserId, "ix_password_changes_user");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ChangeType)
                .HasColumnType("enum('changed','reset_requested','reset_completed')")
                .HasColumnName("change_type");
            entity.Property(e => e.ChangedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("datetime")
                .HasColumnName("changed_at");
            entity.Property(e => e.IpAddress)
                .HasMaxLength(45)
                .HasColumnName("ip_address");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.User).WithMany(p => p.PasswordChanges)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("fk_password_changes_user");
        });

        modelBuilder.Entity<PasswordReset>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("password_resets");

            entity.HasIndex(e => e.UserId, "fk_password_resets_user");

            entity.HasIndex(e => e.TokenHash, "uq_password_resets_token").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ExpiresAt)
                .HasColumnType("datetime")
                .HasColumnName("expires_at");
            entity.Property(e => e.TokenHash)
                .HasMaxLength(64)
                .IsFixedLength()
                .HasColumnName("token_hash");
            entity.Property(e => e.UsedAt)
                .HasColumnType("datetime")
                .HasColumnName("used_at");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.User).WithMany(p => p.PasswordResets)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("fk_password_resets_user");
        });

        modelBuilder.Entity<PointsHistory>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("points_history");

            entity.HasIndex(e => e.UserId, "user_id");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.PointsDelta).HasColumnName("points_delta");
            entity.Property(e => e.Timestamp)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp")
                .HasColumnName("timestamp");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.User).WithMany(p => p.PointsHistories)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("points_history_ibfk_1");
        });

        modelBuilder.Entity<Sponsor>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("sponsors");

            entity.HasIndex(e => e.UserId, "uq_sponsors_user").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CompanyName)
                .HasMaxLength(255)
                .HasColumnName("company_name");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.User).WithOne(p => p.Sponsor)
                .HasForeignKey<Sponsor>(d => d.UserId)
                .HasConstraintName("fk_sponsors_user");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("users");

            entity.HasIndex(e => e.CompanyId, "fk_users_company");

            entity.HasIndex(e => e.Email, "uq_users_email").IsUnique();

            entity.HasIndex(e => e.Username, "uq_users_username").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Address)
                .HasMaxLength(255)
                .HasColumnName("address");
            entity.Property(e => e.CompanyId).HasColumnName("company_id");
            entity.Property(e => e.Email).HasColumnName("email");
            entity.Property(e => e.FirstName)
                .HasMaxLength(100)
                .HasColumnName("first_name");
            entity.Property(e => e.LastName)
                .HasMaxLength(100)
                .HasColumnName("last_name");
            entity.Property(e => e.Password)
                .HasMaxLength(255)
                .HasColumnName("password");
            entity.Property(e => e.PhoneNumber)
                .HasMaxLength(20)
                .HasColumnName("phone_number");
            entity.Property(e => e.Points).HasColumnName("points");
            entity.Property(e => e.UserType)
                .HasColumnType("enum('admin','sponsor','driver')")
                .HasColumnName("user_type");
            entity.Property(e => e.Username).HasColumnName("username");

            entity.HasOne(d => d.Company).WithMany(p => p.Users)
                .HasForeignKey(d => d.CompanyId)
                .HasConstraintName("fk_users_company");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
