using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CashProjection.Services.Plaid;

namespace CashProjection.ViewModels
{
    public sealed partial class PlaidLinkViewModel : ObservableObject
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan PollTimeout = TimeSpan.FromMinutes(10);

        private readonly PlaidLinkService _linkService;
        private readonly bool _isUpdateMode;
        private CancellationTokenSource? _pollCts;
        private string? _linkToken;

        [ObservableProperty]
        private string? _hostedLinkUrl;

        [ObservableProperty]
        private string _statusMessage = "Preparing secure connection to Plaid...";

        public bool Succeeded { get; private set; }

        public event EventHandler? RequestClose;

        // isUpdateMode=true drives the reconnect flow: the existing Item is
        // repaired in place via its stored access_token (no new Item, no
        // new access_token, no Trial-plan slot consumed) rather than the
        // first-time Link flow that creates a brand-new Item.
        public PlaidLinkViewModel(PlaidLinkService linkService, bool isUpdateMode = false)
        {
            _linkService = linkService;
            _isUpdateMode = isUpdateMode;
        }

        public async Task InitializeAsync()
        {
            try
            {
                var result = _isUpdateMode
                    ? await _linkService.CreateUpdateModeLinkTokenAsync()
                    : await _linkService.CreateLinkTokenAsync();
                _linkToken = result.LinkToken;
                HostedLinkUrl = result.HostedLinkUrl;
                StatusMessage = _isUpdateMode
                    ? "Reconnecting to your bank — sign in to restore access."
                    : "Select your bank and sign in to connect your account.";
                StartPolling();
            }
            catch (Exception ex)
            {
                // TEMP diagnostic: the window closes almost immediately on
                // failure, too fast to read StatusMessage — so also capture
                // full details to a file in case a second round of guessing
                // would otherwise be needed.
                try
                {
                    System.IO.File.WriteAllText(
                        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cp_link_init_error.txt"),
                        ex.ToString());
                }
                catch { /* best-effort diagnostic only */ }

                SetStatus($"Could not start Plaid {(_isUpdateMode ? "reconnect" : "Link")}: {ex.Message}");
                Succeeded = false;
                RequestClose?.Invoke(this, EventArgs.Empty);
            }
        }

        public void StopPolling() => _pollCts?.Cancel();

        private void StartPolling()
        {
            _pollCts = new CancellationTokenSource();
            var token = _pollCts.Token;
            var linkToken = _linkToken!;

            _ = Task.Run(async () =>
            {
                var deadline = DateTime.UtcNow + PollTimeout;
                try
                {
                    while (!token.IsCancellationRequested && DateTime.UtcNow < deadline)
                    {
                        await Task.Delay(PollInterval, token);

                        if (_isUpdateMode)
                        {
                            UpdateModeSessionStatus status;
                            try
                            {
                                status = await _linkService.PollUpdateModeSessionAsync(linkToken);
                            }
                            catch
                            {
                                continue; // transient network/API hiccup — just try again next tick
                            }

                            if (status.IsFinished)
                            {
                                if (status.Succeeded)
                                    OnReconnected();
                                else
                                    OnExit(status.ExitMessage);
                                return;
                            }
                        }
                        else
                        {
                            LinkSessionStatus status;
                            try
                            {
                                status = await _linkService.PollSessionAsync(linkToken);
                            }
                            catch
                            {
                                continue;
                            }

                            if (status.IsFinished)
                            {
                                if (!string.IsNullOrEmpty(status.PublicToken))
                                    await OnConnectedAsync(status.PublicToken);
                                else
                                    OnExit(status.ExitMessage);
                                return;
                            }
                        }
                    }
                }
                catch (TaskCanceledException)
                {
                    return;
                }

                if (!token.IsCancellationRequested)
                    OnExit($"{(_isUpdateMode ? "Reconnect" : "Link")} session timed out.");
            }, token);
        }

        public async Task OnConnectedAsync(string publicToken)
        {
            StopPolling();
            SetStatus("Finishing setup...");
            try
            {
                await _linkService.ExchangePublicTokenAsync(publicToken);
                Succeeded = true;
            }
            catch (Exception ex)
            {
                SetStatus($"Could not finish connecting: {ex.Message}");
                Succeeded = false;
            }
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        // Update mode success: the Item was repaired in place. No token
        // exchange — the existing access_token is still valid and unchanged.
        private void OnReconnected()
        {
            StopPolling();
            Succeeded = true;
            SetStatus("Reconnected.");
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        public void OnExit(string? errorMessage)
        {
            StopPolling();
            SetStatus(errorMessage ?? $"{(_isUpdateMode ? "Reconnect" : "Link")} was closed before completing.");
            Succeeded = false;
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        // StatusMessage is bound in the view; WPF bindings require updates to
        // originate on the UI thread, but polling runs on a background task.
        private void SetStatus(string message)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess())
                StatusMessage = message;
            else
                dispatcher.Invoke(() => StatusMessage = message);
        }
    }
}
