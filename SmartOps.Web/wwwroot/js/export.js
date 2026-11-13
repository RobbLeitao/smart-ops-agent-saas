// Exportaciones del Historial de diagnósticos (antes estaban inline en DiagnosticsHistory.razor).
(function () {
    'use strict';

    // Descarga un texto como archivo (la exportación JSON).
    window.smartOpsDownloadFile = function (filename, mime, content) {
        try {
            const blob = new Blob([content], { type: mime });
            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = filename;
            document.body.appendChild(a);
            a.click();
            a.remove();
            setTimeout(() => URL.revokeObjectURL(url), 5000);
        } catch (e) {
            console.error('Download failed', e);
        }
    };

    // Abre el informe PDF (HTML armado en el servidor por DiagnosticReportService) en una ventana nueva e
    // imprime cuando terminaron de cargar el logo y las fuentes. Devuelve false si el navegador bloqueó la
    // ventana, para que la página muestre el aviso. Devuelve en cuanto la ventana existe: print() bloquea
    // hasta que se cierra el diálogo y no debe retener la llamada de Blazor.
    window.smartOpsOpenPrintView = function (htmlContent) {
        const w = window.open('', '_blank');
        if (!w) return false;

        w.document.open();
        w.document.write(htmlContent);
        w.document.close();

        printWhenReady(w);
        return true;
    };

    const PRINT_WAIT_LIMIT_MS = 8000;

    function withTimeout(promise, ms) {
        return Promise.race([promise, new Promise(resolve => setTimeout(resolve, ms))]);
    }

    async function printWhenReady(w) {
        try {
            const doc = w.document;
            // 1) Hoja de estilos de las fuentes e imágenes (el logo): el evento load de la ventana.
            if (doc.readyState !== 'complete') {
                await withTimeout(new Promise(resolve => w.addEventListener('load', resolve, { once: true })), PRINT_WAIT_LIMIT_MS);
            }
            // 2) Las imágenes que todavía no terminaron (por las dudas).
            const pending = Array.from(doc.images)
                .filter(img => !img.complete)
                .map(img => new Promise(resolve => {
                    img.addEventListener('load', resolve, { once: true });
                    img.addEventListener('error', resolve, { once: true });
                }));
            await withTimeout(Promise.all(pending), PRINT_WAIT_LIMIT_MS);
            // 3) Las fuentes web (Space Grotesk, Inter, JetBrains Mono).
            if (doc.fonts && doc.fonts.ready) {
                await withTimeout(doc.fonts.ready, PRINT_WAIT_LIMIT_MS);
            }
        } catch (e) {
            console.warn('Print view: waiting for resources failed; printing anyway.', e);
        }

        if (w.closed) return;
        w.focus();
        w.print();
    }
})();
