namespace SmartOps.Web.Services;

/// <summary>
/// Serializa el acceso al <c>AppDbContext</c> scoped del circuito. El Dashboard, el Drawer y el voto
/// comparten la misma instancia (un circuito = un scope) y la usaban en paralelo (handler de SignalR,
/// carga del diagnóstico existente, análisis), lo que puede tirar
/// "A second operation was started on this context".
/// Mitigación: la solución de fondo (IDbContextFactory) está registrada como deuda técnica.
/// </summary>
public sealed class DbContextGate
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<T> RunAsync<T>(Func<Task<T>> action)
    {
        await _lock.WaitAsync();
        try { return await action(); }
        finally { _lock.Release(); }
    }

    public async Task RunAsync(Func<Task> action)
    {
        await _lock.WaitAsync();
        try { await action(); }
        finally { _lock.Release(); }
    }
}
