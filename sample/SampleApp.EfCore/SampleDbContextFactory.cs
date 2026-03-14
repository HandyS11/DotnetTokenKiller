using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SampleApp.EfCore;

/// <summary>Design-time factory for creating <see cref="SampleDbContext"/> instances used by EF migrations.</summary>
public class SampleDbContextFactory : IDesignTimeDbContextFactory<SampleDbContext>
{
    /// <summary>Creates a new <see cref="SampleDbContext"/> configured for SQLite.</summary>
    /// <param name="args">Command-line arguments (unused).</param>
    /// <returns>A configured <see cref="SampleDbContext"/> instance.</returns>
    public SampleDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SampleDbContext>()
            .UseSqlite("Data Source=sample.db")
            .Options;

        return new SampleDbContext(options);
    }
}
