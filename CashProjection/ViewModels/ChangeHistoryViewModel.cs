using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CashProjection.Models;
using CashProjection.Services;

namespace CashProjection.ViewModels
{
    public sealed partial class ChangeHistoryViewModel : ObservableObject
    {
        private readonly ChangeHistoryService _historyService;
        private readonly Action _onDataChanged;

        public ObservableCollection<ChangeHistoryEntry> Entries { get; } = new();

        public ChangeHistoryViewModel(ChangeHistoryService historyService, Action onDataChanged)
        {
            _historyService = historyService;
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
        public void RollbackToHere(ChangeHistoryEntry? entry)
        {
            if (entry is null)
                return;

            var result = MessageBox.Show(
                $"This will undo every change made after {entry.TimestampUtc.ToLocalTime():g}. Continue?",
                "Roll back",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
                return;

            _historyService.RollbackTo(entry.TimestampUtc);
            _onDataChanged();
            Reload();
        }
    }
}
