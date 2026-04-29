using System;
using Domain.Backend.Tasks.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Domain.Backend.Sql;

public sealed class DomainBackendDbContext(DbContextOptions<DomainBackendDbContext> options) : DbContext(options)
{
    public DbSet<DomainSearchTask> DomainSearchTasks => Set<DomainSearchTask>();
    public DbSet<DomainSearchResult> DomainSearchResults => Set<DomainSearchResult>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DomainSearchTask>(entity =>
        {
            entity.ToTable("domain_search_tasks");
            entity.HasKey(task => task.Id);
            entity.Property(task => task.Id).HasColumnName("id");
            entity.Property(task => task.Name).HasColumnName("name").HasMaxLength(256);
            entity.Property(task => task.Mode).HasColumnName("mode").HasConversion<string>().HasMaxLength(32);
            entity.Property(task => task.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(32);
            entity.Property(task => task.RequestJson).HasColumnName("request_json");
            entity.Property(task => task.TotalCount).HasColumnName("total_count");
            entity.Property(task => task.CompletedCount).HasColumnName("completed_count");
            entity.Property(task => task.AvailableCount).HasColumnName("available_count");
            entity.Property(task => task.RegisteredCount).HasColumnName("registered_count");
            entity.Property(task => task.UnknownCount).HasColumnName("unknown_count");
            entity.Property(task => task.ErrorCount).HasColumnName("error_count");
            entity.Property(task => task.CancelRequested).HasColumnName("cancel_requested");
            entity.Property(task => task.DeletedAt).HasColumnName("deleted_at").HasConversion(
                value => value.HasValue ? value.Value.ToUnixTimeMilliseconds() : (long?)null,
                value => value.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(value.Value) : null);
            entity.Property(task => task.CreatedAt).HasColumnName("created_at").HasConversion(
                value => value.ToUnixTimeMilliseconds(),
                value => DateTimeOffset.FromUnixTimeMilliseconds(value));
            entity.Property(task => task.StartedAt).HasColumnName("started_at").HasConversion(
                value => value.HasValue ? value.Value.ToUnixTimeMilliseconds() : (long?)null,
                value => value.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(value.Value) : null);
            entity.Property(task => task.FinishedAt).HasColumnName("finished_at").HasConversion(
                value => value.HasValue ? value.Value.ToUnixTimeMilliseconds() : (long?)null,
                value => value.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(value.Value) : null);
            entity.Property(task => task.ErrorMessage).HasColumnName("error_message");
            entity.HasIndex(task => task.Status);
            entity.HasIndex(task => task.CreatedAt);
        });

        modelBuilder.Entity<DomainSearchResult>(entity =>
        {
            entity.ToTable("domain_search_results");
            entity.HasKey(result => result.Id);
            entity.Property(result => result.Id).HasColumnName("id");
            entity.Property(result => result.TaskId).HasColumnName("task_id");
            entity.Property(result => result.Domain).HasColumnName("domain").HasMaxLength(253);
            entity.Property(result => result.InputDomain).HasColumnName("input_domain").HasMaxLength(253);
            entity.Property(result => result.Tld).HasColumnName("tld").HasMaxLength(64);
            entity.Property(result => result.Availability).HasColumnName("availability").HasConversion<string>().HasMaxLength(32);
            entity.Property(result => result.WhoisServer).HasColumnName("whois_server").HasMaxLength(255);
            entity.Property(result => result.ErrorMessage).HasColumnName("error_message");
            entity.Property(result => result.RawWhois).HasColumnName("raw_whois");
            entity.Property(result => result.ExpirationDate).HasColumnName("expiration_date").HasConversion(
                value => value.HasValue ? value.Value.ToUnixTimeMilliseconds() : (long?)null,
                value => value.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(value.Value) : null);
            entity.Property(result => result.ProxyId).HasColumnName("proxy_id").HasMaxLength(128);
            entity.Property(result => result.ProxyElapsedMs).HasColumnName("proxy_elapsed_ms");
            entity.Property(result => result.ProxyWorkerId).HasColumnName("proxy_worker_id");
            entity.Property(result => result.DispatchInfo).HasColumnName("dispatch_info");
            entity.Property(result => result.RegistrationPriceSnapshotJson).HasColumnName("registration_price_snapshot_json");
            entity.Property(result => result.RegistrationPriceSnapshotAt).HasColumnName("registration_price_snapshot_at").HasConversion(
                value => value.HasValue ? value.Value.ToUnixTimeMilliseconds() : (long?)null,
                value => value.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(value.Value) : null);
            entity.Property(result => result.CheckedAt).HasColumnName("checked_at").HasConversion(
                value => value.ToUnixTimeMilliseconds(),
                value => DateTimeOffset.FromUnixTimeMilliseconds(value));
            entity.HasIndex(result => result.TaskId);
            entity.HasIndex(result => result.Availability);
            entity.HasIndex(result => result.Domain);
            entity.HasOne(result => result.Task)
                .WithMany(task => task.Results)
                .HasForeignKey(result => result.TaskId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
