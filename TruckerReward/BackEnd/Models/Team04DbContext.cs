using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace BackEnd.Models;

public partial class Team04DbContext : DbContext
{
    public Team04DbContext()
    {
    }

    public Team04DbContext(DbContextOptions<Team04DbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Application> Applications { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Application>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("applications");

            entity.HasIndex(e => e.CompanyId, "fk_applications_company");

            entity.HasIndex(e => e.ReviewerId, "fk_applications_sponsor");

            entity.HasIndex(e => e.ApplicantId, "idx_applications_applicant_id");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ApplicantExtraInfo)
                .HasColumnType("text")
                .HasColumnName("applicant_extra_info");
            entity.Property(e => e.ApplicantId).HasColumnName("applicant_id");
            entity.Property(e => e.ApplicationTimestamp)
                .HasColumnType("datetime")
                .HasColumnName("application_timestamp");
            entity.Property(e => e.CompanyId).HasColumnName("company_id");
            entity.Property(e => e.ResponseTimestamp)
                .HasColumnType("datetime")
                .HasColumnName("response_timestamp");
            entity.Property(e => e.ReviewerId).HasColumnName("reviewer_id");
            entity.Property(e => e.ReviewerReasoning)
                .HasColumnType("text")
                .HasColumnName("reviewer_reasoning");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'active'")
                .HasColumnType("enum('active','approved','rejected','canceled')")
                .HasColumnName("status");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
