using CommunityToolkit.Mvvm.ComponentModel;

namespace CashProjection.ViewModels
{
    public enum StatusSeverity
    {
        Info,
        Warning,
        Error,
    }

    // Backs the persistent status bar at the bottom of the main window.
    // Report(...) is the single entry point every service/view model should
    // use to surface the current (or last) async operation to the user.
    public sealed partial class StatusBarViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _currentMessage = "Ready";

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private StatusSeverity _severity = StatusSeverity.Info;

        [ObservableProperty]
        private DateTime? _lastUpdatedUtc;

        public void Report(string message, bool busy = false, StatusSeverity severity = StatusSeverity.Info)
        {
            CurrentMessage = message;
            IsBusy = busy;
            Severity = severity;
            LastUpdatedUtc = DateTime.UtcNow;
        }
    }
}
