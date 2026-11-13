# Rediseño SmartOps: referencia para implementar

Esta carpeta es la **fuente de verdad visual** del rediseño. Los `.html` son mockups estáticos: se abren con doble clic en el navegador y su código muestra colores, medidas, espaciados y textos exactos. Implementá cada pantalla en Blazor copiando su estructura y sus estilos, pero usando siempre los tokens (`var(--token)`), no los hex que aparecen inline en el mockup.

## Archivos

| Archivo | Qué es |
|---|---|
| `DESIGN-SYSTEM.md` | Principios, voz, uso de color, tipografía, componentes, accesibilidad. Leelo primero. |
| `tokens.json` | Todos los tokens con valor y uso. Los nombres son los de las custom properties (`--bg`, `--surface`…). |
| `logo.png` | Logo actual (fondo transparente, solo sobre fondo oscuro, 28px de alto en el header). |
| `01-dashboard.html` | Dashboard: header, 4 métricas, Motivos de falla, Cola de diagnóstico, tabla de transacciones fallidas. |
| `02-dashboard-cargando.html` | El mismo Dashboard en estado de carga (skeleton). Ver «Transición» abajo. |
| `03-drawer-detalle.html` | Drawer de Detalle en sus 4 estados: sin diagnóstico, analizando, listo, error. |
| `04-historial.html` | Historial: filtros, tabla y panel de detalle fijo (sticky) con el primero seleccionado. |
| `05-integraciones.html` | Integraciones: proveedor como opciones a la izquierda, credenciales a la derecha. |
| `06-login.html` | Login, mostrado con el estado de error. |
| `07-notificaciones.html` | Desplegable de notificaciones (badge 99+). |
| `08-informe-pdf.html` | Informe de diagnóstico A4 para imprimir: lo único claro de la app. |

## Decisiones que no se ven en un mockup estático

- **La app es solo oscura.** No hay modo claro. El único documento claro es el informe PDF (`print-*` tokens).
- **Color = estado.** `danger` solo fallas, `success` solo aprobado/en vivo, `ai` (violeta) solo lo que genera la IA. Todo lo demás es neutro.
- **Datos en monoespaciada** (`--font-mono`): IDs, tarjetas, montos, horas. Montos alineados a la derecha.
- **Controles de 44px de alto mínimo** (`--control`).
- **Tablas sin scroll vertical propio:** la página scrollea y la tabla pagina (20 filas por página). Scroll horizontal solo en pantallas angostas.
- **Historial:** al entrar se selecciona el primer diagnóstico; la fila seleccionada se distingue del hover (fondo más claro + barra de 3px a la izquierda + semibold). El panel es `position: sticky` con alto máximo de la pantalla y scroll interno.
- **Drawer:** «Analizar con IA» va en el cuerpo, no en el header. El voto «¿Te sirvió?» también está en el Drawer. Mientras analiza, el botón se deshabilita y el esqueleto es violeta (es la IA).
- **Notificaciones:** el badge muestra «99+» si hay más de 99; el número exacto va dentro del desplegable.
- **PDF:** con `@page { margin: 0 }` se ocultan el encabezado y el pie que agrega el navegador.

## Transición Login → Dashboard

Reemplaza el overlay con logo animado a pantalla completa.

1. Ingresar: el botón pasa a «Ingresando…» deshabilitado.
2. Login exitoso: la tarjeta hace fade out (opacidad 1→0 y sube 8px, 200 ms, ease-in).
3. El Dashboard aparece en skeleton (`02-dashboard-cargando.html`) con fade in (opacidad 0→1, 250 ms, ease-out). Header, pestañas, título y usuario ya son reales; «En vivo» muestra «Conectando…» hasta que conecta SignalR.
4. Mientras cargan los datos, los bloques grises tienen un brillo suave que los recorre (1.4 s, en bucle).
5. Llegan los datos: cada bloque hace crossfade a su contenido (180 ms) y recién ahí arranca el count-up de las métricas.

Reglas: si los datos llegan en menos de 200 ms no se muestra el skeleton; si se muestra, dura al menos 300 ms; con `prefers-reduced-motion` no hay brillo ni desplazamiento, solo fundidos cortos. El esqueleto de carga es gris neutro (`--surface-raised`), no violeta.

## Contenido de ejemplo

Los números y filas de los mockups son de ejemplo. El texto del diagnóstico («Qué pasó», «Qué hacer», «Mensaje para el cliente») lo genera la IA; el tono con voseo del mensaje al cliente requiere ajustar el prompt del motor (bloque aparte).
