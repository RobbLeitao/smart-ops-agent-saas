// Blazor JS initializer (auto-discovered by naming convention: {AssemblyName}.lib.module.js).
// Drives the shared full-screen page-transition overlay (logo loader) declared in MainLayout.razor.
//
// IMPORTANT: this overlay must fire ONLY for the initial Login -> Dashboard transition (a hard,
// full-page browser navigation triggered by Login.razor's forceLoad NavigateTo). It must NEVER
// appear on internal tab navigation (Dashboard <-> Historial <-> Integraciones), which uses
// Blazor's enhanced navigation.
//
// The overlay is rendered HIDDEN by default on the server (class "page-transition-hidden" is
// always present in MainLayout.razor's markup), so every normal page render/enhanced-navigation
// re-render keeps it hidden with no extra logic required - there is nothing to "undo" between
// navigations, which is what caused two previous bugs:
//   1) Listening to 'enhancednavigationstart'/'enhancednavigationend' re-showed the overlay on
//      every tab click (it fires on ALL enhanced navigations, not just post-login).
//   2) Removing those listeners without changing the overlay's default-visible markup left the
//      overlay stuck fully visible forever after the very first tab click, because the server
//      always re-rendered it as visible and nothing hid it again.
//
// Instead, Login.razor explicitly flags "I just logged in" via sessionStorage right before its
// forceLoad navigation. Only when that flag is present do we reveal the overlay here (once, on
// the resulting hard page load) and then hide it again after a short delay.
const TRANSITION_FLAG_KEY = 'smartops-show-transition-overlay';

export function afterWebStarted(blazor) {
    const overlay = document.getElementById('page-transition-overlay');
    if (!overlay) {
        return;
    }

    let justLoggedIn = false;
    try {
        justLoggedIn = window.sessionStorage.getItem(TRANSITION_FLAG_KEY) === '1';
        if (justLoggedIn) {
            window.sessionStorage.removeItem(TRANSITION_FLAG_KEY);
        }
    } catch {
        // sessionStorage can be unavailable (e.g. private browsing); simply skip the transition.
        return;
    }

    if (!justLoggedIn) {
        // Any other load (direct URL navigation, refresh, or tab switch) - overlay stays hidden.
        return;
    }

    overlay.classList.remove('page-transition-hidden');
    window.setTimeout(() => {
        overlay.classList.add('page-transition-hidden');
    }, 500);
}
