Hito 3 — Registro de plugins nativos y pruebas E2E

Resumen
- Se añadió un plugin de datos: SmartOps.Web.Plugins.DataOpsPlugin con el método GetFailedTransactionsAsync que devuelve transacciones fallidas de Stripe en formato legible.
- Se creó AIOpsService (SmartOps.Web.Services.AIOpsService) que invoca Semantic Kernel mediante InvokePromptAsync y configura PromptExecutionSettings con FunctionChoiceBehavior.Auto().

Registro de plugins nativos
- Kernel se registra como singleton usando una fábrica que recibe el IServiceProvider final (evita BuildServiceProvider durante ConfigureServices).
- Durante la construcción del Kernel, el sistema intenta registrar el plugin DataOpsPlugin con kb.Plugins.AddFromType(DataOpsPlugin) resolviendo dependencias desde el IServiceProvider.
- Como la API de registro de funciones de Semantic Kernel varia entre versiones, también se intenta (best-effort) registrar una función nativa en el Kernel mediante reflexión. Esto permite que, cuando la API de registro esté disponible, la función "DataOps.GetFailedTransactions" quede accesible para invocación autónoma.
- Además, DataOpsPlugin se registra en el contenedor DI (AddScoped) para uso directo por otros servicios.

Prueba end-to-end (E2E)
- Se añadió SmartOps.Web.Tests/AIOpsAutoFunctionInvokeTests.cs que valida el flujo:
  1. Construcción de un Kernel de prueba con un IChatCompletionService falso (RecordingChatCompletionService).
  2. Registro de AppDbContext en memoria y creación de la base de datos con datos seed.
  3. Registro explícito (vía reflexión en la prueba) de una función nativa que invoca DataOpsPlugin.GetFailedTransactionsAsync.
  4. Llamada a AIOpsService.ExecutePromptAsync("List failed transactions.") y verificación de que PromptExecutionSettings incluye FunctionChoiceBehavior.Auto y que la respuesta esperada se obtuvo.

Decisiones y notas
- Se evitó depender de BuildServiceProvider en ConfigureServices: Kernel se construye en una fábrica que usa el IServiceProvider final.
- La solución usa estrategias "best-effort" (AddFromType y reflexión) para mantener compatibilidad con múltiples versiones del paquete Semantic Kernel sin forzar cambios de dependencia.
- El shim temporal KernelFunctionAttribute fue usado inicialmente para compilar durante el desarrollo; si el paquete oficial provee la definición en tiempo de ejecución, el shim puede eliminarse.

Siguientes pasos sugeridos
- Reemplazar la lógica reflectiva por llamadas directas a la API oficial de Semantic Kernel cuando se actualice el paquete y se confirme la API de registro de funciones.
- Añadir más funciones/argumentos al plugin (filtros por fecha, paginación, etc.) según necesidades del agente.
- Considerar migraciones EF Core para aplicar cambios del modelo en entornos reales.
