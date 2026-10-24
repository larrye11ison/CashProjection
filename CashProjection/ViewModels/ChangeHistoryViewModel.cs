using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CashProjection.Models;
using CashProjection.Services;
using CashProjection.Services.Dialogs;

namespace CashProjection.ViewModels
{
    public sealed partial class ChangeHistoryViewModel : ObservableObject
    {
        private readonly ChangeHistoryService _historyService;
        private readonly IDialogService _dialogService;
        private readonly Action _onDataChanged;

        public event Action? RequestClose;

        public ObservableCollection<ChangeHistoryEntry> Entries { get; } = new();

        public ChangeHistoryViewModel(ChangeHistoryService historyService, IDialogService dialogService, Action onDataChanged)
        {
            _historyService = historyService;
            _dialogService = dialogService;
            _onDataChanged = onDataChanged;
            Reload();
        }

        public void Reload()
        {
            Entries.Clear();
            foreach (var entry in _historyService.GetAllNewestFirst())
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
        public async Task RollbackToHere(ChangeHistoryEntry? entry)
        {
            if (entry is null)
                return;

            var confirmed = await _dialogService.ConfirmAsync(
                $"This will undo every change made after {entry.TimestampUtc.ToLocalTime():g}. Continue?",
                "Roll back");
            if (!confirmed)
                return;

            _historyService.RollbackTo(entry.TimestampUtc);
            _onDataChanged();
            Reload();
        }

        [RelayCommand]
        public void Close() => RequestClose?.Invoke();
    }
}
