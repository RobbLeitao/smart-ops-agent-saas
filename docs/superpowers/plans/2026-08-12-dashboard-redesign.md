# Dashboard SaaS/Cyberpunk Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Redesign `Dashboard.razor` into a modern SaaS/Cyberpunk control console with four KPI cards, two live charts, and a restyled table, all reacting instantly to the admin toggle.

**Architecture:** Keep the existing dashboard data pipeline in `DashboardMetricsHelper` and extend it only where the view needs chart-ready series. Add a small JSInterop layer dedicated to Chart.js rendering so the Blazor page stays responsible for data and state while JavaScript only paints and updates charts. Preserve existing filtering and analysis behavior, but restyle the entire surface so cards, charts, and table share one dark visual system.

**Tech Stack:** Blazor Server (`Dashboard.razor`), EF Core, xUnit/bUnit, Chart.js via JSInterop, existing `DashboardMetricsHelper`.

---

### Task 1: Extend dashboard metrics for chart rendering

**Files:**
- Modify: `SmartOps.Web/Services/DashboardMetricsHelper.cs`
- Modify: `SmartOps.Web.Tests/DashboardMetricsHelperTests.cs`

- [ ] **Step 1: Add chart-ready aggregates to the metrics model**

```csharp
public sealed record DashboardMetrics(
    int FailedTransactionsCount,
    int DiagnosticsCount,
    int ApprovedTransactionsCount,
    int SuccessRatePercent,
    int UsefulPercent,
    IReadOnlyList<(string Reason, int Count)> FailureReasons,
    int FailedChartCount,
    int ApprovedChartCount);
```

- [ ] **Step 2: Return the new counts from `Build` without changing existing totals**

```csharp
return new DashboardMetrics(
    failedTx,
    diags.Count(),
    approvedTx,
    successRate,
    usefulPercent,
    reasons,
    failedTx,
    approvedTx);
```

- [ ] **Step 3: Add a test that verifies chart counts match the approved/failed split**

```csharp
[Fact]
public void ChartCounts_MatchApprovedAndFailedTransactions()
{
    var txs = new[]
    {
        new Transaction { Id = 1, Status = "Failed", ErrorMessage = "Card declined" },
        new Transaction { Id = 2, Status = "Approved", ErrorMessage = null },
        new Transaction { Id = 3, Status = "Success", ErrorMessage = null }
    };

    var metrics = DashboardMetricsHelper.Build(txs, Array.Empty<DiagnosticFeedback>(), Array.Empty<Diagnostic>(), false, null);

    Assert.Equal(1, metrics.FailedChartCount);
    Assert.Equal(2, metrics.ApprovedChartCount);
}
```

- [ ] **Step 4: Run the focused helper tests**

Run: `dotnet test SmartOps.Web.Tests\SmartOps.Web.Tests.csproj --filter DashboardMetricsHelperTests -v minimal`
Expected: PASS

- [ ] **Step 5: Commit the helper update**

```bash
git add SmartOps.Web/Services/DashboardMetricsHelper.cs SmartOps.Web.Tests/DashboardMetricsHelperTests.cs
git commit -m "feat: extend dashboard metrics for charts"
```

### Task 2: Add Chart.js interop for dashboard charts

**Files:**
- Create: `SmartOps.Web/wwwroot/js/dashboardCharts.js`
- Modify: `SmartOps.Web/Components/App.razor`
- Modify: `SmartOps.Web/SmartOps.Web.csproj` if a package becomes necessary; prefer no package change

- [ ] **Step 1: Create a focused Chart.js helper that can draw and replace two charts**

```javascript
window.dashboardCharts = (function () {
  const charts = {};

  function destroy(name) {
    if (charts[name]) {
      charts[name].destroy();
      delete charts[name];
    }
  }

  function doughnut(canvasId, labels, values) {
    const canvas = document.getElementById(canvasId);
    if (!canvas) return;
    destroy(canvasId);
    charts[canvasId] = new Chart(canvas, {
      type: 'doughnut',
      data: {
        labels,
        datasets: [{
          data: values,
          backgroundColor: ['#ef4444', '#f97316', '#f59e0b', '#8b5cf6', '#14b8a6']
        }]
      },
      options: { responsive: true, maintainAspectRatio: false, plugins: { legend: { labels: { color: '#dbe4f0' } } } }
    });
  }

  function bar(canvasId, labels, approved, failed) {
    const canvas = document.getElementById(canvasId);
    if (!canvas) return;
    destroy(canvasId);
    charts[canvasId] = new Chart(canvas, {
      type: 'bar',
      data: {
        labels,
        datasets: [
          { label: 'Aprobadas', data: approved, backgroundColor: '#22c55e' },
          { label: 'Falladas', data: failed, backgroundColor: '#ef4444' }
        ]
      },
      options: { responsive: true, maintainAspectRatio: false, plugins: { legend: { labels: { color: '#dbe4f0' } } } }
    });
  }

  return { doughnut, bar, destroy };
})();
```

- [ ] **Step 2: Load Chart.js and the dashboard helper in the app shell**

```razor
<script src="https://cdn.jsdelivr.net/npm/chart.js"></script>
<script src="js/dashboardCharts.js"></script>
<script src="_framework/blazor.web.js"></script>
```

- [ ] **Step 3: Verify the app shell still renders and the script order is valid**

Run: `dotnet build SmartOps.Web\SmartOps.Web.csproj -v minimal`
Expected: PASS

- [ ] **Step 4: Commit the interop helper**

```bash
git add SmartOps.Web/wwwroot/js/dashboardCharts.js SmartOps.Web/Components/App.razor
git commit -m "feat: add dashboard chart interop"
```

### Task 3: Rebuild the dashboard layout and live chart binding

**Files:**
- Modify: `SmartOps.Web/Components/Pages/Dashboard.razor`

- [ ] **Step 1: Replace the current KPI row with a 4-column dark grid**

```razor
<div class="dashboard-shell">
  <div class="dashboard-kpi-grid">
    <article class="dashboard-card">
      <span class="dashboard-label">Tasa de Aprobación</span>
      <strong class="dashboard-value">@SuccessRateDisplay</strong>
    </article>
    <article class="dashboard-card">
      <span class="dashboard-label">Transacciones Fallidas</span>
      <strong class="dashboard-value">@FailedTransactionsCount</strong>
    </article>
    <article class="dashboard-card">
      <span class="dashboard-label">Diagnósticos Realizados por IA</span>
      <strong class="dashboard-value">@DiagnosticsCount</strong>
    </article>
    <article class="dashboard-card">
      <span class="dashboard-label">Efectividad de IA</span>
      <strong class="dashboard-value">@UsefulPercentDisplay</strong>
    </article>
  </div>
</div>
```

- [ ] **Step 2: Replace the badge list with a doughnut chart card and add the approved-vs-failed chart card**

```razor
<div class="dashboard-chart-grid">
  <article class="dashboard-card chart-card">
    <h3>Motivos de Falla Más Frecuentes</h3>
    <div class="chart-host"><canvas id="failureReasonsChart"></canvas></div>
  </article>
  <article class="dashboard-card chart-card">
    <h3>Transacciones Aprobadas vs. Falladas</h3>
    <div class="chart-host"><canvas id="transactionsSplitChart"></canvas></div>
  </article>
</div>
```

- [ ] **Step 3: Restyle the table into the same dark system**

```razor
<div class="dashboard-table-card">
  <div class="table-responsive dashboard-table-wrap">
    <table class="table table-borderless align-middle dashboard-table">
      <thead>
        <tr>
          <th>Id</th>
          <th>Amount</th>
          <th>Currency</th>
          <th>Tarjeta</th>
          <th>Error</th>
          <th>OccurredAt</th>
          <th class="text-end">Actions</th>
        </tr>
      </thead>
      <tbody>
        @foreach (var t in FilteredTransactions)
        {
          <tr>
            <td>@t.Id</td>
            <td>@t.Amount</td>
            <td>@t.Currency</td>
            <td>@FormatCard(t.CardLast4)</td>
            <td>@t.Status</td>
            <td>@t.OccurredAt?.ToString("g")</td>
            <td class="text-end">
              <button class="btn btn-ia" @onclick="() => AnalyzeAsync(t.Id)">Analizar con IA</button>
            </td>
          </tr>
        }
      </tbody>
    </table>
  </div>
</div>
```

- [ ] **Step 4: Bind the admin toggle so metrics and charts recalculate together**

```csharp
private async Task ReloadDashboardAsync()
{
    await LoadMetricsAsync();
    await LoadGridAsync();
    await RefreshChartsAsync();
}
```

- [ ] **Step 5: Add chart refresh after first render and after every toggle/filter update**

```csharp
protected override async Task OnAfterRenderAsync(bool firstRender)
{
    if (firstRender)
    {
        await RefreshChartsAsync();
    }
}
```

- [ ] **Step 6: Add the CSS classes inline or in the existing app stylesheet so the dashboard uses the requested #1e2330 surface and border colors**

```css
.dashboard-shell { background: #1e2330; min-height: 100vh; }
.dashboard-card { background: #171c26; border: 1px solid #2d3446; border-radius: 0.5rem; }
.dashboard-kpi-grid, .dashboard-chart-grid { display: grid; gap: 1rem; }
.dashboard-kpi-grid { grid-template-columns: repeat(4, minmax(0, 1fr)); }
.dashboard-chart-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); }
.chart-host { min-height: 280px; position: relative; }
```

- [ ] **Step 7: Run the bUnit dashboard test and verify the component still renders with the new structure**

Run: `dotnet test SmartOps.Web.Tests\SmartOps.Web.Tests.csproj --filter DashboardUiTests -v minimal`
Expected: PASS

- [ ] **Step 8: Commit the dashboard UI rewrite**

```bash
git add SmartOps.Web/Components/Pages/Dashboard.razor
git commit -m "feat: redesign dashboard with charts and kpis"
```

### Task 4: Add regression tests for the new live dashboard behavior

**Files:**
- Modify: `SmartOps.Web.Tests/DashboardUiTests.cs`
- Create or modify: `SmartOps.Web.Tests/DashboardChartStateTests.cs`

- [ ] **Step 1: Add a test that the admin toggle changes the rendered record set**

```csharp
[Fact]
public async Task AdminToggle_RecomputesDashboardView()
{
    using var ctx = new TestContext();
    // seed admin auth + in-memory db with 2 failed and 1 approved transaction
    var comp = ctx.Render<SmartOps.Web.Components.Pages.Dashboard>();
    var toggle = comp.Find("#showAllRecordsDashboard");
    await toggle.ChangeAsync(true);
    Assert.Contains("Transacciones Fallidas", comp.Markup);
    Assert.Contains("Motivos de Falla Más Frecuentes", comp.Markup);
}
```

- [ ] **Step 2: Add a test that the KPI totals remain consistent after toggle**

```csharp
[Fact]
public void MetricsHelper_SeparatesApprovedFailedAndUsefulTotals()
{
    var txs = new[]
    {
        new Transaction { Id = 1, Status = "Failed", ErrorMessage = "Card declined" },
        new Transaction { Id = 2, Status = "Approved", ErrorMessage = null },
        new Transaction { Id = 3, Status = "Success", ErrorMessage = null }
    };
    var feedback = new[]
    {
        new DiagnosticFeedback { IsUseful = true },
        new DiagnosticFeedback { IsUseful = false }
    };
    var metrics = DashboardMetricsHelper.Build(txs, feedback, Array.Empty<Diagnostic>(), false, null);
    Assert.Equal(1, metrics.FailedTransactionsCount);
    Assert.Equal(2, metrics.ApprovedTransactionsCount);
    Assert.Equal(50, metrics.UsefulPercent);
    Assert.Equal(1, metrics.FailedChartCount);
    Assert.Equal(2, metrics.ApprovedChartCount);
}
```

- [ ] **Step 3: Run the focused dashboard UI tests**

Run: `dotnet test SmartOps.Web.Tests\SmartOps.Web.Tests.csproj --filter "DashboardUiTests|DashboardMetricsTests|DashboardMetricsHelperTests" -v minimal`
Expected: PASS

- [ ] **Step 4: Commit the regression coverage**

```bash
git add SmartOps.Web.Tests/DashboardUiTests.cs SmartOps.Web.Tests/DashboardMetricsTests.cs SmartOps.Web.Tests/DashboardMetricsHelperTests.cs
git commit -m "test: cover redesigned dashboard metrics and ui"
```

### Task 5: Full verification

**Files:**
- None

- [ ] **Step 1: Run the solution build**

Run: `dotnet build -v minimal`
Expected: PASS

- [ ] **Step 2: Run the relevant unit test suite**

Run: `dotnet test -v minimal`
Expected: PASS

- [ ] **Step 3: Confirm the dashboard visually in the browser if needed**

Run the web app and confirm:
```text
1. Four KPI cards sit in one row.
2. The doughnut and bar charts render in the middle section.
3. Toggling "Ver todos los registros" updates the cards and charts immediately.
4. The table matches the new dark SaaS/Cyberpunk styling.
```
