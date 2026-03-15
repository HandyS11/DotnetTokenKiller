using Microsoft.EntityFrameworkCore;

namespace SampleApp.EfCore;

/// <summary>EF Core database context for the sample application.</summary>
public class SampleDbContext : DbContext
{
    /// <summary>Initializes a new instance of <see cref="SampleDbContext"/>.</summary>
    /// <param name="options">The database context options.</param>
    public SampleDbContext(DbContextOptions<SampleDbContext> options) : base(options) { }

    /// <summary>Gets or sets the sample items table.</summary>
    public DbSet<SampleItem> Items { get; set; } = null!;
}
