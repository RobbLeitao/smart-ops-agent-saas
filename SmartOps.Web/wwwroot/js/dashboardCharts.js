window.dashboardCharts = (function () {
    const charts = {};

    function getCanvas(canvasId) {
        const canvas = document.getElementById(canvasId);
        if (!canvas) {
            return null;
        }

        return canvas;
    }

    function destroy(canvasId) {
        const chart = charts[canvasId];
        if (chart) {
            chart.destroy();
            delete charts[canvasId];
        }
    }

    function createChart(canvasId, config) {
        if (typeof Chart === 'undefined') {
            console.warn('Chart.js is not available.');
            return;
        }

        const canvas = getCanvas(canvasId);
        if (!canvas) {
            return;
        }

        destroy(canvasId);
        charts[canvasId] = new Chart(canvas, config);
    }

    function renderDoughnut(canvasId, labels, values) {
        createChart(canvasId, {
            type: 'doughnut',
            data: {
                labels: labels ?? [],
                datasets: [{
                    data: values ?? [],
                    backgroundColor: ['#ef4444', '#f97316', '#f59e0b', '#8b5cf6', '#14b8a6'],
                    borderWidth: 0
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: {
                    legend: {
                        labels: {
                            color: '#dbe4f0'
                        }
                    }
                }
            }
        });
    }

    function renderBar(canvasId, labels, approved, failed) {
        createChart(canvasId, {
            type: 'bar',
            data: {
                labels: labels ?? [],
                datasets: [
                    {
                        label: 'Aprobadas',
                        data: approved ?? [],
                        backgroundColor: '#22c55e'
                    },
                    {
                        label: 'Falladas',
                        data: failed ?? [],
                        backgroundColor: '#ef4444'
                    }
                ]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                scales: {
                    x: {
                        ticks: { color: '#dbe4f0' },
                        grid: { color: 'rgba(219, 228, 240, 0.12)' }
                    },
                    y: {
                        beginAtZero: true,
                        ticks: { color: '#dbe4f0' },
                        grid: { color: 'rgba(219, 228, 240, 0.12)' }
                    }
                },
                plugins: {
                    legend: {
                        labels: {
                            color: '#dbe4f0'
                        }
                    }
                }
            }
        });
    }

    return {
        renderDoughnut: renderDoughnut,
        renderBar: renderBar,
        destroy: destroy
    };
})();
