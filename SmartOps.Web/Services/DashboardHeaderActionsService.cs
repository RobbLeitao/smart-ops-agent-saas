using System;
using System.Threading.Tasks;

namespace SmartOps.Web.Services
{
    /// <summary>Estado del HubConnection del Dashboard, tal como lo muestra el header.</summary>
    public enum LiveConnectionState
    {
        Connecting,
        Live,
        Reconnecting
    }

    // Lets the Dashboard page expose its "Pausar/Reanudar notificaciones" action and the state of
    // its SignalR connection to the shared header in MainLayout.razor, without MainLayout needing
    // direct knowledge of Dashboard's internal state. Scoped per circuit, so each connected user has
    // their own instance and pausing notifications only affects their own session.
    public class DashboardHeaderActionsService
    {
        private Func<Task>? _toggleAction;

        public bool IsRegistered => _toggleAction != null;
        public bool IsPaused { get; private set; }
        public LiveConnectionState ConnectionState { get; private set; } = LiveConnectionState.Connecting;

        /// <summary>
        /// True cuando el Dashboard ya se registró y luego se fue (navegación a otra pestaña): a partir de
        /// ahí el header no vuelve a mostrar el «Conectando…» provisorio del primer frame.
        /// </summary>
        public bool HasUnregistered { get; private set; }

        public event Action? OnChange;

        // Called by Dashboard.razor once it's ready to handle toggle clicks.
        public void Register(Func<Task> toggleAction, bool isPaused)
        {
            _toggleAction = toggleAction;
            IsPaused = isPaused;
            ConnectionState = LiveConnectionState.Connecting;
            HasUnregistered = false;
            OnChange?.Invoke();
        }

        public void UpdateState(bool isPaused)
        {
            IsPaused = isPaused;
            OnChange?.Invoke();
        }

        public void SetConnectionState(LiveConnectionState state)
        {
            if (ConnectionState == state) return;
            ConnectionState = state;
            OnChange?.Invoke();
        }

        // Called when Dashboard is disposed (user navigates away) so the header hides
        // the indicator again instead of calling into a disposed component.
        public void Unregister()
        {
            _toggleAction = null;
            ConnectionState = LiveConnectionState.Connecting;
            HasUnregistered = true;
            OnChange?.Invoke();
        }

        public async Task ToggleAsync()
        {
            if (_toggleAction != null)
            {
                await _toggleAction();
            }
        }
    }
}
