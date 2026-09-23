// Lightweight, dependency-free UI polish animations for SmartOps.
// Follows the same window.<module> IIFE pattern used by dashboardCharts.js.
window.smartOpsAnimations = (function () {
    const activeAnimations = {};

    function easeOutCubic(t) {
        return 1 - Math.pow(1 - t, 3);
    }

    // Animates the text content of #elementId from fromValue to toValue over durationMs,
    // appending suffix (e.g. "%") to the rendered number. Safe to call repeatedly; a new
    // call cancels any animation already running on the same element.
    function countUp(elementId, fromValue, toValue, suffix, durationMs) {
        const el = document.getElementById(elementId);
        if (!el) {
            return;
        }

        suffix = suffix || '';
        durationMs = durationMs || 1200;
        fromValue = Number(fromValue) || 0;
        toValue = Number(toValue) || 0;

        if (activeAnimations[elementId]) {
            cancelAnimationFrame(activeAnimations[elementId]);
            delete activeAnimations[elementId];
        }

        if (fromValue === toValue) {
            el.textContent = toValue + suffix;
            return;
        }

        const startTime = performance.now();

        function frame(now) {
            const elapsed = now - startTime;
            const progress = Math.min(elapsed / durationMs, 1);
            const eased = easeOutCubic(progress);
            const current = Math.round(fromValue + (toValue - fromValue) * eased);
            el.textContent = current + suffix;

            if (progress < 1) {
                activeAnimations[elementId] = requestAnimationFrame(frame);
            } else {
                el.textContent = toValue + suffix;
                delete activeAnimations[elementId];
            }
        }

        activeAnimations[elementId] = requestAnimationFrame(frame);
    }

    return {
        countUp: countUp
    };
})();
