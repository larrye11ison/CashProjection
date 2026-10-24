using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;

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
        [NotifyPropertyChangedFor(nameof(BusyVisibility))]
        private bool _isBusy;

        // Window-rooted XAML can't use a Converter inside {x:Bind} (the
        // generated binding code needs a FrameworkElement "converter
        // lookup root," and Window — unlike WPF's — isn't one), so the
        // already-converted value is exposed directly instead.
        public Visibility BusyVisibility => IsBusy ? Visibility.Visible : Visibility.Collapsed;

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
