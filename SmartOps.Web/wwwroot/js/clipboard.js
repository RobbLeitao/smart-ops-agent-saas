// Copies plain text to the clipboard. Returns true on success, false on failure (never throws), so the
// Blazor caller can show a clear error instead of failing silently or breaking the circuit.
// 1) navigator.clipboard (secure contexts: https / localhost). 2) Fallback: hidden textarea + execCommand('copy'),
// for insecure contexts or browsers that reject the async API after the Blazor Server round-trip.
window.smartOpsCopyText = async function (text) {
    if (navigator.clipboard && window.isSecureContext) {
        try {
            await navigator.clipboard.writeText(text);
            return true;
        } catch (e) {
            console.warn('navigator.clipboard.writeText failed, trying fallback', e);
        }
    }

    try {
        const textarea = document.createElement('textarea');
        textarea.value = text;
        textarea.setAttribute('readonly', '');
        textarea.style.position = 'fixed';
        textarea.style.top = '-1000px';
        textarea.style.opacity = '0';
        document.body.appendChild(textarea);
        textarea.select();
        const ok = document.execCommand('copy');
        textarea.remove();
        return ok;
    } catch (e) {
        console.error('Clipboard fallback failed', e);
        return false;
    }
};
