using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CashProjection.Services.Dialogs
{
    // ContentDialog must be shown against a XamlRoot. This app only ever
    // has one window that hosts dialogs, so rather than threading a
    // Window/XamlRoot reference through every ViewModel constructor, the
    // main window's code-behind sets this once (on Loaded) and DialogService
    // just reads it.
    public static class DialogHost
    {
        public static XamlRoot? Root { get; set; }
    }

    public sealed class DialogService : IDialogService
    {
        public async Task<bool> ConfirmAsync(string message, string title, string confirmText = "Yes", string cancelText = "No")
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                PrimaryButtonText = confirmText,
                CloseButtonText = cancelText,
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = DialogHost.Root
            };

            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
        }

        public async Task ShowInfoAsync(string message, string title)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = new ScrollViewer
                {
                    Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    MaxHeight = 500
                },
                CloseButtonText = "OK",
                XamlRoot = DialogHost.Root
            };

            await dialog.ShowAsync();
        }
    }
}
