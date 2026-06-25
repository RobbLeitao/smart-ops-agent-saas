using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartOps.Infrastructure.Data;
using SmartOps.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Read connection string from the finalized IConfiguration (which includes
// any overrides added by WebApplicationFactory.ConfigureAppConfiguration).
builder.Services.AddDbContext<AppDbContext>((sp, options) =>
{
    var config = sp.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
    var connectionString = config.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException(
            "Connection string 'DefaultConnection' is not configured. " +
            "Add it to appsettings.json or environment variables.");

    // If the SQLite data source is a relative path, anchor it to the content root
    // so the database file is always created in the app's intended location,
    // regardless of the caller's working directory.
    var csb = new SqliteConnectionStringBuilder(connectionString);
    if (!string.IsNullOrEmpty(csb.DataSource) && !Path.IsPathRooted(csb.DataSource))
    {
        var env = sp.GetRequiredService<IWebHostEnvironment>();
        csb.DataSource = Path.Combine(env.ContentRootPath, csb.DataSource);
        connectionString = csb.ToString();
    }

    options.UseSqlite(connectionString);
});

var app = builder.Build();

// Ensure the database schema exists on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

// Release pooled SQLite connections when the app stops so callers
// (including integration tests) can safely delete the database file.
app.Lifetime.ApplicationStopping.Register(SqliteConnection.ClearAllPools);

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();


app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

// Expose Program to the integration test project
public partial class Program { }
