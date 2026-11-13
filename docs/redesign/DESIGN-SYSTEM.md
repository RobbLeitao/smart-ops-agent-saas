# SmartOps

Consola de operaciones FinTech: transacciones fallidas en tiempo real y diagnóstico por IA. Oscura, neutra y precisa; el color aparece solo cuando algo significa algo.

Este sistema sale del rediseño del Dashboard (dirección C: estructura espaciada, paleta sobria). Lo consume la app Blazor Server del repo `smart-ops-agent-saas`, que usa **Bootstrap + CSS isolation de Blazor + Bootstrap Icons + Chart.js**. No hay Tailwind.

## Principios

1. **Neutro por defecto, color para el estado.** La interfaz es gris; un color solo aparece para decir algo: `danger` es una falla, `success` es aprobado o en vivo, `ai` es algo que produjo la IA. Si un color no comunica un estado, no va.
2. **Los datos se leen en columna.** IDs, tarjetas, montos y horas van en `mono` (estilo `data`), alineados: montos a la derecha.
3. **Una acción primaria por panel.** El botón `primary` (claro sobre oscuro) es escaso a propósito; el resto son secundarios con borde `border-strong`.
4. **La marca vive en el logo.** `brand-cyan` y `brand-lime` son solo del logo y la portada; nunca botones, links ni estados.
5. **Legible antes que denso.** Controles de al menos `control` (44px) de alto; texto chico nunca más claro que `text-muted`.

## Voz y contenido

- Español rioplatense con voseo: «Filtrá», «Abrí una fila», «Verificá la configuración».
- Botones con verbo: «Analizar», «Analizar la más reciente», «Guardar configuración». Nada de «OK» o «Aceptar».
- Errores accionables, sin culpar: qué pasó y qué hacer. «El proveedor de IA devolvió un error (404). Verificá la configuración en Integraciones.»
- Los motivos de falla del procesador se muestran tal como llegan (`Suspected fraud`, `Card declined`); no se traducen para no perder trazabilidad con los logs.
- Números: separador decimal coma, `$ 67,00`. Tarjetas siempre enmascaradas: `•••• 3723`.

## Color

| Rol | Token | Uso |
|---|---|---|
| Suelo | `bg` | página y header |
| Superficie | `surface` + `border` | tarjetas, paneles, tablas, drawer |
| Hundido | `surface-sunken` | encabezado de tabla, inputs |
| Elevado | `surface-raised` | pestaña activa, hover, pista de barras |
| Seleccionado | `surface-selected` | fila seleccionada; más clara que el hover |
| Texto | `text` / `text-muted` | principal / secundario |
| Falla | `danger` | punto de estado, badge, errores |
| Aprobado, en vivo | `success` | punto de estado |
| IA | `ai`, `ai-text`, `ai-surface`, `ai-border` | chip Listo, segmentos y marcas de la IA |

Los estados no se distinguen solo por tono: el chip Pendiente es un contorno gris, el chip Listo es relleno violeta con ✓.

**La app es solo oscura.** No hay tema claro ni modo claro: es una decisión de producto. La única pieza clara es el informe PDF (ver Impresión).

## Impresión (PDF)

El informe de diagnóstico es una hoja A4 (794×1123 px) en `print-paper`, con una franja superior `print-band` de 88px que lleva el logo a color (el logo no se lee sobre blanco). Texto en `print-ink`, etiquetas y pie en `print-muted`, separadores de 1px en `print-rule`, y el mensaje para el cliente en un recuadro `print-box`. Márgenes laterales de 56px. Estructura: título con transacción y motivo, grilla de datos, «Qué pasó», «Qué hacer», «Mensaje para el cliente» y pie con ID de diagnóstico, página, aviso de documento interno y quién exportó. Con `@page { margin: 0 }` se ocultan el encabezado y el pie que agrega el navegador. Los tokens `print-*` no se usan en la app.

## Tipografía

- **Display** (Space Grotesk): `metric` para valores de tarjetas, `title` para el título de página, `section` para títulos de panel.
- **Text** (Inter): `body` general, `body-strong` para botones, `small` para ayudas, `overline` (mayúsculas, tracking 0.08em) para etiquetas de métrica y encabezados de tabla.
- **Data** (JetBrains Mono): `data` y `data-sm` para todo lo que se compara en columna.

Las tres son de Google Fonts: `Space+Grotesk:wght@500;600;700`, `Inter:wght@400;500;600`, `JetBrains+Mono:wght@400;500`.

## Espaciado, radios y tamaños

Escala de 4px: `space-1` (4) a `space-12` (48). Página: margen `space-8`, bloques separados por `space-6`. Tarjetas de métrica: padding `space-5`; paneles: `space-6`.

Radios: `radius-md` para controles, `radius-lg` para tarjetas y paneles, `radius-pill` para chips y badges, `radius-sm` solo para los controles chicos del header. Sin sombras: la profundidad la dan `border` y `surface-raised`.

## Iconografía

Bootstrap Icons 1.11.3 (ya cargado en la app), estilo contorno, 16px en controles y 20px en el header, color `currentColor`. Los íconos acompañan una etiqueta; un botón solo-ícono lleva `aria-label` (ej. la campana: «Notificaciones, 495 sin leer»). Sin emojis en la UI.

## Logo

El logo vigente (`assets/Logos/smartops-logo.png`) es un PNG de 498×104 con fondo transparente, para fondo oscuro, a 28px de alto en el header. Limitaciones conocidas: es raster (se pixela en grande), no funciona como favicon y no tiene versión monocroma ni clara. Pendiente: redibujarlo en SVG con isotipo, versión monocroma y versión para fondo claro.

## Componentes

- **Header**: logo, pestañas con subrayado de 2px en `text` para la activa, indicador «En vivo» (punto `success`) + botón Pausar, campana con badge `danger`, usuario y rol en `small`.
- **MetricCard**: etiqueta `overline`, valor `metric`, nota `small`. Un punto de estado antes de la etiqueta solo si la métrica es un estado (fallidas, IA).
- **Button**: primario, secundario y link. Alto `control`.
- **Input**: de búsqueda con ícono, fondo `surface-sunken`, borde `border-strong`, etiqueta accesible siempre (visible u oculta).
- **StatusChip**: Pendiente (contorno) y Listo (IA).
- **DataTable**: encabezado `overline` en `surface-sunken`, filas separadas por `border-subtle`, datos en `mono`, acción por fila a la derecha. En pantallas chicas hace scroll horizontal dentro de su caja.

## Accesibilidad

- Texto 4.5:1 como mínimo sobre su fondo (3:1 desde 24px). `text-muted` es el gris más claro permitido para texto.
- Controles reales: `<button>`, `<a href>`, `<input>` con `<label>`. Nada clickeable sobre un `div`.
- Foco visible: contorno de 2px en `text` con 2px de separación.
- Área táctil mínima `control` (44px).

## Implementación en la app

1. Declarar los tokens como custom properties en `wwwroot/app.css` (`:root { --bg: #0b0d10; … }`), con los mismos nombres de este sistema.
2. Mapear Bootstrap a los tokens (`--bs-body-bg: var(--bg)`, `--bs-body-color: var(--text)`, `--bs-border-color: var(--border)`) para que tablas, inputs y modales dejen de salir en blanco.
3. Los `.razor.css` de cada componente usan solo `var(--token)`, nunca hex sueltos.
