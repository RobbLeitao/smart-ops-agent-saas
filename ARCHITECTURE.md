# SmartOps — Arquitectura (estado actual)

Mapeo descriptivo del código en `feature/ui-notifications-and-loader` (HEAD `a90d4a2`, 2026-11-11). Todo lo que sigue sale de leer el código fuente y la configuración. No se compiló ni se corrieron tests.

---

## 1. Stack y estructura general

### Versiones reales (según los `.csproj`)

| Componente | Versión | Dónde |
|---|---|---|
| Target framework | `net9.0` (todos los proyectos) | `*/*.csproj` |
| Blazor | Razor Components, render mode **InteractiveServer** (no WebAssembly) | `SmartOps.Web/Program.cs:57`, `Components/App.razor` |
| EF Core | `Microsoft.EntityFrameworkCore` 9.0.0, `.Sqlite` 9.0.0, `.Design` 9.0.0 | `SmartOps.Infrastructure/SmartOps.Infrastructure.csproj` |
| Identity | `Microsoft.AspNetCore.Identity.EntityFrameworkCore` 9.0.0 | Infrastructure y Web |
| Semantic Kernel | `Microsoft.SemanticKernel` 1.77.0, `Microsoft.SemanticKernel.Connectors.OpenAI` 1.77.0 | `SmartOps.Web/SmartOps.Web.csproj` |
| SignalR server | incluido en ASP.NET Core 9 (`AddSignalR()`) | `SmartOps.Web/Program.cs:181` |
| SignalR client | `Microsoft.AspNetCore.SignalR.Client` 8.0.0 (lo usan los componentes Razor del lado servidor) | `SmartOps.Web/SmartOps.Web.csproj` |
| SQLite ADO | `Microsoft.Data.Sqlite` 9.0.0 | `SmartOps.Web/SmartOps.Web.csproj` |
| Tests | xUnit 2.9.2, `xunit.runner.visualstudio` 2.8.2, `Microsoft.NET.Test.Sdk` 17.11.1, `Microsoft.AspNetCore.Mvc.Testing` 9.0.0, `Microsoft.EntityFrameworkCore.InMemory` **8.0.0**, `bunit` 2.8.4-preview | `SmartOps.Web.Tests/SmartOps.Web.Tests.csproj` |
| Front (CDN) | Bootstrap (local `wwwroot/lib`), Bootstrap Icons 1.11.3 (jsDelivr), Chart.js (jsDelivr, sin versión fijada) | `SmartOps.Web/Components/App.razor` |

### Proyectos de la solución (`SmartOpsAgent.sln`)

| Proyecto | Referencias | Responsabilidad |
|---|---|---|
| `SmartOps.Core` | — | Entidades POCO (`Entities/`), el contrato de eventos `ITransactionPublisher` (`SmartOps.Core/ITransactionPublisher.cs`, namespace `SmartOps.Core.Interfaces`) y `TransactionEventArgs` (`SmartOps.Core/TransactionEventArgs.cs`, namespace `SmartOps.Core.Events`). No tiene dependencias NuGet. |
| `SmartOps.Infrastructure` | Core | `AppDbContext` (Identity + entidades), `ApplicationUser`, las migraciones EF, el generador de transacciones `SimulatorTransactionService` (BackgroundService) y `NoopTransactionPublisher`. |
| `SmartOps.Web` | Infrastructure | El host ASP.NET Core: Blazor Server, SignalR hub, los servicios de IA (Semantic Kernel), el plugin `DataOpsPlugin`, los endpoints mínimos (login/logout/webhook/debug) y los assets estáticos. |
| `SmartOps.Web.Tests` | Web, Infrastructure | Tests xUnit, bUnit e integración con `WebApplicationFactory<Program>`. |

### Fuera de la solución (versionados en el repo)

- `Program.cs` (raíz): consola suelta que lee `SmartOps.Web/smartops.db` y lista `Transactions`.
- `.tmp_dbctx/`, `.tmp_query/`: consolas temporales (net9.0) contra el DbContext o SQLite.
- `tools/ApplyMigrationTool`, `tools/ReadDb`, `tools/SignalRTest`, `tools/diag-resolve.csx`: utilidades de diagnóstico.
- `SmartOps.Web/smartops-old.db`, `smartops-drive-relative.db-shm/-wal`: archivos SQLite versionados.
- `build-output.txt`, `build-after-migrations.txt`: salidas de build guardadas.
- `SmartOps.Web/Hubs` y `SmartOps.Web/Components/Auth` son **archivos vacíos** (0 bytes), no carpetas. El hub real vive en `SmartOps.Web/TransactionHub.cs`.
- `docs/`: `HITO3_CHANGELOG.md`, y en `docs/superpowers/{plans,specs}/` los planes y specs (SQLite startup, AIOpsService, consistencia de datos, rediseño del dashboard, drawer de transacciones).

### Bootstrapping (`SmartOps.Web/Program.cs`)

Orden de registro relevante:

- Kernel (L106) y servicios de IA (L168–200).
- DbContext SQLite. Se registra dos veces con la misma lambda: L67 y L265.
- Publisher de transacciones (L216–228).
- Identity con cookies (L231–253).
- Pipeline (L440–457) y endpoints mínimos (L305, L460, L495, L543, L551).

Arranque de la base (L312–399):

- `EnsureCreated()`.
- `EnsureColumnExists` (`ALTER TABLE` manual) para varias columnas.
- Backfill de `CardLast4`.
- Seed de demo: hasta 26 transacciones y 20 diagnósticos.
- Roles y usuarios de prueba.
- `CREATE TABLE IF NOT EXISTS Settings`.

`Program` es `partial` y expone helpers estáticos (`EnsureColumnExists`, `BackfillTransactionCards`, `SeedDemoTransactionsAndDiagnostics`) que usan los tests.

Usuarios sembrados (L357–358), con password `P@ssw0rd!`:

- `operador@smartops.com` → rol `Operador`
- `admin@smartops.com` → rol `Auditor`

---

## 2. Modelo de datos

### DbContext

`SmartOps.Infrastructure/Data/AppDbContext.cs` es un `IdentityDbContext<ApplicationUser>`. `ApplicationUser` (`SmartOps.Infrastructure/Data/ApplicationUser.cs`) es `IdentityUser` sin propiedades extra.

| DbSet | Entidad | Tabla |
|---|---|---|
| `Customers` | `SmartOps.Core/Entities/Customer.cs` | `Customers` |
| `Transactions` | `SmartOps.Core/Entities/Transaction.cs` | `Transactions` |
| `OperationLogs` | `SmartOps.Core/Entities/OperationLog.cs` | `OperationLogs` |
| `Diagnostics` | `SmartOps.Core/Entities/Diagnostic.cs` | `Diagnostics` |
| `DiagnosticFeedback` | `SmartOps.Core/Entities/DiagnosticFeedback.cs` | `DiagnosticFeedback` |
| `Notifications` | `SmartOps.Core/Entities/Notification.cs` | `Notifications` |
| (Identity) | `AspNetUsers`, `AspNetRoles`, `AspNetUserRoles`, `AspNetUserClaims`, `AspNetUserLogins`, `AspNetUserTokens`, `AspNetRoleClaims` | — |

Hay además una tabla **`Settings`** (`Key TEXT PK, Value TEXT`) que no está modelada en EF. Se crea con SQL crudo en `Program.cs:385`.

### Entidades

**Customer**: `Id:int`, `Name:string`, `Type:string`.

**Transaction**: `Id:int`, `CustomerId:int`, `Amount:decimal`, `Currency:string`, `Status:string`, `GatewayReference:string`, `CardLast4:string?`, `Provider:string`, `ErrorMessage:string?`, `OccurredAt:DateTime?`.
- `Status` no es un enum. El código usa los valores `"Succeeded"`, `"Failed"`, `"Approved"` y `"Success"`.
- `Id` se asigna manualmente (`Max(Id)+1`) en el simulador y en el seed.

**OperationLog**: `Id:int`, `Level`, `Component`, `Message`.

**Diagnostic**: `Id:Guid`, `TransactionId:int`, `CreatedAt:DateTime`, `Markdown:string`, `Summary:string?`, `SuggestedMessage:string?`, `CardLast4:string?`, `WasHelpful:bool?`, `IsUseful:bool?`, `UserId:string?`, `CreatedByUserId:string?`.
- `CreatedByUserId` tiene `HasMaxLength(450)`.

**DiagnosticFeedback**: `Id:Guid`, `DiagnosticId:Guid`, `UserId:string`, `IsUseful:bool`, `VotedAt:DateTime`.
- Índice único `(DiagnosticId, UserId)`.

**Notification**: `Id:int`, `TransactionId:int?`, `Title:string?`, `Message:string`, `Type:string` (`"Error"/"Warning"/"Info"`, default `"Info"`), `IsRead:bool`, `CreatedAt:DateTime`.

### Relaciones

En el modelo EF hay una sola FK configurada:
- `Transaction.CustomerId → Customer.Id`. Es `HasOne<Customer>().WithMany()`, sin propiedades de navegación (`AppDbContext.cs:27`).

Las demás asociaciones son lógicas: comparten clave pero no tienen FK ni navegación.
- `Diagnostic.TransactionId → Transaction.Id`
- `DiagnosticFeedback.DiagnosticId → Diagnostic.Id`
- `DiagnosticFeedback.UserId`, `Diagnostic.CreatedByUserId` y `Diagnostic.UserId` → `AspNetUsers.Id`
- `Notification.TransactionId → Transaction.Id`

### Seed (`HasData` en `OnModelCreating`)

- Customer 1 "Contoso Retail".
- Transaction 1 (Succeeded, `TXN_OK_1001`) y Transaction 2 (Failed, "Card declined", `TXN_ERR_502`).
- OperationLog 1.

El resto del seed se hace en runtime desde `Program.SeedDemoTransactionsAndDiagnostics`.

### Migraciones (orden cronológico)

Están en dos carpetas y dos namespaces, ambas con `[DbContext(typeof(AppDbContext))]`. El snapshot único está en `SmartOps.Infrastructure/Migrations/AppDbContextModelSnapshot.cs` e incluye las 6 entidades de dominio.

| # | Migración | Archivo | Cambio |
|---|---|---|---|
| 1 | `20260729192559_AddIdentityAndUserId` | `SmartOps.Infrastructure/Migrations/` | Crea las tablas de Identity (`AspNet*`) con sus índices. El comentario dice que las tablas de dominio y `Diagnostics.UserId` ya existían y no las toca. |
| 2 | `20260730125116_AddDiagnosticFeedbackPerUser` | `SmartOps.Infrastructure/Data/Migrations/` | Crea `DiagnosticFeedback` con el índice único `IX_DiagnosticFeedback_DiagnosticId_UserId`. |
| 3 | `20260730130440_AddDiagnosticCreatedByUserId` | `SmartOps.Infrastructure/Data/Migrations/` | `Up`/`Down` vacíos. La columna la agrega `Program.EnsureColumnExists`. |
| 4 | `20260814142457_AddNotifications` | `SmartOps.Infrastructure/Migrations/Notifications/` | Crea `Notifications`. |

No hay ninguna llamada a `Database.Migrate()` en el código. El arranque usa `EnsureCreated()` más `ALTER TABLE` manuales (`Program.cs:317–323`): `Transactions.CardLast4` y `Diagnostics.{CardLast4, WasHelpful, IsUseful, UserId, CreatedByUserId}`.

---

## 3. Arquitectura de la IA / Semantic Kernel

Conviven **dos caminos** de diagnóstico:

- **A**: la abstracción Adapter `ITransactionAnalyzer`.
- **B**: `DiagnosticOrchestratorService → IAIOpsService → Kernel`.

La UI usa el camino B.

### A. Adapter `ITransactionAnalyzer`

- Contrato: `SmartOps.Web/Services/ITransactionAnalyzer.cs`, `Task<string> AnalyzeAsync(Transaction tx)`. Devuelve Markdown.
- Implementaciones:
  - `DevFakeTransactionAnalyzer` (`Services/DevFakeTransactionAnalyzer.cs`). Es el **Simulador Local**: `Task.Delay(2000)` y heurística por palabras clave de `ErrorMessage` (expired, insufficient, fraud, declined, cvc). Devuelve Markdown con 3 secciones fijas (`## 🔍 Razón del fallo`, `## 🛠️ Acción recomendada para el operador`, `## 📨 Mensaje sugerido para el cliente`).
  - `AzureOpenAiDiagnosticEngine` (`Services/AzureOpenAiDiagnosticEngine.cs`). Es el **conector real**. Lee `Integrations.Provider/ApiKey/ModelId/Endpoint` de la tabla `Settings` en cada llamada y obtiene un `IChatCompletionService` desde `IChatCompletionServiceFactory`. Arma un `ChatHistory` (system + user prompt) y llama `GetChatMessageContentsAsync`. Los errores se mapean a `AiEngineException` (`Services/AiEngineException.cs`) con mensajes para el operador: 401/403, 429, timeout, respuesta vacía, falta de API key.
  - `IChatCompletionServiceFactory` → `SemanticKernelChatCompletionServiceFactory` (`Services/SemanticKernelChatCompletionServiceFactory.cs`). Construye un Kernel efímero con `AddAzureOpenAIChatCompletion` (si el provider contiene "azure" y hay endpoint) o `AddOpenAIChatCompletion`. El `modelId` por defecto es `gpt-4o`.
  - `ProviderSwitchingTransactionAnalyzer` (`Services/ProviderSwitchingTransactionAnalyzer.cs`). Es el **selector de runtime**: lee `Integrations.Provider` en cada `AnalyzeAsync`. Si está vacío o es `"Local"`, delega en `DevFakeTransactionAnalyzer`; con cualquier otro valor, en `AzureOpenAiDiagnosticEngine`.
  - `OpenAITransactionAnalyzer` (`Services/OpenAITransactionAnalyzer.cs`). Es un adapter fino que delega en `DiagnosticOrchestratorService.RunDiagnosticAsync(tx)`.
- Registro DI (`Program.cs:186–200`), según `OpenAI:ApiKey` de `appsettings.json`:
  - Si no está vacío → `ITransactionAnalyzer = OpenAITransactionAnalyzer`.
  - Si está vacío (valor actual en `appsettings.json`) → `ITransactionAnalyzer = ProviderSwitchingTransactionAnalyzer`, más `DevFakeTransactionAnalyzer`, `AzureOpenAiDiagnosticEngine` y la factory como singleton.
- Consumidores: ningún componente Razor ni servicio de producción resuelve `ITransactionAnalyzer`. Solo `AzureOpenAiDiagnosticEngine` se instancia, y únicamente en `SmartOps.Web.Tests/AzureOpenAiDiagnosticEngineTests.cs`.

### B. Camino usado por la UI

- `DiagnosticOrchestratorService` (`Services/DiagnosticOrchestratorService.cs`, scoped):
  - `RunDiagnosticAsync(Transaction)`: arma un prompt con los campos de la transacción, llama `IAIOpsService.ExecutePromptAsync` y persiste un `Diagnostic` (exige `CardLast4`; si falta, lanza).
  - `RunDiagnosticAsync(Transaction, string? createdByUserId)`: hace lo mismo y después asigna `CreatedByUserId` al último diagnóstico de esa transacción.
  - `RunDiagnosticAsync(Guid)`: prompt con `TransactionId` para function calling. Para persistir, busca la transacción mapeando `int Id → Guid` con MD5 (`CreateDeterministicGuidForInt`).
  - Ante una excepción devuelve un Markdown de fallback.
- `AIOpsService : IAIOpsService` (`Services/AIOpsService.cs`, scoped). Usa `FunctionChoiceBehavior.Auto()`. El orden de resolución es:
  1. Lee `Settings`. Si `Integrations.Provider == "OpenAI"` y hay `ApiKey`, construye un Kernel temporal con `AddOpenAIChatCompletion(key, model)` y llama `InvokePromptAsync`. Si falla, sigue al paso 2.
  2. Llama al `Kernel` singleton inyectado.
  3. Si la excepción contiene `"No service was found"`, entra en el **simulador local** del propio `AIOpsService`: espera `SimulatedThinkingDelay` (1800 ms) y aplica una heurística sobre la línea `ErrorMessage:` del prompt.
  4. Cualquier otra excepción se devuelve como string `"Error executing AI prompt: ..."`.
- Kernel singleton (`Program.cs:106–165`):
  - Agrega OpenAI solo si existe la variable de entorno `OPENAI_API_KEY` (modelo `OPENAI_MODEL_ID` o `gpt-4o`).
  - Registra `DataOpsPlugin` con `kb.Plugins.AddFromType<DataOpsPlugin>(null, sp)`.
  - Por reflexión intenta registrar `DataOps.GetFailedTransactions` buscando métodos `RegisterNativeFunction`/`RegisterFunction`/`AddFunction`.
- Configuración `AI:*` (`Program.cs:12–54`):
  - Lee `AI:Provider/ApiKey/Model/Endpoint` o las variables `AI_PROVIDER`, `OPENAI_API_KEY`, `OPENAI_MODEL_ID` y `AZURE_OPENAI_ENDPOINT`, y configura un `skBuilder` local. Ese builder no se construye ni se registra.
  - Si no hay key, registra `DevFakeKernelAdapter` (`Services/DevFakeKernelAdapter.cs`, implementa `IChatCompletionService` + `ITextGenerationService`) en el DI de la app.
- Otros fakes:
  - `DevFakeChatCompletionService` (`Services/DevFakeChatCompletionService.cs`): `IChatCompletionService` con una respuesta estática. No se registra en `Program.cs`.
- Plugin `DataOpsPlugin` (`SmartOps.Web/Plugins/DataOpsPlugin.cs`, scoped): `GetFailedTransactionsAsync()` devuelve las transacciones `Status=="Failed" && Provider=="Stripe"` formateadas.
  - El atributo `[KernelFunction]` que usa es uno propio, definido en `SmartOps.Web/Plugins/KernelFunctionAttribute.cs` (namespace `Microsoft.SemanticKernel.SkillDefinition`), no el de SK 1.x.

### Selección del proveedor en runtime (resumen)

| Origen | Clave(s) | Quién la lee |
|---|---|---|
| Tabla `Settings` (UI `/integrations`) | `Integrations.Provider` (`Local`/`OpenAI`/`AzureOpenAI`) y `Integrations.{OpenAI\|AzureOpenAI}.{ApiKey\|ModelId\|Endpoint}` (ver §6) | `AIOpsService`, vía `IntegrationSettingsStore` |
| Variable de entorno | `OPENAI_API_KEY`, `OPENAI_MODEL_ID` | Kernel singleton (`Program.cs:110`) |
| `appsettings.json` | `OpenAI:ApiKey` | Decide qué implementación de `ITransactionAnalyzer` se registra |
| `appsettings.json` / env | `AI:*`, `AI_PROVIDER`, `AZURE_OPENAI_ENDPOINT` | `skBuilder` (sin uso posterior) y la decisión de registrar `DevFakeKernelAdapter` |

---

## 4. Capas de presentación (Blazor)

- Host: `SmartOps.Web/Components/App.razor`. Carga Bootstrap, `app.css`, `SmartOps.Web.styles.css` (CSS isolation), Chart.js, `js/dashboardCharts.js`, `js/animations.js`, `blazor.web.js` y define `window.fetchLogin`.
- Router: `SmartOps.Web/Components/Routes.razor`. Usa `CascadingAuthenticationState` + `AuthorizeRouteView` con layout por defecto `Layout.MainLayout`. Si el usuario está autenticado pero sin rol, va a `RedirectToDashboard.razor` (`/`, forceLoad). Si es anónimo, va a `RedirectToLogin.razor` (`/login`, forceLoad).

### Rutas

| Ruta | Componente | Render mode | Autorización |
|---|---|---|---|
| `/` | `Components/Pages/Dashboard.razor` | InteractiveServer | `[Authorize]` |
| `/diagnostics-history` | `Components/Pages/DiagnosticsHistory.razor` (+ `.razor.css`) | InteractiveServer | `[Authorize]` (filtra los datos por rol) |
| `/integrations` | `Components/Pages/Integrations.razor` | InteractiveServer | `[Authorize(Roles="Auditor")]` |
| `/login` | `Components/Pages/Login.razor` (layout `Components/LoginLayout.razor`) | InteractiveServer | anónimo |
| `/Error` | `Components/Pages/Error.razor` | — | — |
| `/home`, `/counter`, `/weather` | `Home.razor`, `Counter.razor`, `Weather.razor` (plantilla Blazor) | — / InteractiveServer / StreamRendering | — |

Endpoints HTTP mínimos (`Program.cs`):

- `POST /account/login`: JSON `{Email, Password}`. Hace `SignInAsync` y setea la cookie `smartops_transition` (30 s). Lleva `IgnoreAntiforgeryToken`.
- `GET /account/logout`.
- `POST /api/webhooks/stripe/{transactionId?}`: parsea un GUID de la URL o de `data.object.metadata.transactionId` / `data.object.id` y llama `DiagnosticOrchestratorService.RunDiagnosticAsync(Guid)`. Devuelve `text/markdown`.
- `POST /api/debug/click` y `GET /debug/diag` (diagnóstico de DI/suscriptores).

### Comportamiento por página

- **Dashboard**:
  - Métricas vía `DashboardMetricsHelper.Build` (`Services/DashboardMetricsHelper.cs`): fallidas, aprobadas, % éxito, diagnósticos y % útil (desde `DiagnosticFeedback`).
  - Grilla de transacciones con toggle "mostrar todos" y modal de gráficos (`dashboardCharts.renderDoughnut/renderBar`).
  - Count-up con `smartOpsAnimations.countUp`.
  - Toasts por transacción fallida.
  - Análisis inline con `Orchestrator.RunDiagnosticAsync(tx, userId)`.
  - Botón "Detalle" → `TransactionDrawerService.Open(TransactionDto)`.
  - Expone pausa/reanudación del tiempo real con `DashboardHeaderActionsService`.
- **DiagnosticsHistory**:
  - Lista hasta 200 `Diagnostics`. El rol `Auditor`/`Admin` ve todos (con la columna de creador resuelta vía `UserManager`); el resto ve solo los que tienen `CreatedByUserId == userId`.
  - Filtros por últimos 4 dígitos de tarjeta, rango de fechas y "solo míos" (`Services/DiagnosticsHistoryFilterHelper.cs`).
  - Panel de detalle con copiar mensaje, exportar JSON (`window.smartOpsDownloadFile`) y PDF por vista de impresión (`window.smartOpsOpenPrintView`). Las dos funciones JS están definidas inline en el mismo `.razor` (L555, L571).
  - Votar útil/no útil hace upsert en `DiagnosticFeedback` por `(DiagnosticId, UserId)` y además escribe `Diagnostic.WasHelpful`.
- **Integrations**: enum local `ModelProvider { Local, OpenAI }`, API key con mostrar/ocultar, Model ID (default `gpt-4o`). Lee y escribe la tabla `Settings` (ver §6).
- **Login**: formulario con overlay de carga propio (CSS inline). Hace POST a `/account/login` mediante `window.fetchLogin` y navega a `/` con forceLoad.

### Componentes compartidos

| Componente | Archivo | Rol |
|---|---|---|
| `MainLayout` | `Components/Layout/MainLayout.razor` (+ `.css`) | Header con logo, tabs Dashboard/Historial e Integraciones (`AuthorizeView Roles="Auditor"`), `NotificationBell`, nombre de usuario, `DashboardHeaderToggle`, logout y `TransactionDetailDrawer` global. Contiene el overlay `#page-transition-overlay`, que se muestra solo si existe la cookie `smartops_transition` (la lee y la borra vía `IHttpContextAccessor`). |
| `NavMenu` | `Components/Layout/NavMenu.razor` (+ `.css`) | Menú lateral con `AuthorizeView` por rol. No se referencia desde ningún componente. |
| `NotificationBell` | `Components/Layout/NotificationBell.razor` (+ `.css`) | Campana con contador de no leídas y lista de las últimas 50 `Notifications`. Marca leída una o todas, y "Analizar" abre el drawer con la transacción. Se suscribe a SignalR. |
| `TransactionDetailDrawer` | `Components/Layout/TransactionDetailDrawer.razor` (+ `.css`) | Drawer lateral global. Se suscribe a `TransactionDrawerService` y carga el diagnóstico existente o ejecuta uno nuevo (`Orchestrator.RunDiagnosticAsync(tx, userId)`). Loader "Analizando con IA..." con un mínimo visible de 550 ms y fade-out de 220 ms. |
| `DashboardHeaderToggle` | `Components/Layout/DashboardHeaderToggle.razor` | Botón pausar/reanudar en el header. Escucha `DashboardHeaderActionsService.OnChange`. |
| `LoginLayout` | `Components/LoginLayout.razor` | Layout centrado del login. |
| `LoggingErrorBoundary` | `Components/LoggingErrorBoundary.razor` | `ErrorBoundary` que loguea y muestra la excepción (el texto menciona "Historial"). |
| `RedirectToLogin` / `RedirectToDashboard` | `Components/RedirectTo*.razor` | Redirecciones con forceLoad. |

Servicios de estado de UI:

- `TransactionDrawerService` (`Services/TransactionDrawerService.cs`, **singleton**): `Open/Close/Register/Unregister` con callbacks y un reintento de notificación (10 × 100 ms) si no hay suscriptores. Define `TransactionDto`.
- `DashboardHeaderActionsService` (`Services/DashboardHeaderActionsService.cs`, scoped por circuito).

JS:

- `wwwroot/js/dashboardCharts.js` (`window.dashboardCharts`).
- `wwwroot/js/animations.js` (`window.smartOpsAnimations`).
- `wwwroot/SmartOps.Web.lib.module.js`: inicializador JS de Blazor. En `afterWebStarted` oculta el overlay de transición a los 500 ms si se renderizó visible.

---

## 5. Tiempo real (SignalR)

Flujo:

```
SimulatorTransactionService (Timer, cada TransactionSettings:IntervalSeconds = 8s)
  ├─ inserta Transaction (70/30 aprox.: NextDouble() > 0.6 → Succeeded, si no Failed)
  ├─ si Failed → inserta Notification (Type="Error")
  └─ dispara ITransactionPublisher.OnTransactionCreated
        ↓
TransactionNotifier (singleton, se instancia a la fuerza en Program.cs:408)
  └─ IHubContext<TransactionHub>.Clients.All:
       SendAsync("ReceiveTransaction", int txId)
       SendAsync("NotificationCreated", int txId)
        ↓
/hubs/transactions  (TransactionHub, sin métodos)
```

- Hub: `SmartOps.Web/TransactionHub.cs`, namespace `SmartOps.Web.Hubs`, clase `TransactionHub : Hub` vacía. Mapeo: `app.MapHub<TransactionHub>("/hubs/transactions")` (`Program.cs:457`).
- Puente: `SmartOps.Web/Services/TransactionNotifier.cs`. Envía solo el `int Id` y emite los dos eventos para toda transacción, fallida o no.
- Publisher: `SmartOps.Infrastructure/SimulatorTransactionService.cs`. Se registra si `TransactionSettings:Provider == "Simulator"` (valor actual); si no, se usa `NoopTransactionPublisher`. `TransactionSettings` está en el mismo archivo.
- Un handler de consola `[DI-PROBE]` también se suscribe al publisher (`Program.cs:413–436`).
- Suscriptores de UI: los componentes crean un `HubConnection` server-side con `NavigationManager.ToAbsoluteUri("/hubs/transactions")` y `WithAutomaticReconnect()`.

| Componente | `ReceiveTransaction` | `NotificationCreated` |
|---|---|---|
| `Pages/Dashboard.razor` (L287–347) | Si no está pausado: `Db.Transactions.FindAsync(id)`, lo inserta en las listas, actualiza contadores y % éxito, y muestra un toast si tiene `ErrorMessage` | No hace nada (lo registra solo para evitar warnings) |
| `Layout/NotificationBell.razor` (L95–131) | Refresca las notificaciones | Refresca las notificaciones |

---

## 6. Persistencia y configuración de `/integrations`

- Modelo: no hay entidad EF. Se usa un key-value en la tabla `Settings (Key TEXT PRIMARY KEY, Value TEXT)`, creada con SQL crudo al arrancar (`Program.cs`).
- Acceso: siempre a través de `Services/IntegrationSettingsStore.cs` (ADO.NET sobre `AppDbContext.Database.GetDbConnection()`, parámetros `@k`/`@v`).
- Claves (configuración **por proveedor**: cambiar de proveedor nunca arrastra los valores del otro):

| Clave | Escrita por | Leída por |
|---|---|---|
| `Integrations.Provider` | `Integrations.razor` (`Local` / `OpenAI` / `AzureOpenAI`) | `Integrations.razor`, `AIOpsService` |
| `Integrations.OpenAI.ApiKey` / `.ModelId` / `.Endpoint` | `Integrations.razor` (la key en texto plano; Endpoint vacío = OpenAI oficial, o una URL OpenAI-compatible como Groq) | `Integrations.razor`, `AIOpsService` si el activo es `OpenAI` |
| `Integrations.AzureOpenAI.ApiKey` / `.ModelId` / `.Endpoint` | `Integrations.razor` (ModelId = deployment; Endpoint obligatorio) | `Integrations.razor`, `AIOpsService` si el activo es `AzureOpenAI` |

- Migración de las claves viejas (`Integrations.ApiKey`, `.ModelId`, `.Endpoint`, globales): `IntegrationSettingsStore.MigrateLegacy` corre al arrancar, en una transacción. Copia con `INSERT OR IGNORE` al proveedor activo (si es `Local`, a `AzureOpenAI` cuando el endpoint es `*.openai.azure.com`, si no a `OpenAI`), borra las claves viejas y loguea qué migró, sin valores. Es idempotente.
- «Probar conexión» (`Services/AiConnectionTester.cs`) usa los valores del formulario sin guardarlos.
- La API key nunca se loguea ni aparece en mensajes de error.

Resto de la configuración:

- `SmartOps.Web/appsettings.json`:
  - `ConnectionStrings:DefaultConnection` (ruta absoluta `C:\Users\rober\...\SmartOps.Web\smartops.db`). Si la ruta fuera relativa, `Program.cs` la anclaría al ContentRoot.
  - `OpenAI:{ApiKey:"", ModelId:"gpt-4o-mini"}`.
  - `TransactionSettings:{Provider:"Simulator", IntervalSeconds:8}`.
- `appsettings.Development.json`: solo logging.
- `SmartOps.Web/.env.example`: `OPENAI_API_KEY`, `OPENAI_MODEL_ID`.
- `Properties/launchSettings.json`: `http://localhost:5028` y `https://localhost:7210`.
- Auth: Identity + cookie, `LoginPath="/login"`, sin ReturnUrl. Si se niega el acceso, redirige a `/`.

---

## 7. Tests existentes

Hay un único proyecto: `SmartOps.Web.Tests` (xUnit), con 48 tests (`[Fact]`/`[Theory]`).

| Archivo | Tests | Qué cubre | Infra |
|---|---|---|---|
| `AuthTests.cs` + `AuthTestFixture.cs` | 14 | `POST /account/login` (200/401/400) y acceso a `/`, `/diagnostics-history`, `/integrations` por anónimo, Operador y Auditor (redirecciones) | `WebApplicationFactory` + SQLite real |
| `AzureOpenAiDiagnosticEngineTests.cs` | 4 | `AzureOpenAiDiagnosticEngine`: éxito, 401 → `AiEngineException`, timeout o error genérico, sin API key | SQLite in-memory + factory y chat services fake |
| `AIOpsServiceTests.cs` | 2 | `AIOpsService` devuelve la respuesta del modelo con `FunctionChoiceBehavior.Auto` y se resuelve en DI | fake `IChatCompletionService`, `WebApplicationFactory` |
| `AIOpsServiceFallbackTests.cs` | 1 | Fallback simulado de `AIOpsService` sin servicio en el Kernel | — |
| `AIOpsAutoFunctionInvokeTests.cs` | 1 | Settings de auto function choice | EF InMemory |
| `AIOpsAutoFunctionInvokeE2ETests.cs` | 1 | El Kernel invoca la función registrada con transactionId | EF InMemory |
| `AIOpsKernelPluginIntegrationTests.cs` | 1 | El Kernel tiene `DataOpsPlugin` accesible desde `AIOpsService` | `WebApplicationFactory` |
| `DataOpsPluginTests.cs` | 1 | `GetFailedTransactionsAsync` formatea los fallos de Stripe | — |
| `DiagnosticOrchestratorServiceTests.cs` | 2 | Markdown estructurado y persistencia de `CardLast4` | fake `IAIOpsService` |
| `DiagnosticsEndpointTests.cs` | 1 | `POST /api/webhooks/stripe` devuelve Markdown | `WebApplicationFactory` + fake `IAIOpsService` |
| `DiagnosticFeedbackPerUserTests.cs` | 3 | Votos por usuario, lectura del voto propio, índice único `(DiagnosticId, UserId)` | — |
| `DiagnosticFeedbackPersistenceTests.cs` | 2 | Crear, leer y actualizar `IsUseful` | — |
| `DiagnosticsHistoryRoleTests.cs` | 2 | El Operador ve solo los suyos; el Admin ve todos con el creador | — |
| `DiagnosticsHistoryFilterHelperTests.cs` | 3 | `DiagnosticsHistoryFilterHelper.Matches` | unit |
| `DashboardMetricsHelperTests.cs` | 3 | `DashboardMetricsHelper.Build`: toggle, conteos de gráficos, agrupación de razones | unit |
| `DashboardMetricsTests.cs` | 2 | % útil calculado y caso sin votos (N/A) | — |
| `DashboardUiTests.cs` | 1 | Click en "Detalle" abre `TransactionDetailDrawer` | bUnit |
| `DatabaseStartupTests.cs` | 2 | Creación del archivo SQLite (también en un subdirectorio configurado) | `WebApplicationFactory` |
| `IntegrationsSettingsPersistenceTests.cs` | 9 | Configuración por proveedor (`IntegrationSettingsStore`) y migración idempotente de las claves viejas | SQLite |
| `SignalRIntegrationTests.cs` | 1 | El hub entrega `ReceiveTransaction` cuando el publisher dispara | `WebApplicationFactory` + `TestTransactionPublisher` |

Helpers de test: `ControllableFakeAIOps.cs`, `FakeOrchestrator.cs` (clase `FakeAIOps`), `AuthTestFixture.cs`.

Sin tests directos para: `ProviderSwitchingTransactionAnalyzer`, `DevFakeTransactionAnalyzer`, `SimulatorTransactionService`, `TransactionNotifier`, `NotificationBell`, `Integrations.razor` (la UI) ni `TransactionDrawerService`.

---

## Notas abiertas

1. **¿El Kernel singleton ve a `DevFakeKernelAdapter`?** El adapter se registra en el `IServiceCollection` de la app (`Program.cs:51–53`), pero el Kernel se arma con un `Kernel.CreateBuilder()` independiente (`Program.cs:108`), que tiene su propio contenedor. Leyendo el código, cuando no hay `OPENAI_API_KEY` el Kernel no tendría servicio de chat y `AIOpsService` caería en su simulador interno ("No service was found"), no en `DevFakeKernelAdapter`. Habría que confirmarlo ejecutando.
2. **`[KernelFunction]` propio.** `DataOpsPlugin` usa el atributo definido en `Plugins/KernelFunctionAttribute.cs`, no `Microsoft.SemanticKernel.KernelFunctionAttribute`. No pude determinar sin ejecutar si `AddFromType<DataOpsPlugin>` expone la función (SK 1.x la descubre por su propio atributo). Existe un test que lo verifica (`AIOpsKernelPluginIntegrationTests`), pero no lo corrí.
3. **Qué migraciones están aplicadas en la base real.** Al arrancar se usa `EnsureCreated()`, que no registra `__EFMigrationsHistory`, y no hay ningún `Migrate()`. No puedo saber desde el código qué contiene `__EFMigrationsHistory` en `smartops.db`, ni si las migraciones se aplican con `dotnet ef` o con `tools/ApplyMigrationTool`.
4. **`Diagnostic.UserId`, `Diagnostic.IsUseful`, `Summary`, `SuggestedMessage`.** Están en la entidad y en el esquema, pero no encontré código de producción que los escriba. `WasHelpful` sí se escribe, desde `DiagnosticsHistory.razor:381`. No queda claro cuáles son legado y cuáles están reservados para algo futuro.
5. **README vs. código.** `README.md` describe ".NET 8 Blazor WebAssembly" y adapters llamados `DevFakeAdapter`/`OpenAiAdapter`. El código es net9.0 Blazor Interactive Server y las clases se llaman `DevFakeTransactionAnalyzer`/`AzureOpenAiDiagnosticEngine`. No sé si el README describe un plan o una versión anterior.
6. **Azure OpenAI desde la UI.** `Integrations.razor` solo ofrece `Local`/`OpenAI` y no escribe `Integrations.Endpoint`, mientras que la factory soporta `AzureOpenAI`. No encontré otro mecanismo para cargar el endpoint de Azure en `Settings`.
