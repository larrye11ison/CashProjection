using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CashProjection.Models;
using CashProjection.Services;

namespace CashProjection.ViewModels
{
    public sealed partial class ChangeHistoryViewModel : ObservableObject
    {
        private readonly ChangeHistoryService _historyService;
        private readonly int _accountId;
        private readonly Action _onDataChanged;

        public event Action? RequestClose;

        public ObservableCollection<ChangeHistoryEntry> Entries { get; } = new();

        // Rollback confirms in-place (an overlay within this same dialog)
        // rather than via a second ContentDialog — the same fix applied to
        // the projection item editor's Delete after a nested ContentDialog
        // there proved unreliable across several different WinUI failure
        // modes (confirmed via the app's own crash log each time, not
        // guessed). WinUI only ever reliably supports one ContentDialog
        // open at a time.
        [ObservableProperty]
        private ChangeHistoryEntry? _pendingRollbackEntry;

        public bool IsConfirmingRollback => PendingRollbackEntry is not null;

        public string RollbackConfirmationMessage => PendingRollbackEntry is { } entry
            ? $"This will undo every change made after {entry.TimestampUtc.ToLocalTime():g}. Continue?"
            : string.Empty;

        partial void OnPendingRollbackEntryChanged(ChangeHistoryEntry? value)
        {
            OnPropertyChanged(nameof(IsConfirmingRollback));
            OnPropertyChanged(nameof(RollbackConfirmationMessage));
        }

        public ChangeHistoryViewModel(ChangeHistoryService historyService, int accountId, Action onDataChanged)
        {
            _historyService = historyService;
            _accountId = accountId;
            _onDataChanged = onDataChanged;
            Reload();
        }

        public void Reload()
        {
            Entries.Clear();
            foreach (var entry in _historyService.GetAllNewestFirst(_accountId))
                Entries.Add(entry);
        }

        [RelayCommand]
        public void Undo(ChangeHistoryEntry? entry)
        {
            if (entry is null)
                return;

            _historyService.Undo(entry.Id);
            _onDataChanged();
            Reload();
        }

        [RelayCommand]
        public void RequestRollback(ChangeHistoryEntry? entry) => PendingRollbackEntry = entry;

        [RelayCommand]
        public void CancelRollback() => PendingRollbackEntry = null;

        [RelayCommand]
        public void ConfirmRollback()
        {
            if (PendingRollbackEntry is not { } entry)
                return;

            _historyService.RollbackTo(entry.TimestampUtc, _accountId);
            _onDataChanged();
            PendingRollbackEntry = null;
            Reload();
        }

        [RelayCommand]
        public void Close() => RequestClose?.Invoke();
    }
}
