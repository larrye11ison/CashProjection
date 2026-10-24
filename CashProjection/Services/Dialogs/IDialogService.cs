namespace CashProjection.Services.Dialogs
{
    // WinUI has no synchronous MessageBox.Show equivalent — every dialog is
    // an async ContentDialog. This is the one seam ViewModels go through for
    // confirm/info prompts, so call sites just await instead of blocking.
    public interface IDialogService
    {
        Task<bool> ConfirmAsync(string message, string title, string confirmText = "Yes", string cancelText = "No");

        Task ShowInfoAsync(string message, string title);
    }
}
