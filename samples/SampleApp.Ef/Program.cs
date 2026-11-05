using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SampleApp.Ef;
var builder = Host.CreateApplicationBuilder(args);

// SAMPLE_EF_QUIET=1 drops the console logger, so `dotnet ef database update` prints only ef's own lines.
if (Environment.GetEnvironmentVariable("SAMPLE_EF_QUIET") == "1")
{
    builder.Logging.ClearProviders();
}
else
{
    builder.Logging.AddSimpleConsole(options => options.SingleLine = false);
    builder.Logging.SetMinimumLevel(LogLevel.Information);
}

builder.Services.AddDbContext<BlogContext>(options => options.UseSqlite("Data Source=app.db"));

using var host = builder.Build();
await host.RunAsync().ConfigureAwait(false);
