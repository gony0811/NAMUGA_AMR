using AMR.Models;
using Microsoft.EntityFrameworkCore;

namespace AMR.Data;

public class AmrDbContext : DbContext
{
    public AmrDbContext(DbContextOptions<AmrDbContext> options) : base(options) { }

    public DbSet<LocationTagMapping> LocationTagMappings => Set<LocationTagMapping>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LocationTagMapping>(entity =>
        {
            entity.HasIndex(e => e.LocationTag).IsUnique();
        });
    }
}
