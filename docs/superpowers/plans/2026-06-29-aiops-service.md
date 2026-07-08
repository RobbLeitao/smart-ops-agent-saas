# AIOpsService Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an `AIOpsService` to `SmartOps.Web` that receives a Semantic Kernel `Kernel` via DI and executes prompts through `Kernel.InvokePromptAsync` with automatic function choice enabled.

**Architecture:** Keep the first iteration intentionally small. Add one focused service class in `SmartOps.Web\Services`, verify its behavior with a web test that uses a fake chat completion service, and register both `Kernel` and `AIOpsService` in `Program.cs` so the application can resolve the service immediately while leaving real OpenAI wiring for a later change.

**Tech Stack:** ASP.NET Core 9, xUnit, Microsoft.SemanticKernel 1.77.0

---

## File structure

- Create: `SmartOps.Web\Services\AIOpsService.cs` — thin application service that wraps `Kernel.InvokePromptAsync`.
- Create: `SmartOps.Web.Tests\AIOpsServiceTests.cs` — unit-style Semantic Kernel test plus app DI resolution smoke test.
- Modify: `SmartOps.Web\Program.cs` — register a base `Kernel` instance and `AIOpsService` in the app container.

### Task 1: Add failing tests for the service contract

**Files:**
- Create: `SmartOps.Web.Tests\AIOpsServiceTests.cs`

- [ ] **Step 1: Create the failing test file**

```csharp
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using SmartOps.Web.Services;
using Xunit;

namespace SmartOps.Web.Tests;

public sealed class AIOpsServiceTests
{
    [Fact]
    public async Task ExecutePromptAsync_ReturnsModelResponseAndEnablesAutoFunctionChoice()
    {
        var completionService = new RecordingChatCompletionService("SmartOps AI reply");
        var builder = Kernel.CreateBuilder();
        builder.Services.AddSingleton<IChatCompletionService>(completionService);

        var service = new AIOpsService(builder.Build());

        var result = await service.ExecutePromptAsync("Summarize the current incidents.");

        Assert.Equal("SmartOps AI reply", result);
        Assert.NotNull(completionService.LastExecutionSettings);
        Assert.NotNull(completionService.LastExecutionSettings!.FunctionChoiceBehavior);
        Assert.Equal(
            "AutoFunctionChoiceBehavior",
            completionService.LastExecutionSettings.FunctionChoiceBehavior!.GetType().Name);
    }

    [Fact]
    public void ApplicationServices_CanResolveAIOpsService()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var scope = factory.Services.CreateScope();

        var service = scope.ServiceProvider.GetRequiredService<AIOpsService>();

        Assert.NotNull(service);
    }

    private sealed class RecordingChatCompletionService(string response) : IChatCompletionService
    {
        public PromptExecutionSettings? LastExecutionSettings { get; private set; }

        public IReadOnlyDictionary<string, object?> Attributes { get; } =
            new Dictionary<string, object?>();

        public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            CancellationToken cancellationToken = default)
        {
            LastExecutionSettings = executionSettings;

            IReadOnlyList<ChatMessageContent> messages =
            [
                new(AuthorRole.Assistant, response)
            ];

            return Task.FromResult(messages);
        }

        public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastExecutionSettings = executionSettings;
            await Task.CompletedTask;
            yield break;
        }
    }
}
```

- [ ] **Step 2: Run the targeted tests to verify they fail**

Run:

```powershell
dotnet test .\SmartOps.Web.Tests\SmartOps.Web.Tests.csproj --filter AIOpsServiceTests -v minimal
```

Expected: FAIL because `SmartOps.Web.Services.AIOpsService` does not exist yet and `Program.cs` does not register it.

- [ ] **Step 3: Commit the failing test**

```powershell
git add .\SmartOps.Web.Tests\AIOpsServiceTests.cs
git commit -m "test: cover aiops service prompt execution"
```

### Task 2: Implement the Semantic Kernel wrapper service

**Files:**
- Create: `SmartOps.Web\Services\AIOpsService.cs`
- Test: `SmartOps.Web.Tests\AIOpsServiceTests.cs`

- [ ] **Step 1: Create `AIOpsService`**

```csharp
using Microsoft.SemanticKernel;

namespace SmartOps.Web.Services;

public sealed class AIOpsService
{
    private readonly Kernel _kernel;

    public AIOpsService(Kernel kernel)
    {
        _kernel = kernel;
    }

    public async Task<string> ExecutePromptAsync(string userPrompt)
    {
        var settings = new PromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
        };

        var result = await _kernel.InvokePromptAsync(userPrompt, new(settings));

        return result.ToString();
    }
}
```

- [ ] **Step 2: Run the targeted tests again**

Run:

```powershell
dotnet test .\SmartOps.Web.Tests\SmartOps.Web.Tests.csproj --filter AIOpsServiceTests -v minimal
```

Expected: the `ExecutePromptAsync_ReturnsModelResponseAndEnablesAutoFunctionChoice` test passes, while `ApplicationServices_CanResolveAIOpsService` still fails because the app container is not registering the service yet.

- [ ] **Step 3: Commit the service implementation**

```powershell
git add .\SmartOps.Web\Services\AIOpsService.cs .\SmartOps.Web.Tests\AIOpsServiceTests.cs
git commit -m "feat: add aiops service wrapper"
```

### Task 3: Register Kernel and AIOpsService in the web app

**Files:**
- Modify: `SmartOps.Web\Program.cs`
- Test: `SmartOps.Web.Tests\AIOpsServiceTests.cs`

- [ ] **Step 1: Register the base `Kernel` and `AIOpsService`**

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using SmartOps.Infrastructure.Data;
using SmartOps.Web.Components;
using SmartOps.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSingleton(_ => Kernel.CreateBuilder().Build());
builder.Services.AddScoped<AIOpsService>();

// Read connection string from the finalized IConfiguration (which includes
// any overrides added by WebApplicationFactory.ConfigureAppConfiguration).
builder.Services.AddDbContext<AppDbContext>((sp, options) =>
{
    var config = sp.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
    var connectionString = config.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException(
            "Connection string 'DefaultConnection' is not configured. " +
            "Add it to appsettings.json or environment variables.");

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
```

- [ ] **Step 2: Run the targeted tests and confirm they pass**

Run:

```powershell
dotnet test .\SmartOps.Web.Tests\SmartOps.Web.Tests.csproj --filter AIOpsServiceTests -v minimal
```

Expected: PASS, with one test proving the service returns a simple string and enables automatic function choice, and the other proving the app container can resolve `AIOpsService`.

- [ ] **Step 3: Run build-level regression coverage**

Run:

```powershell
dotnet build .\SmartOps.Web\SmartOps.Web.csproj -c Release
```

Expected: build succeeds with 0 errors.

- [ ] **Step 4: Commit the DI wiring**

```powershell
git add .\SmartOps.Web\Program.cs .\SmartOps.Web\Services\AIOpsService.cs .\SmartOps.Web.Tests\AIOpsServiceTests.cs
git commit -m "feat: register aiops service with semantic kernel"
```
