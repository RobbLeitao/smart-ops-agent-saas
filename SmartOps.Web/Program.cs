using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartOps.Infrastructure.Data;
using SmartOps.Web.Components;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using SmartOps.Web.Plugins;

var builder = WebApplication.CreateBuilder(args);

// Register a default Kernel and the AIOpsService so integration tests and app code can resolve them.
// Kernel registration is intentionally minimal; real deployments should configure providers (OpenAI, etc.) as needed.
// Configure KernelBuilder but defer building the kernel until after application services (DbContext) are registered.
var skBuilder = Kernel.CreateBuilder();

// Expected env vars: OPENAI_API_KEY, OPENAI_MODEL_ID (e.g., 'gpt-4o' or 'gpt-4o-mini').
var openAiApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
var openAiModelId = Environment.GetEnvironmentVariable("OPENAI_MODEL_ID") ?? "gpt-4o";

if (!string.IsNullOrWhiteSpace(openAiApiKey))
{
    // Register OpenAI chat completion only when API key is present (e.g., in CI/dev machines).
    skBuilder.AddOpenAIChatCompletion(openAiApiKey, null, openAiModelId);
}

// Add services to the container (DbContext is registered below). Kernel will be built after services are configured.
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

    // If the SQLite data source is a plain relative path, anchor it to the content root
    // so the database file is always created in the app's intended location,
    // regardless of the caller's working directory.
    // SQLite special data sources (:memory:, file: URIs) are left unchanged.
    var csb = new SqliteConnectionStringBuilder(connectionString);
    if (!string.IsNullOrEmpty(csb.DataSource)
        && !Path.IsPathRooted(csb.DataSource)
        && csb.DataSource != ":memory:"
        && !csb.DataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
    {
        var env = sp.GetRequiredService<IWebHostEnvironment>();
        csb.DataSource = Path.Combine(env.ContentRootPath, csb.DataSource);
        connectionString = csb.ToString();
    }

    if (!string.IsNullOrEmpty(csb.DataSource)
        && csb.DataSource != ":memory:"
        && !csb.DataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
    {
        var directoryPath = Path.GetDirectoryName(csb.DataSource);
        if (!string.IsNullOrEmpty(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }
    }

    options.UseSqlite(connectionString);
});

// Now that services (including AppDbContext) are registered, register the DataOpsPlugin with the kernel builder.
try
{
    var tempSp = builder.Services.BuildServiceProvider();
    var plugins = (System.Collections.Generic.ICollection<Microsoft.SemanticKernel.KernelPlugin>)skBuilder.Plugins;
    plugins.AddFromType<DataOpsPlugin>(null, tempSp);
}
catch
{
    try
    {
        var plugins = (System.Collections.Generic.ICollection<Microsoft.SemanticKernel.KernelPlugin>)skBuilder.Plugins;
        plugins.AddFromType<DataOpsPlugin>();
    }
    catch
    {
        // best-effort
    }
}

// Build and register Kernel and AIOpsService (scoped) for DI after plugin registration.
var kernel = skBuilder.Build();
builder.Services.AddSingleton(kernel);
builder.Services.AddScoped<SmartOps.Web.Services.AIOpsService>();

// Also register the DataOpsPlugin in the DI container so it can be resolved and used directly.
builder.Services.AddScoped<DataOpsPlugin>();

// Proceed with building the app further below...

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

    // If the SQLite data source is a plain relative path, anchor it to the content root
    // so the database file is always created in the app's intended location,
    // regardless of the caller's working directory.
    // SQLite special data sources (:memory:, file: URIs) are left unchanged.
    var csb = new SqliteConnectionStringBuilder(connectionString);
    if (!string.IsNullOrEmpty(csb.DataSource)
        && !Path.IsPathRooted(csb.DataSource)
        && csb.DataSource != ":memory:"
        && !csb.DataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
    {
        var env = sp.GetRequiredService<IWebHostEnvironment>();
        csb.DataSource = Path.Combine(env.ContentRootPath, csb.DataSource);
        connectionString = csb.ToString();
    }

    if (!string.IsNullOrEmpty(csb.DataSource)
        && csb.DataSource != ":memory:"
        && !csb.DataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
    {
        var directoryPath = Path.GetDirectoryName(csb.DataSource);
        if (!string.IsNullOrEmpty(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }
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
