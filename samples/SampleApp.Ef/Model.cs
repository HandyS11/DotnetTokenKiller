using Microsoft.EntityFrameworkCore;

namespace SampleApp.Ef;

public class Blog
{
    public int Id { get; set; }

    public string Title { get; set; } = "";
}

public class BlogContext(DbContextOptions<BlogContext> options) : DbContext(options)
{
    public DbSet<Blog> Blogs => Set<Blog>();
}
