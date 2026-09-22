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

// Register TransactionNotifier which bridges publisher events to SignalR
builder.Services.AddSingleton<SmartOps.Web.Services.TransactionNotifier>();
    builder.Services.AddSingleton<SmartOps.Web.Services.TransactionDrawerService>();
// Ensure HubContext is available via SignalR server support; SignalR is included in ASP.NET Core
builder.Services.AddSignalR();

// Enable verbose SignalR logging to surface InvalidDataException stack traces in Output window
builder.Logging.AddFilter("Microsoft.AspNetCore.SignalR", LogLevel.Debug);
builder.Logging.AddFilter("Microsoft.AspNetCore.Http.Connections", LogLevel.Debug);
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

// Register transaction publisher provider based on configuration (Simulator | External)
var txSection = builder.Configuration.GetSection("TransactionSettings");
var txProvider = txSection.GetValue<string>("Provider") ?? "Simulator";
var txInterval = txSection.GetValue<int?>("IntervalSeconds") ?? 8;

// Bind settings for services that need them
builder.Services.Configure<SmartOps.Infrastructure.TransactionSettings>(opt =>
{
    opt.Provider = txProvider;
    opt.IntervalSeconds = txInterval;
});

if (txProvider.Equals("Simulator", StringComparison.OrdinalIgnoreCase))
{
    // Register simulator as singleton publisher and hosted service
    builder.Services.AddSingleton<SmartOps.Infrastructure.SimulatorTransactionService>();
    builder.Services.AddSingleton<SmartOps.Core.Interfaces.ITransactionPublisher>(sp => sp.GetRequiredService<SmartOps.Infrastructure.SimulatorTransactionService>());
    builder.Services.AddHostedService(sp => sp.GetRequiredService<SmartOps.Infrastructure.SimulatorTransactionService>());
}
else
{
    // Fallback: register a no-op publisher so DI resolutions succeed
    builder.Services.AddSingleton<SmartOps.Infrastructure.NoopTransactionPublisher>();
    builder.Services.AddSingleton<SmartOps.Core.Interfaces.ITransactionPublisher>(sp => sp.GetRequiredService<SmartOps.Infrastructure.NoopTransactionPublisher>());
}

// Add Identity and auth services
builder.Services.AddIdentity<SmartOps.Infrastructure.Data.ApplicationUser, Microsoft.AspNetCore.Identity.IdentityRole>(options =>
{
    options.User.RequireUniqueEmail = true;
})
    .AddEntityFrameworkStores<SmartOps.Infrastructure.Data.AppDbContext>();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/login";
    // Avoid adding ReturnUrl query parameter — keep login URL clean for UX
    options.ReturnUrlParameter = string.Empty;
    // Redirect unauthorized (wrong role) straight to Dashboard, no query params
    options.Events.OnRedirectToAccessDenied = ctx =>
    {
        ctx.Response.Redirect("/");
        return Task.CompletedTask;
    };
});

builder.Services.AddAuthentication(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie();

builder.Services.AddAuthorization();

// Register HttpClient for server-side components so Blazor pages can inject HttpClient (e.g., Login.razor)
builder.Services.AddHttpClient();


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

// Debug endpoint to capture client-side clicks and report to server logs
app.MapPost("/api/debug/click", async (HttpContext http, ILogger<Program> logger) =>
{
    logger.LogInformation("/api/debug/click received from {RemoteIp}", http.Connection.RemoteIpAddress);
    await http.Response.WriteAsync("ok");
});

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
        Program.EnsureColumnExists(db, "Diagnostics", "IsUseful", "INTEGER");
        Program.EnsureColumnExists(db, "Diagnostics", "UserId", "TEXT");
        Program.EnsureColumnExists(db, "Diagnostics", "CreatedByUserId", "TEXT");
        Program.BackfillTransactionCards(db);
        Program.SeedDemoTransactionsAndDiagnostics(db);

        // Seed roles and test users
        try
        {
            var roleManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.RoleManager<Microsoft.AspNetCore.Identity.IdentityRole>>();
            var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<SmartOps.Infrastructure.Data.ApplicationUser>>();

            var roles = new[] { "Operador", "Auditor" };
            foreach (var r in roles)
            {
                if (!await roleManager.RoleExistsAsync(r))
                {
                    await roleManager.CreateAsync(new Microsoft.AspNetCore.Identity.IdentityRole(r));
                }
            }

            async Task EnsureUserAsync(string email, string role)
            {
                var existing = await userManager.FindByEmailAsync(email);
                if (existing == null)
                {
                    var user = new SmartOps.Infrastructure.Data.ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
                    var pw = "P@ssw0rd!"; // dev/test password
                    var create = await userManager.CreateAsync(user, pw);
                    if (create.Succeeded)
                    {
                        await userManager.AddToRoleAsync(user, role);
                    }
                }
            }

            await EnsureUserAsync("operador@smartops.com", "Operador");
            await EnsureUserAsync("admin@smartops.com", "Auditor");

            var admin = await userManager.FindByEmailAsync("admin@smartops.com");
            if (admin != null)
            {
                var diagnosticsToBackfill = db.Diagnostics.Where(d => d.CreatedByUserId == null).ToList();
                if (diagnosticsToBackfill.Count > 0)
                {
                    foreach (var diag in diagnosticsToBackfill)
                    {
                        diag.CreatedByUserId = admin.Id;
                    }

                    db.SaveChanges();
                }
            }
        }
        catch
        {
            // ignore seeding errors in constrained/test environments
        }

        // Ensure a simple key-value settings table exists for runtime persistence of integrations/configuration
        try
        {
            using var cmd = db.Database.GetDbConnection().CreateCommand();
            if (cmd.Connection.State != System.Data.ConnectionState.Open) cmd.Connection.Open();
            cmd.CommandText = "CREATE TABLE IF NOT EXISTS Settings (Key TEXT PRIMARY KEY, Value TEXT);";
            cmd.ExecuteNonQuery();
        }
        catch
        {
            // ignore
        }
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

// Force instantiate TransactionNotifier to ensure it subscribes to the publisher events.
try
{
    var _notifier = app.Services.GetService<SmartOps.Web.Services.TransactionNotifier>();
}
catch { }

// Temporary DI probe: subscribe a console handler to ITransactionPublisher to verify events are emitted.
try
{
    var publisher = app.Services.GetService<SmartOps.Core.Interfaces.ITransactionPublisher>();
    if (publisher != null)
    {
        publisher.OnTransactionCreated += (s, e) =>
        {
            try
            {
                Console.WriteLine($"[DI-PROBE] Event fired for transaction {e.Transaction.Id} status={e.Transaction.Status} gateway={e.Transaction.GatewayReference}");
            }
            catch { }
        };
        Console.WriteLine("[DI-PROBE] Subscribed temporary console handler to ITransactionPublisher.");
    }
    else
    {
        Console.WriteLine("[DI-PROBE] ITransactionPublisher not registered/resolvable.");
    }
}
catch (Exception ex)
{
    Console.WriteLine("[DI-PROBE] Exception while subscribing probe: " + ex);
}


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

// Map SignalR hub for transactions
app.MapHub<SmartOps.Web.Hubs.TransactionHub>("/hubs/transactions");

// Temporary diagnostic endpoint to inspect DI and publisher subscribers
app.MapGet("/debug/diag", (IServiceProvider sp) =>
{
    var pid = System.Diagnostics.Process.GetCurrentProcess().Id;
    var notifier = sp.GetService<SmartOps.Web.Services.TransactionNotifier>();
    var publisher = sp.GetService<SmartOps.Core.Interfaces.ITransactionPublisher>();
    int subscriberCount = 0;
    try
    {
        var ev = publisher?.GetType().GetEvent("OnTransactionCreated");
        if (ev != null)
        {
            var fi = publisher.GetType().GetField("OnTransactionCreated", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (fi != null)
            {
                var dlg = fi.GetValue(publisher) as MulticastDelegate;
                if (dlg != null) subscriberCount = dlg.GetInvocationList().Length;
            }
            else
            {
                // Try property-style backing field
                var prop = publisher.GetType().GetField("_onTransactionCreated", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (prop != null)
                {
                    var dlg2 = prop.GetValue(publisher) as MulticastDelegate;
                    if (dlg2 != null) subscriberCount = dlg2.GetInvocationList().Length;
                }
            }
        }
    }
    catch { }

    return Results.Json(new { pid, notifierPresent = notifier != null, publisherPresent = publisher != null, subscriberCount });
});

// Minimal account endpoints for login to set cookies outside the Blazor circuit
app.MapPost("/account/login", async (HttpContext http, Microsoft.AspNetCore.Identity.UserManager<SmartOps.Infrastructure.Data.ApplicationUser> userManager, Microsoft.AspNetCore.Identity.SignInManager<SmartOps.Infrastructure.Data.ApplicationUser> signInManager) =>
{
    try
    {
        using var sr = new StreamReader(http.Request.Body);
        var body = await sr.ReadToEndAsync();
        Console.Error.WriteLine($"[DEBUG] /account/login body: '{body}'");
        if (string.IsNullOrWhiteSpace(body)) return Results.BadRequest(new { error = "Los campos están vacíos." });
        var dict = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string,string>>(body, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        var email = dict != null && dict.TryGetValue("Email", out var e) ? e : string.Empty;
        var password = dict != null && dict.TryGetValue("Password", out var p) ? p : string.Empty;
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password)) return Results.BadRequest(new { error = "Los campos están vacíos." });

        var user = await userManager.FindByEmailAsync(email);
        if (user == null)
        {
            return Results.Unauthorized();
        }

        var check = await signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: false);
        if (!check.Succeeded)
        {
            return Results.Unauthorized();
        }

        await signInManager.SignInAsync(user, isPersistent: false);
        return Results.Ok();
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Account login endpoint error: {ex}");
        return Results.StatusCode(500);
    }
}).WithMetadata(new Microsoft.AspNetCore.Mvc.IgnoreAntiforgeryTokenAttribute());

app.MapGet("/account/logout", async (HttpContext http, Microsoft.AspNetCore.Identity.SignInManager<SmartOps.Infrastructure.Data.ApplicationUser> signInManager) =>
{
    await signInManager.SignOutAsync();
    return Results.Redirect("/login");
});

// DTO used for the login endpoint
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

    public static void SeedDemoTransactionsAndDiagnostics(AppDbContext db)
    {
        var existingCount = db.Transactions.Count();
        var targetCount = 26;
        if (existingCount < targetCount)
        {
            var sampleStatuses = new[]
            {
                ("Approved", null),
                ("Success", null),
                ("Failed", "Insufficient funds"),
                ("Failed", "Card declined"),
                ("Failed", "Expired card"),
                ("Failed", "Suspected fraud"),
                ("Failed", "Stolen card")
            };

            var nextId = db.Transactions.Any() ? db.Transactions.Max(t => t.Id) + 1 : 1;
            var now = DateTime.UtcNow;
            var toAdd = new List<SmartOps.Core.Entities.Transaction>();
            var index = 0;
            while (db.Transactions.Count() + toAdd.Count < targetCount)
            {
                var pair = sampleStatuses[index % sampleStatuses.Length];
                toAdd.Add(new SmartOps.Core.Entities.Transaction
                {
                    Id = nextId + index,
                    CustomerId = 1,
                    Amount = 10m + (index * 7),
                    Currency = index % 2 == 0 ? "USD" : "EUR",
                    Status = pair.Item1,
                    GatewayReference = $"DEMO_{nextId + index}",
                    CardLast4 = ((((index * 137) + 4242) % 10000)).ToString("D4"),
                    Provider = "Stripe",
                    ErrorMessage = pair.Item2,
                    OccurredAt = now.AddMinutes(-(index + 1) * 11)
                });
                index++;
            }

            db.Transactions.AddRange(toAdd);
            db.SaveChanges();
        }

        if (db.Diagnostics.Count() < 20)
        {
            var admin = db.Users.FirstOrDefault(u => u.Email == "admin@smartops.com");
            var operatorUser = db.Users.FirstOrDefault(u => u.Email == "operador@smartops.com");
            var creatorIds = new[] { admin?.Id, operatorUser?.Id }.Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().ToArray();
            var failedTxs = db.Transactions.Where(t => t.Status == "Failed").OrderByDescending(t => t.OccurredAt).ToList();
            var existingDiagTxIds = db.Diagnostics.Select(d => d.TransactionId).ToHashSet();
            var diagIndex = 0;

            foreach (var tx in failedTxs)
            {
                if (existingDiagTxIds.Contains(tx.Id)) continue;
                db.Diagnostics.Add(new SmartOps.Core.Entities.Diagnostic
                {
                    Id = Guid.NewGuid(),
                    TransactionId = tx.Id,
                    CreatedAt = DateTime.UtcNow.AddMinutes(-(diagIndex + 1) * 10),
                    Markdown = $"## 🔍 Resumen del Error\n{tx.ErrorMessage}\n\n## 🛠️ Acciones Recomendadas para el Operador\n- Revisar la tarjeta del cliente.\n\n## 📨 Mensaje sugerido para el cliente\nEstimado cliente, estamos investigando su pago.",
                    CardLast4 = tx.CardLast4,
                    CreatedByUserId = creatorIds.Length == 0 ? null : creatorIds[diagIndex % creatorIds.Length]
                });
                diagIndex++;
                if (db.Diagnostics.Count() >= 20) break;
            }

            db.SaveChanges();
        }
    }

    private static string CreateStableCardLast4(int transactionId)
    {
        var value = ((transactionId * 137) + 4242) % 10000;
        return value.ToString("D4");
    }
}
