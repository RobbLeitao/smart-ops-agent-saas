# Transaction Drawer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Añadir un Drawer lateral para ver detalle y diagnóstico de transacciones, controlado por un servicio singleton para apertura desde Dashboard, NotificationBell y otros.

**Architecture:** Un TransactionDrawerService singleton expone Open/Close y un evento OnChange. MainLayout renderiza TransactionDetailDrawer. Otros componentes (Dashboard, NotificationBell) llaman al servicio para abrirlo con un DTO o id.

**Tech Stack:** Blazor Server (Razor), C# (.NET), Entity Framework Core, Tailwind-like styles (inline CSS matching paleta), Git.

---

### Overview de archivos a crear/modificar

- Create: `SmartOps.Web/Components/Layout/TransactionDetailDrawer.razor`
- Create: `SmartOps.Web/Services/TransactionDrawerService.cs`
- Modify: `SmartOps.Web/Program.cs` - registrar singleton
- Modify: `SmartOps.Web/Components/Layout/MainLayout.razor` - renderizar el Drawer e inyectar servicio
- Modify: `SmartOps.Web/Components/Pages/Dashboard.razor` - simplificar tabla y llamar drawerService.Open(txDto)
- Modify: `SmartOps.Web/Components/Layout/NotificationBell.razor` - llamar drawerService.Open en Analyze
- (Optional) Create: `SmartOps.Web/Models/TransactionDto.cs` if no lightweight DTO exists


### Task 1: Crear TransactionDrawerService

**Files:**
- Create: `SmartOps.Web/Services/TransactionDrawerService.cs`

- [ ] **Step 1: Implementar el archivo**

```csharp
using System;
using System.Threading.Tasks;

namespace SmartOps.Web.Services
{
    public class TransactionDto
    {
        public int Id { get; set; }
        public string? CardLast4 { get; set; }
        public DateTime Timestamp { get; set; }
        public decimal Amount { get; set; }
        public string? Status { get; set; }
        public string? Channel { get; set; }
        public string? ErrorCode { get; set; }
    }

    public class TransactionDrawerService
    {
        private TransactionDto? _current;
        public event Action? OnChange;

        public TransactionDto? Current => _current;

        public void Open(TransactionDto dto)
        {
            _current = dto;
            OnChange?.Invoke();
        }

        public void Close()
        {
            _current = null;
            OnChange?.Invoke();
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add SmartOps.Web/Services/TransactionDrawerService.cs
git commit -m "feat(drawer): add TransactionDrawerService and TransactionDto" 
```

### Task 2: Registrar servicio en Program.cs

**Files:**
- Modify: `SmartOps.Web/Program.cs` (or equivalent startup)

- [ ] **Step 1: Edit Program.cs**

```csharp
// agregar al final de configuracion de servicios
builder.Services.AddSingleton<SmartOps.Web.Services.TransactionDrawerService>();
```

- [ ] **Step 2: Commit**

```bash
git add SmartOps.Web/Program.cs
git commit -m "chore: register TransactionDrawerService singleton" 
```

### Task 3: Crear TransactionDetailDrawer.razor

**Files:**
- Create: `SmartOps.Web/Components/Layout/TransactionDetailDrawer.razor`

- [ ] **Step 1: Implementar componente**

```razor
@using SmartOps.Web.Services
@inject TransactionDrawerService DrawerService
@implements IDisposable

<div class="drawer-wrapper" role="dialog" aria-hidden="@(_dto==null)" style="display:@(_dto==null?"none":"block")">
  <div class="backdrop" @onclick="Close"></div>
  <aside class="drawer" tabindex="-1">
    <header class="drawer-header">
      <div>
        <div class="tx-id">TX #@_dto?.Id</div>
        <div class="badge">@_dto?.Status</div>
      </div>
      <div>
        <button @onclick="Analyze">Analizar con IA</button>
        <button @onclick="Close">✕</button>
      </div>
    </header>
    <section class="drawer-body">
      <div><strong>Tarjeta</strong><div>@_dto?.CardLast4</div></div>
      <div><strong>Timestamp</strong><div>@_dto?.Timestamp</div></div>
      <div><strong>Monto</strong><div>@_dto?.Amount</div></div>
      <div><strong>Canal</strong><div>@_dto?.Channel</div></div>
      <div><strong>Código Error</strong><div>@_dto?.ErrorCode</div></div>

      <h4>Diagnóstico IA</h4>
      <div class="markdown">@(_diagnostic ?? "No hay diagnóstico aún.")</div>
    </section>

    <footer class="drawer-footer">
      <button @onclick="Retry">Reintentar</button>
      <button @onclick="Notify">Notificar</button>
    </footer>
  </aside>
</div>

@code {
    private TransactionDto? _dto;
    private string? _diagnostic;

    protected override void OnInitialized()
    {
        DrawerService.OnChange += OnDrawerChange;
        _dto = DrawerService.Current;
    }

    private void OnDrawerChange() { _dto = DrawerService.Current; StateHasChanged(); }

    private void Close() => DrawerService.Close();
    private Task Analyze() { _diagnostic = "Generando..."; /* placeholder */ return Task.CompletedTask; }
    private Task Retry() { /* placeholder */ return Task.CompletedTask; }
    private Task Notify() { /* placeholder */ return Task.CompletedTask; }

    public void Dispose() { DrawerService.OnChange -= OnDrawerChange; }
}
```

- [ ] **Step 2: Add styles** — add scoped CSS at top of file or shared stylesheet. Use width 420px, background #1e2330, transitions.

- [ ] **Step 3: Commit**

```bash
git add SmartOps.Web/Components/Layout/TransactionDetailDrawer.razor
git commit -m "feat(ui): add TransactionDetailDrawer component" 
```

### Task 4: Mostrar Drawer en MainLayout

**Files:**
- Modify: `SmartOps.Web/Components/Layout/MainLayout.razor` (render drawer once)

- [ ] **Step 1: Edit MainLayout.razor** — in markup body near bottom (after @Body) add:

```razor
@inject SmartOps.Web.Services.TransactionDrawerService DrawerService
<TransactionDetailDrawer />
```

- [ ] **Step 2: Commit**

```bash
git add SmartOps.Web/Components/Layout/MainLayout.razor
git commit -m "chore(layout): render TransactionDetailDrawer in MainLayout" 
```

### Task 5: Simplificar Dashboard.razor

**Files:**
- Modify: `SmartOps.Web/Components/Pages/Dashboard.razor`

- [ ] **Step 1: Replace grid markup** — show condensed table and add button to open drawer

```razor
@inject SmartOps.Web.Services.TransactionDrawerService DrawerService

<table class="table">
  <thead>
    <tr><th>ID</th><th>Fecha</th><th>Monto</th><th>Estado</th><th>Acciones</th></tr>
  </thead>
  <tbody>
    @foreach(var t in transactions)
    {
      <tr>
        <td>@t.Id</td>
        <td>@t.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm")</td>
        <td>@t.Amount.ToString("C")</td>
        <td><span class="badge">@t.Status</span></td>
        <td><button @onclick="() => DrawerService.Open(new TransactionDto { Id=t.Id, CardLast4=t.CardLast4, Timestamp=t.Timestamp, Amount=t.Amount, Status=t.Status, Channel=t.Channel, ErrorCode=t.ErrorCode })">Detalle</button></td>
      </tr>
    }
  </tbody>
</table>
```

- [ ] **Step 2: Commit**

```bash
git add SmartOps.Web/Components/Pages/Dashboard.razor
git commit -m "feat(dashboard): simplify table and integrate drawer open action" 
```

### Task 6: Integrar NotificationBell

**Files:**
- Modify: `SmartOps.Web/Components/Layout/NotificationBell.razor`

- [ ] **Step 1: Update Analyze() implementation** — call DrawerService.Open instead of NavigationManager.NavigateTo

```csharp
private async Task Analyze(int? txId, int? notificationId = null)
{
    if (notificationId.HasValue) await MarkAsRead(notificationId.Value);
    if (txId != null)
    {
        var tx = await Db.Transactions.FindAsync(txId.Value);
        if (tx != null)
        {
            DrawerService.Open(new TransactionDto { Id = tx.Id, CardLast4 = tx.CardLast4, Timestamp = tx.Timestamp, Amount = tx.Amount, Status = tx.Status, Channel = tx.Channel, ErrorCode = tx.ErrorCode });
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add SmartOps.Web/Components/Layout/NotificationBell.razor
git commit -m "feat(notification): open drawer from notifications analyze action" 
```

### Task 7: Build y solucionar errores

- [ ] **Step 1: Run build**

```bash
dotnet build SmartOps.Web/SmartOps.Web.csproj
```

Expected: BUILD SUCCESS. If errors, implement minimal fixes (missing usings, namespace mismatches), re-run until green.

- [ ] **Step 2: Commit any fix**

### Task 8: Verificación manual

- [ ] **Step 1:** Ejecutar la app (dotnet run) y abrir en navegador
- [ ] **Step 2:** Abrir Dashboard, clic en "Detalle" — Drawer debe abrir con datos
- [ ] **Step 3:** Cerrar con ESC, X, y backdrop
- [ ] **Step 4:** Abrir desde NotificationBell -> Drawer
- [ ] **Step 5:** Pulsar "Analizar con IA" (placeholder) y verificar que muestra contenido

---

Plan guardado: `docs/superpowers/plans/2026-09-21-transaction-drawer-plan.md`

¿Preferís ejecución Subagent-Driven (recomendado) o Inline Execution?