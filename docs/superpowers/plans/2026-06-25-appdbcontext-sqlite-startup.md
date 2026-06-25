# AppDbContext SQLite Startup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Register `AppDbContext` in `SmartOps.Web` with a SQLite connection string pointing to `smartops.db`, and create the database automatically on application startup.

**Architecture:** Keep the feature inside the existing web app startup path. Store the SQLite connection string in `SmartOps.Web\appsettings.json`, register `AppDbContext` through DI in `Program.cs`, then resolve the context inside a startup scope and call `Database.EnsureCreated()` so the file-backed SQLite database and seeded schema exist before requests are served.

**Tech Stack:** ASP.NET Core 9, Entity Framework Core 9, SQLite provider, xUnit integration test with `WebApplicationFactory`

---

## File structure

- Create: `SmartOps.Web.Tests\SmartOps.Web.Tests.csproj` — test project for web app startup verification
- Create: `SmartOps.Web.Tests\DatabaseStartupTests.cs` — integration test proving startup creates `smartops.db` and exposes seeded data
- Modify: `SmartOpsAgent.sln` — add the new test project to the solution
- Modify: `SmartOps.Web\Program.cs` — register `AppDbContext`, call `EnsureCreated()`, expose `Program` for integration tests
- Modify: `SmartOps.Web\appsettings.json` — add the default SQLite connection string

### Task 1: Add a failing startup integration test

**Files:**
- Create: `SmartOps.Web.Tests\SmartOps.Web.Tests.csproj`
- Create: `SmartOps.Web.Tests\DatabaseStartupTests.cs`
- Modify: `SmartOpsAgent.sln`

- [ ] **Step 1: Create the web test project file**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="9.0.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\SmartOps.Web\SmartOps.Web.csproj" />
    <ProjectReference Include="..\SmartOps.Infrastructure\SmartOps.Infrastructure.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Add the test project to the solution**

Run:

```powershell
dotnet sln .\SmartOpsAgent.sln add .\SmartOps.Web.Tests\SmartOps.Web.Tests.csproj
```

Expected: the CLI reports that `SmartOps.Web.Tests.csproj` was added to the solution.

- [ ] **Step 3: Write the failing integration test**

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartOps.Infrastructure.Data;

namespace SmartOps.Web.Tests;

public sealed class DatabaseStartupTests
{
    [Fact]
    public async Task DatabaseStartup_CreatesSqliteDatabaseFile()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), $"smartops-web-{Guid.NewGuid():N}");
        Directory.CreateDirectory(contentRoot);

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(contentRoot, "appsettings.json"),
                """
                {
                  "ConnectionStrings": {
                    "DefaultConnection": "Data Source=smartops.db"
                  }
                }
                """);

            await using var factory = new TestWebApplicationFactory(contentRoot);
            using var scope = factory.Services.CreateScope();

            var dbPath = Path.Combine(contentRoot, "smartops.db");
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            Assert.True(File.Exists(dbPath));
            Assert.Equal(1, await dbContext.Customers.CountAsync());
            Assert.Equal(2, await dbContext.Transactions.CountAsync());
            Assert.Equal(1, await dbContext.OperationLogs.CountAsync());
        }
        finally
        {
            if (Directory.Exists(contentRoot))
            {
                Directory.Delete(contentRoot, recursive: true);
            }
        }
    }

    private sealed class TestWebApplicationFactory(string contentRoot) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseContentRoot(contentRoot);
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.Sources.Clear();
                config.SetBasePath(contentRoot);
                config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);
            });
        }
    }
}
```

- [ ] **Step 4: Run the targeted test and confirm it fails for the right reason**

Run:

```powershell
dotnet test .\SmartOps.Web.Tests\SmartOps.Web.Tests.csproj --filter DatabaseStartup_CreatesSqliteDatabaseFile -v minimal
```

Expected: FAIL because `AppDbContext` is not registered in `SmartOps.Web\Program.cs` yet, or because startup does not create `smartops.db` yet.

- [ ] **Step 5: Commit the failing test scaffold**

```powershell
git add .\SmartOps.Web.Tests\SmartOps.Web.Tests.csproj .\SmartOps.Web.Tests\DatabaseStartupTests.cs .\SmartOpsAgent.sln
git commit -m "test: cover sqlite startup database creation"
```

### Task 2: Register AppDbContext and create the database on startup

**Files:**
- Modify: `SmartOps.Web\Program.cs`
- Modify: `SmartOps.Web\appsettings.json`
- Test: `SmartOps.Web.Tests\DatabaseStartupTests.cs`

- [ ] **Step 1: Add the SQLite connection string to appsettings**

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=smartops.db"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```

- [ ] **Step 2: Update `Program.cs` to register the DbContext and call `EnsureCreated()`**

```csharp
using Microsoft.EntityFrameworkCore;
using SmartOps.Infrastructure.Data;
using SmartOps.Web.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' was not found.");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(connectionString));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    dbContext.Database.EnsureCreated();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

public partial class Program;
```

- [ ] **Step 3: Run the targeted test again and confirm it passes**

Run:

```powershell
dotnet test .\SmartOps.Web.Tests\SmartOps.Web.Tests.csproj --filter DatabaseStartup_CreatesSqliteDatabaseFile -v minimal
```

Expected: PASS, with the test confirming `smartops.db` is created and seeded rows are queryable through `AppDbContext`.

- [ ] **Step 4: Run the solution restore and build for regression coverage**

Run:

```powershell
dotnet restore .\SmartOpsAgent.sln
dotnet build .\SmartOpsAgent.sln -c Release --no-restore
```

Expected: restore succeeds, then build succeeds with 0 errors.

- [ ] **Step 5: Commit the startup wiring**

```powershell
git add .\SmartOps.Web\Program.cs .\SmartOps.Web\appsettings.json .\SmartOps.Web.Tests\DatabaseStartupTests.cs .\SmartOps.Web.Tests\SmartOps.Web.Tests.csproj .\SmartOpsAgent.sln
git commit -m "feat: register AppDbContext with sqlite startup initialization"
```
