// 
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;

using SpeedyCdn.Server.AppCtx;
using SpeedyCdn.Server.Entities;
using SpeedyCdn.Server.Entities.Origin;

namespace SpeedyCdn.Server.DbContexts;

#if true // 
public class WebOriginDbContext : DbContext /**/
#endif
{
    public DbSet<AppEntity> App { get; set; }

    public DbSet<DisplayUrlEntity> DisplayUrl { get; set; }

    // 

    public WebOriginDbContext()
    {
    }

    public WebOriginDbContext(DbContextOptions options) : base(options)
    {
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        options.UseLoggerFactory(new Serilog.Extensions.Logging.SerilogLoggerFactory(LoggingCtx.LogOriginSql));

        options
            .UseSqlite(ConfigCtx.Options.OriginAppDbConnectionString);

        if (ConfigCtx.Options.OriginEnableSensitiveLogging) {
            options
                .EnableSensitiveDataLogging();
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        foreach (IMutableForeignKey fk in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()))
        {
            fk.DeleteBehavior = DeleteBehavior.Restrict;
        }
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = new CancellationToken())
    {
        IEnumerable<EntityEntry> entries = ChangeTracker
            .Entries()
            .Where(e => e.State == EntityState.Modified);

        foreach (EntityEntry entityEntry in entries)
        {
            entityEntry.Entity.GetType().GetProperty("Updated")?.SetValue(entityEntry.Entity, DateTime.UtcNow);
        }

        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        IEnumerable<EntityEntry> entries = ChangeTracker
            .Entries()
            .Where(e => e.State == EntityState.Modified);

        foreach (EntityEntry entityEntry in entries)
        {
            entityEntry.Entity.GetType().GetProperty("Updated")?.SetValue(entityEntry.Entity, DateTime.UtcNow);
        }

        return base.SaveChanges();
    }
}
