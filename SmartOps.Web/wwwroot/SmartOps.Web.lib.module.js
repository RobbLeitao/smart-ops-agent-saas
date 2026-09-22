// Blazor JS initializer (auto-discovered by naming convention: {AssemblyName}.lib.module.js).
// Drives the shared full-screen page-transition overlay (logo loader) declared in MainLayout.razor.
//
// IMPORTANT: this overlay must fire ONLY for the initial Login -> Dashboard transition (a hard,
// full-page browser navigation triggered by Login.razor's forceLoad NavigateTo). It must NOT
// re-trigger on internal tab navigation (Dashboard <-> Historial <-> Integraciones), which uses
// Blazor's enhanced navigation and does not reload the page/script. A previous version of this
// file also listened to 'enhancednavigationstart'/'enhancednavigationend' to show/hide the
// overlay on every enhanced navigation, which caused it to flash on every tab change - that
// logic has been intentionally removed.
export function afterWebStarted(blazor) {
    const overlay = document.getElementById('page-transition-overlay');
    if (!overlay) {
        return;
    }

    // The overlay is visible by default in the server-rendered HTML (covers the moment right
    // after a hard page load/reload, e.g. right after Login navigates here). Fade it out shortly
    // after first paint. This runs once per real page load - afterWebStarted is NOT re-invoked on
    // enhanced (in-page) navigations, so switching tabs never shows this overlay again.
    window.setTimeout(() => {
        overlay.classList.add('page-transition-hidden');
    }, 500);
}
