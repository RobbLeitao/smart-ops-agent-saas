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

// Read AI configuration from appsettings.json or environment variables
var aiSection = builder.Configuration.GetSection("AI");
var aiProvider = aiSection.GetValue<string>("Provider") ?? Environment.GetEnvironmentVariable("AI_PROVIDER");
var aiApiKey = aiSection.GetValue<string>("ApiKey") ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
var aiModelId = aiSection.GetValue<string>("Model") ?? Environment.GetEnvironmentVariable("OPENAI_MODEL_ID") ?? "gpt-4o";

if (!string.IsNullOrWhiteSpace(aiApiKey) && !string.IsNullOrWhiteSpace(aiProvider))
{
    // Support provider selection; default to OpenAI if provider mentions "openai"
    if (aiProvider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase) || aiProvider.Contains("openai", StringComparison.OrdinalIgnoreCase))
    {
        skBuilder.AddOpenAIChatCompletion(aiApiKey, null, aiModelId);
    }
    else if (aiProvider.Equals("AzureOpenAI", StringComparison.OrdinalIgnoreCase) || aiProvider.Contains("azure", StringComparison.OrdinalIgnoreCase))
    {
        // If using Azure, expect ApiKey and Endpoint in config
        var endpoint = aiSection.GetValue<string>("Endpoint") ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            skBuilder.AddAzureOpenAIChatCompletion(endpoint, aiApiKey, aiModelId);
        }
        else
        {
            // Fallback to OpenAI if endpoint missing
            skBuilder.AddOpenAIChatCompletion(aiApiKey, null, aiModelId);
        }
    }
    else
    {
        // Unknown provider: attempt OpenAI
        skBuilder.AddOpenAIChatCompletion(aiApiKey, null, aiModelId);
    }
}
else
{
    // No AI key/config: register a development fake adapter so the Kernel can execute in dev mode.
    // Register the adapter for the common interfaces the kernel may resolve during execution.
    builder.Services.AddSingleton<SmartOps.Web.Services.DevFakeKernelAdapter>();
    builder.Services.AddSingleton<Microsoft.SemanticKernel.ChatCompletion.IChatCompletionService>(sp => sp.GetRequiredService<SmartOps.Web.Services.DevFakeKernelAdapter>());
    builder.Services.AddSingleton<Microsoft.SemanticKernel.TextGeneration.ITextGenerationService>(sp => sp.GetRequiredService<SmartOps.Web.Services.DevFakeKernelAdapter>());
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

// Register ITransactionAnalyzer abstraction and choose implementation based on configuration
var openAiSection = builder.Configuration.GetSection("OpenAI");
var openAiKey = openAiSection.GetValue<string>("ApiKey");
if (!string.IsNullOrWhiteSpace(openAiKey))
{
    // If an OpenAI key exists, use the orchestrator-backed analyzer which will use the Kernel/OpenAI path
    builder.Services.AddScoped<SmartOps.Web.Services.ITransactionAnalyzer, SmartOps.Web.Services.OpenAITransactionAnalyzer>();
}
else
{
    // Default to DevFake analyzer in development/no-key scenarios
    builder.Services.AddSingleton<SmartOps.Web.Services.ITransactionAnalyzer, SmartOps.Web.Services.DevFakeTransactionAnalyzer>();
}

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
        Program.EnsureColumnExists(db, "Transactions", "CardLast4", "TEXT");
        Program.EnsureColumnExists(db, "Diagnostics", "CardLast4", "TEXT");
        Program.EnsureColumnExists(db, "Diagnostics", "WasHelpful", "INTEGER");
        Program.BackfillTransactionCards(db);
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

// Stripe webhook receiver
app.MapPost("/api/webhooks/stripe/{transactionId?}", async (HttpRequest req, string? transactionId, SmartOps.Web.Services.DiagnosticOrchestratorService orchestrator) =>
{
    Guid? txId = null;

    // Prefer transactionId from URL if present
    if (!string.IsNullOrWhiteSpace(transactionId) && Guid.TryParse(transactionId, out var parsed))
    {
        txId = parsed;
    }
    else
    {
        // Try to parse JSON body for known Stripe-like structure
        try
        {
            using var sr = new StreamReader(req.Body);
            var body = await sr.ReadToEndAsync();
            if (!string.IsNullOrWhiteSpace(body))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(body);
                    // Common places: data.object.metadata.transactionId or data.object.id
                    if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == System.Text.Json.JsonValueKind.Object)
                    {
                        if (data.TryGetProperty("object", out var obj) && obj.ValueKind == System.Text.Json.JsonValueKind.Object)
                        {
                            if (obj.TryGetProperty("metadata", out var meta) && meta.ValueKind == System.Text.Json.JsonValueKind.Object)
                            {
                                if (meta.TryGetProperty("transactionId", out var txEl) && txEl.ValueKind == System.Text.Json.JsonValueKind.String && Guid.TryParse(txEl.GetString(), out var idFromMeta))
                                {
                                    txId = idFromMeta;
                                }
                            }

                            // Fallback: try object.id if it looks like a guid
                            if (txId == null && obj.TryGetProperty("id", out var objId) && objId.ValueKind == System.Text.Json.JsonValueKind.String && Guid.TryParse(objId.GetString(), out var idFromObj))
                            {
                                txId = idFromObj;
                            }
                        }
                    }
            }
        }
        catch
        {
            // ignore parse errors
        }
    }

    if (txId == null)
    {
        return Results.BadRequest("transactionId not provided in URL or request body");
    }

    var result = await orchestrator.RunDiagnosticAsync(txId.Value);

    if (string.IsNullOrWhiteSpace(result) || result.Contains("NotFound", StringComparison.OrdinalIgnoreCase) || result.Contains("No failed", StringComparison.OrdinalIgnoreCase))
    {
        return Results.NotFound();
    }

    return Results.Content(result, "text/markdown");
});

app.Run();

// Expose Program to the integration test project
public partial class Program
{
    public static void EnsureColumnExists(AppDbContext db, string tableName, string columnName, string sqliteType)
    {
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
        {
            connection.Open();
        }

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info({tableName});";
            using var reader = command.ExecuteReader();
            var exists = false;
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }

            if (!exists)
            {
                using var alter = connection.CreateCommand();
                alter.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {sqliteType};";
                alter.ExecuteNonQuery();
            }
        }
        finally
        {
            if (shouldClose)
            {
                connection.Close();
            }
        }
    }

    public static void BackfillTransactionCards(AppDbContext db)
    {
        var transactions = db.Transactions.Where(t => string.IsNullOrWhiteSpace(t.CardLast4)).ToList();
        if (transactions.Count == 0)
        {
            return;
        }

        foreach (var tx in transactions)
        {
            tx.CardLast4 = CreateStableCardLast4(tx.Id);
        }

        db.SaveChanges();
    }

    private static string CreateStableCardLast4(int transactionId)
    {
        var value = ((transactionId * 137) + 4242) % 10000;
        return value.ToString("D4");
    }
}
