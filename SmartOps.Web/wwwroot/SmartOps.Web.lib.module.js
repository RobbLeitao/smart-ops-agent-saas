// Blazor JS initializer (auto-discovered by naming convention: {AssemblyName}.lib.module.js).
// Drives the shared full-screen page-transition overlay (logo loader) declared in MainLayout.razor,
// so every navigation between tabs (Dashboard, Historial, Integraciones) - as well as the very
// first page load - shows the same branded transition instead of an abrupt content swap.
export function afterWebStarted(blazor) {
    const overlay = document.getElementById('page-transition-overlay');
    if (!overlay) {
        return;
    }

    let hideTimeoutId = null;

    function show() {
        if (hideTimeoutId) {
            window.clearTimeout(hideTimeoutId);
            hideTimeoutId = null;
        }
        overlay.classList.remove('page-transition-hidden');
    }

    function scheduleHide(delayMs) {
        if (hideTimeoutId) {
            window.clearTimeout(hideTimeoutId);
        }
        hideTimeoutId = window.setTimeout(() => {
            overlay.classList.add('page-transition-hidden');
            hideTimeoutId = null;
        }, delayMs);
    }

    // Initial page load (including full reloads, e.g. after Login): the overlay is visible by
    // default in the server-rendered HTML, so just schedule its fade-out shortly after paint.
    scheduleHide(500);

    // Subsequent SPA-style navigations between tabs use Blazor's enhanced navigation, which does
    // not trigger a full page reload; show/hide the overlay explicitly around each transition.
    blazor.addEventListener('enhancednavigationstart', show);
    blazor.addEventListener('enhancednavigationend', () => scheduleHide(250));
}
