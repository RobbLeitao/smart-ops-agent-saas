using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartOps.Infrastructure.Data;
using SmartOps.Web.Components;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using SmartOps.Web.Plugins;

var builder = WebApplication.CreateBuilder(args);

// Register Kernel and AIOpsService; build kernel after application services (DbContext) are registered.
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

// Defer Kernel construction to a factory so we can use the final IServiceProvider without calling BuildServiceProvider.
// Register Kernel as a singleton using a factory that receives the application's IServiceProvider.
builder.Services.AddSingleton<Kernel>(sp =>
{
    var kb = Kernel.CreateBuilder();

    if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY")))
    {
        var modelId = Environment.GetEnvironmentVariable("OPENAI_MODEL_ID") ?? "gpt-4o";
        kb.AddOpenAIChatCompletion(Environment.GetEnvironmentVariable("OPENAI_API_KEY")!, null, modelId);
    }

    try
    {
        var plugins = (System.Collections.Generic.ICollection<Microsoft.SemanticKernel.KernelPlugin>)kb.Plugins;
        plugins.AddFromType<DataOpsPlugin>(null, sp);
    }
    catch
    {
        try
        {
            var plugins = (System.Collections.Generic.ICollection<Microsoft.SemanticKernel.KernelPlugin>)kb.Plugins;
            plugins.AddFromType<DataOpsPlugin>();
        }
        catch
        {
        }
    }

    var builtKernel = kb.Build();

    try
    {
        var kernelType = builtKernel.GetType();
        var regMethod = kernelType.GetMethod("RegisterNativeFunction")
                        ?? kernelType.GetMethod("RegisterFunction")
                        ?? kernelType.GetMethod("AddFunction")
                        ?? kernelType.GetMethod("RegisterSemanticFunction");

        if (regMethod != null)
        {
            System.Func<System.Threading.Tasks.Task<string>> del = async () =>
            {
                var plugin = sp.GetService<DataOpsPlugin>() ?? ActivatorUtilities.CreateInstance<DataOpsPlugin>(sp);
                return await plugin.GetFailedTransactionsAsync();
            };

            try
            {
                regMethod.Invoke(builtKernel, new object[] { "DataOps.GetFailedTransactions", del });
            }
            catch
            {
            }
        }
    }
    catch
    {
    }

    return builtKernel;
});

// Register AIOpsService, DiagnosticOrchestratorService and plugin in DI
builder.Services.AddScoped<SmartOps.Web.Services.AIOpsService>();
builder.Services.AddScoped<SmartOps.Web.Services.IAIOpsService>(sp => sp.GetRequiredService<SmartOps.Web.Services.AIOpsService>());
builder.Services.AddScoped<DataOpsPlugin>();
builder.Services.AddScoped<SmartOps.Web.Services.DiagnosticOrchestratorService>();

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
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();
    }
    catch
    {
        // In test environments we may replace the DbContext provider (InMemory) which can
        // conflict with the application's registered provider. Ignore failures here so tests can
        // control database initialization.
    }
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

// Diagnostics endpoint (POST to accept webhook-style requests)
app.MapPost("/api/diagnostics/{transactionId}", async (Guid transactionId, SmartOps.Web.Services.DiagnosticOrchestratorService orchestrator) =>
{
    var result = await orchestrator.RunDiagnosticAsync(transactionId);

    if (string.IsNullOrWhiteSpace(result) || result.Contains("NotFound", StringComparison.OrdinalIgnoreCase) || result.Contains("No failed", StringComparison.OrdinalIgnoreCase))
    {
        return Results.NotFound();
    }

    return Results.Content(result, "text/markdown");
});

app.Run();

// Expose Program to the integration test project
public partial class Program { }
