Título: Rediseño de Interacción — Transaction Detail Drawer
Fecha: 2026-09-21
Autor: Copilot (implementación asistida)

Resumen
-------
Reemplazar la vista extendida en línea de la grilla del Dashboard por un panel lateral (Drawer) deslizante para ver detalle y diagnóstico de transacciones. Enfoque recomendado: B — Drawer centralizado en MainLayout controlado por TransactionDrawerService (singleton). Esto facilita abrir el Drawer desde Dashboard, notificaciones, SignalR u otras áreas.

Alcance
-------
- Nueva UI: TransactionDetailDrawer.razor (Blazor Server component) con estilos basados en Tailwind / CSS similar a la paleta SmartOps (#1e2330).
- Simplificar Dashboard.razor: columnas condensadas (ID, Fecha, Monto, Estado, Acciones). Eliminar detalles inline por fila.
- Servicio: TransactionDrawerService con métodos Open(TransactionDto), Close(), y evento OnChange para suscripción.
- Integración: Dashboard y NotificationBell llamarán TransactionDrawerService.Open(...) con datos mínimos (id o DTO) y el Drawer mostrará detalle, telemetría, IA y acciones.
- Accesibilidad: cierre por ESC, botón X y click en backdrop. Animaciones suaves.
- Verificación: dotnet build sin errores; pruebas manuales de abrir/cerrar y flujo SignalR -> abrir Drawer.

Diseño de componentes
---------------------
1) TransactionDetailDrawer.razor
- Ubicación: SmartOps.Web/Components/Layout/TransactionDetailDrawer.razor
- Props: recibe datos via servicio; expone OnClose EventCallback
- Estructura:
  - header: TX #<id> + Status Badge (success/error) + botones (Analizar con IA, X)
  - body: tarjeta, timestamp, monto, canal, código error, telemetría (JSON render), sección IA (botón "Analizar con IA" y area Markdown para diagnóstico)
  - footer: acciones rápidas (Reintentar, Notificar)
- Estilos: contenedor fixed right, width 420px (responsive full-width < 600px), bg #1e2330, transición transform (translateX) y backdrop semitransparente.
- Eventos: ESC (document keydown), backdrop click, close button.

2) TransactionDrawerService
- Ubicación: SmartOps.Web/Services/TransactionDrawerService.cs
- API:
  - void Open(TransactionDto dto) or Open(int id) // service resolves details if only id
  - void Close()
  - event Action<TransactionDto?> OnChange // null -> closed
- Registro: AddSingleton<TransactionDrawerService>() en Program.cs
- Uso: MainLayout injecta servicio y renderiza <TransactionDetailDrawer /> at root so it overlays app. Other components inject service and call Open(...).

3) Dashboard.razor
- Simplificar tabla: mostrar columns ID, Fecha (local), Monto (formatted), Estado (Badge), Acciones (botón Detalle que llama DrawerService.Open(txDto)).
- Eliminar secciones inline o rows expandidas.

Integración con NotificationBell
--------------------------------
- Reemplazar NavigationManager.NavigateTo("/#tx{id}") por: await drawerService.Open(txDto) (if txDto available) or drawerService.Open(id).

IA & Markdown
-------------
- Drawer incluye sección que muestra diagnóstico (renderizar como Markdown seguro). "Analizar con IA" disparará endpoint existente o método que produzca markdown (si no existe, dejar placeholder que llame a server-side API). Mantener separación: Drawer solo muestra resultado.

Pruebas y verificación
----------------------
- dotnet build (SmartOps.sln or project) — confirmar sin errores.
- Probar: abrir Drawer desde Dashboard fila; cerrar con ESC, X y backdrop; abrir desde NotificationBell -> Drawer opens; Analizar con IA muestra markdown.

Tareas posteriores (no en este cambio)
-------------------------------------
- Deep-linking por URL (/#tx{id}) para soporte de Enfoque C.
- Automatizar tests de integración SignalR -> Drawer.

Notas de implementación
-----------------------
- Mantener cambios contenidos: crear solo nuevos archivos y editar Dashboard.razor y MainLayout.razor to render drawer. Evitar refactorizaciones no relacionadas.
- Commit: "spec: Transaction drawer design" + Co-authored-by trailer.

