using System.Linq.Expressions;
using BlueProcure.Domain.Common;
using BlueProcure.Domain.Settings;
using Microsoft.EntityFrameworkCore;

namespace BlueProcure.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(Entity).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            var builder = modelBuilder.Entity(entityType.ClrType);
            builder.Property(nameof(Entity.Id)).ValueGeneratedNever();

            // PostgreSQL xmin system column as optimistic concurrency token.
            builder.Property(nameof(Entity.RowVersion)).IsRowVersion();

            // Soft delete: hide deleted rows by default.
            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var filter = Expression.Lambda(
                Expression.Not(Expression.Property(parameter, nameof(Entity.IsDeleted))),
                parameter);
            builder.HasQueryFilter(filter);
        }
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);
    }
}
