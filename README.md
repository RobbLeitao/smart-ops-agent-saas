# SmartOps - Centro de Operaciones de IA 🚀

SmartOps es una plataforma enterprise orientada al monitoreo, diagnóstico y resolución inteligente de transacciones financieras fallidas en tiempo real. Utiliza una arquitectura desacoplada y moderna que combina el poder de **.NET 8 Blazor WebAssembly** y **Semantic Kernel**, permitiendo una transición fluida entre motores de simulación avanzada e Inteligencia Artificial real de OpenAI.

---

## 📋 1. El Problema de Negocio
En entornos de procesamiento de pagos de alta disponibilidad, las transacciones rechazadas (*Card declined*, *Insufficient funds*, *Suspected fraud*, etc.) representan una pérdida directa de facturación y un incremento drástico en los costos de soporte operativo.
* **Falta de Contexto:** Los códigos de error devuelvos por las pasarelas de pago suelen ser crípticos tanto para los operadores de la plataforma como para el cliente final.
* **Cuellos de Botella Operativos:** El equipo de soporte técnico debe analizar manualmente los logs para dar una respuesta o recomendación de mitigación, ralentizando la resolución de incidentes.
* **Fricción con el Usuario:** El cliente experimenta frustración al no comprender por qué falló su pago ni saber qué acción inmediata tomar para completarlo.

## 💡 2. La Solución SmartOps
SmartOps centraliza los fallos operacionales en un dashboard unificado y de pantalla completa (**Full Canvas**), permitiendo a los operadores diagnosticar cualquier transacción con un solo clic a través de un motor de IA orquestado por Semantic Kernel.
* **Diagnósticos Instantáneos:** Explicación técnica detallada del motivo real del fallo.
* **Mitigación Operativa:** Acciones sugeridas de inmediato para el operador de la mesa de ayuda.
* **Mensajería Empática:** Propuesta de texto lista para ser enviada al cliente final para guiarlo en el reintento exitoso.

---

## 🏗️ 3. Arquitectura y Cuestiones Técnicas

La aplicación está diseñada bajo el principio de **Clean Architecture** y **Desacoplamiento por Interfaces**, aislando completamente la interfaz de usuario (UI) de los proveedores externos de Inteligencia Artificial.

### Patrón Adapter y Estrategia de Migración Flexible
El sistema expone un contrato común mediante la interfaz `ITransactionAnalyzer`. Esto permite implementar dos estrategias de ejecución transparentes para el sistema:

1. **Modo Desarrollo (Simulador Hiperrealista - `DevFakeAdapter`):** 
   * **Costo $0 y 100% Local:** Pensado para entornos de desarrollo y testing donde no se dispone de una API Key de OpenAI.
   * **Análisis Dinámico:** Evalúa el tipo de error específico de la fila seleccionada y computa respuestas contextuales predefinidas en formato Markdown enriquecido.
   * **Simulación de Cómputo (UX Real):** Introduce un retardo asíncrono (`Task.Delay`) de entre 1.5 y 2.5 segundos, forzando el comportamiento real de los spinners de la UI y permitiendo validar la experiencia de usuario (UX) exacta de producción.

2. **Modo Producción (Conector Real - `OpenAiAdapter`):**
   * Invoca los modelos fundacionales de OpenAI (`gpt-4o-mini` / `gpt-4o`) mediante **Semantic Kernel**.
   * Automatiza la inyección de contexto de la transacción en el prompt del orquestador de manera dinámica.

### Conmutación Automática (Dependency Injection)
La transición entre entornos no requiere modificar una sola línea de código fuente. En el archivo `Program.cs`, el contenedor de inversión de control evalúa la configuración de forma dinámica:

```csharp
// Ejemplo conceptual de la inicialización en Program.cs
var apiKey = builder.Configuration["OpenAI:ApiKey"];

if (string.IsNullOrWhiteSpace(apiKey))
{
    // Si no hay clave, activa el simulador avanzado local sin costos
    builder.Services.AddScoped<ITransactionAnalyzer, DevFakeAdapter>();
}
else
{
    // Si se provee la clave, conecta de inmediato el motor real de OpenAI
    builder.Services.AddScoped<ITransactionAnalyzer, OpenAiAdapter>();
}
```

---

## 🛠️ 4. Stack Tecnológico

* **Frontend:** .NET 8.0 Blazor WebAssembly (Single Page Application fluida y de alto rendimiento).
* **Estilos & Layout:** Tailwind CSS (Diseño *Full Canvas* moderno, optimizado para grillas operativas enterprise sin barra lateral obstructiva).
* **Orquestación de IA:** Microsoft Semantic Kernel (Abstracción nativa de prompts y conectores de IA).
* **Formato de Salida:** Markdown (Renderizado enriquecido con soporte para emojis, listas y bloques de texto estructurados).

---

## ⚙️ 5. Configuración del Entorno Local

El proyecto viene preparado con un archivo de configuración segura para el desarrollo local.

1. Asegurate de contar con el archivo `appsettings.local.json` en la raíz del proyecto web (configurado en `.gitignore` para prevenir fugas de credenciales).
2. Para correr de forma **Gratuita / Local**, mantené la clave vacía:
   ```json
   {
     "OpenAI": {
       "ApiKey": "",
       "ModelId": "gpt-4o-mini"
     }
   }
   ```
3. Para migrar a **IA Real**, simplemente agregá tu clave privada:
   ```json
   {
     "OpenAI": {
       "ApiKey": "sk-Proj...",
       "ModelId": "gpt-4o-mini"
     }
   }
   ```
4. Ejecutá el comando de inicio estándar:
   ```bash
   dotnet run --project SmartOps.Web
   ```
