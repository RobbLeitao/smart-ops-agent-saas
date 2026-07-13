# Hito 14: Consistencia de Datos, Scroll de Grillas y Estado Vacío Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make card last4 deterministic end-to-end from Dashboard to DIAGNOSTIC, enforce a fixed internal scroll height on the transaction grids, and show a clear empty state when the diagnostics filter has no matches.

**Architecture:** Add `CardLast4` to the transaction model and seed/demo data so the dashboard always renders stable card values. Update the diagnostic orchestrator to persist the exact transaction card into `Diagnostic.CardLast4` instead of generating a random fallback. Then align the two Blazor pages with the same scroll container pattern and a dedicated empty-state branch for filtered no-results cases.

**Tech Stack:** .NET 8 Blazor, Entity Framework Core, Tailwind/utility CSS, xUnit/Bunit.

---

### Task 1: Make transaction card data deterministic

**Files:**
- Modify: `SmartOps.Core\Entities\Transaction.cs`
- Modify: `SmartOps.Infrastructure\Data\AppDbContext.cs`
- Modify: `SmartOps.Web\Components\Pages\Dashboard.razor`
- Modify: `SmartOps.Web.Tests\DashboardUiTests.cs`

- [ ] **Step 1: Add the failing test**

```csharp
[Fact]
public async Task DashboardSeedsTransactionsWithCardLast4()
{
    using var ctx = new TestContext();
    // assert rendered transactions expose stable card values
}
```

- [ ] **Step 2: Run the focused test**

Run: `dotnet test SmartOps.Web.Tests\SmartOps.Web.Tests.csproj --filter DashboardUiTests -v minimal`
Expected: FAIL until `CardLast4` exists and is seeded.

- [ ] **Step 3: Implement the model and seed values**

```csharp
public string CardLast4 { get; set; } = string.Empty;
```

- [ ] **Step 4: Verify the tests pass**

Run: `dotnet test SmartOps.Web.Tests\SmartOps.Web.Tests.csproj --filter DashboardUiTests -v minimal`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add SmartOps.Core/Entities/Transaction.cs SmartOps.Infrastructure/Data/AppDbContext.cs SmartOps.Web/Components/Pages/Dashboard.razor SmartOps.Web.Tests/DashboardUiTests.cs
git commit -m "feat: seed deterministic transaction cards"
```

### Task 2: Persist the same card into diagnostics

**Files:**
- Modify: `SmartOps.Web\Services\DiagnosticOrchestratorService.cs`
- Modify: `SmartOps.Web.Tests\DiagnosticOrchestratorServiceTests.cs`
- Modify: `SmartOps.Web.Tests\DiagnosticsEndpointTests.cs`

- [ ] **Step 1: Add the failing persistence assertion**

```csharp
Assert.Equal("4242", diagnostic.CardLast4);
```

- [ ] **Step 2: Run the targeted tests**

Run: `dotnet test SmartOps.Web.Tests\SmartOps.Web.Tests.csproj --filter DiagnosticOrchestratorServiceTests -v minimal`
Expected: FAIL until the orchestrator reuses `Transaction.CardLast4`.

- [ ] **Step 3: Remove random card generation**

```csharp
var last4 = tx.CardLast4;
if (string.IsNullOrWhiteSpace(last4))
{
    throw new InvalidOperationException("Transaction.CardLast4 is required.");
}
```

- [ ] **Step 4: Verify the tests pass**

Run: `dotnet test SmartOps.Web.Tests\SmartOps.Web.Tests.csproj --filter DiagnosticOrchestratorServiceTests -v minimal`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add SmartOps.Web/Services/DiagnosticOrchestratorService.cs SmartOps.Web.Tests/DiagnosticOrchestratorServiceTests.cs SmartOps.Web.Tests/DiagnosticsEndpointTests.cs
git commit -m "feat: persist transaction card in diagnostics"
```

### Task 3: Normalize grid height, empty state, and modal styling

**Files:**
- Modify: `SmartOps.Web\Components\Pages\DiagnosticsHistory.razor`
- Modify: `SmartOps.Web\Components\Pages\Dashboard.razor`
- Modify: `SmartOps.Web\wwwroot\app.css`

- [ ] **Step 1: Add the UI assertions**

```csharp
// verify the empty-state message appears when filterLast4 has no matches
```

- [ ] **Step 2: Run the UI tests**

Run: `dotnet test SmartOps.Web.Tests\SmartOps.Web.Tests.csproj --filter DashboardUiTests -v minimal`
Expected: FAIL until the scroll and empty-state branches exist.

- [ ] **Step 3: Add the fixed-height scroll containers and empty state**

```razor
<div class="max-h-[400px] overflow-y-auto">
    ...
</div>
```

- [ ] **Step 4: Verify the UI tests pass**

Run: `dotnet test SmartOps.Web.Tests\SmartOps.Web.Tests.csproj --filter DashboardUiTests -v minimal`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add SmartOps.Web/Components/Pages/DiagnosticsHistory.razor SmartOps.Web/Components/Pages/Dashboard.razor SmartOps.Web/wwwroot/app.css
git commit -m "feat: align diagnostics grid and modal styling"
```
