// Blazor JS initializer (auto-discovered by naming convention: {AssemblyName}.lib.module.js).
// Drives the shared full-screen page-transition overlay (logo loader) declared in MainLayout.razor.
//
// IMPORTANT: this overlay must fire ONLY for the initial Login -> Dashboard transition (a hard,
// full-page browser navigation triggered by Login.razor's forceLoad NavigateTo). It must NEVER
// appear on internal tab navigation (Dashboard <-> Historial <-> Integraciones), which uses
// Blazor's enhanced navigation.
//
// Visibility is decided entirely server-side, per real HTTP request (both hard reloads and
// enhanced-navigation fetches are genuine requests to the server): MainLayout.razor renders the
// overlay already visible in the very first HTML byte only when a short-lived "smartops_transition"
// cookie (set by the /account/login endpoint on success, and cleared as soon as it's read) is
// present. Every other navigation renders it hidden by default - there is nothing for this script
// to "undo" between navigations, which is what caused two previous bugs:
//   1) Listening to 'enhancednavigationstart'/'enhancednavigationend' re-showed the overlay on
//      every tab click (it fires on ALL enhanced navigations, not just post-login).
//   2) A client-only sessionStorage flag combined with a default-hidden server render meant the
//      overlay was never part of the FIRST paint of the post-login page - so the Dashboard was
//      visible for a frame before the overlay (revealed by this script) covered it, then faded
//      out again, producing a double flash.
//
// This script's only job now is: if the overlay was rendered visible (i.e. the server decided
// this is the post-login load), schedule hiding it again after a short delay. If it was rendered
// hidden (any other navigation), do nothing.
export function afterWebStarted() {
    const overlay = document.getElementById('page-transition-overlay');
    if (!overlay || overlay.classList.contains('page-transition-hidden')) {
        return;
    }

    window.setTimeout(() => {
        overlay.classList.add('page-transition-hidden');
    }, 500);
}
