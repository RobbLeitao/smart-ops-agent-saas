using System;
using System.Threading.Tasks;

namespace SmartOps.Web.Services
{
    // Lets the Dashboard page expose its "Pausar/Reanudar notificaciones" action to the
    // shared header in MainLayout.razor, without MainLayout needing direct knowledge of
    // Dashboard's internal state. Scoped per circuit (same lifetime as AIOpsService /
    // DiagnosticOrchestratorService), so each connected user has their own instance and
    // pausing notifications only affects their own session, not every user.
    public class DashboardHeaderActionsService
    {
        private Func<Task>? _toggleAction;

        public bool IsRegistered => _toggleAction != null;
        public bool IsPaused { get; private set; }

        public event Action? OnChange;

        // Called by Dashboard.razor once it's ready to handle toggle clicks, and again
        // whenever its paused state changes, so the header stays in sync.
        public void Register(Func<Task> toggleAction, bool isPaused)
        {
            _toggleAction = toggleAction;
            IsPaused = isPaused;
            OnChange?.Invoke();
        }

        public void UpdateState(bool isPaused)
        {
            IsPaused = isPaused;
            OnChange?.Invoke();
        }

        // Called when Dashboard is disposed (user navigates away) so the header hides
        // the button again instead of calling into a disposed component.
        public void Unregister()
        {
            _toggleAction = null;
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
