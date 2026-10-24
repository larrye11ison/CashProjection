using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace CashProjection.Views
{
    // MicaBackdrop self-manages whether the current OS/hardware actually
    // supports Mica — it falls back to a plain solid background on
    // unsupported systems (older Windows 10, some virtualized/RDP
    // sessions) rather than throwing, so every window can just apply it
    // unconditionally.
    internal static class WindowBackdropHelper
    {
        public static void ApplyMica(Window window)
        {
            window.SystemBackdrop = new MicaBackdrop();
        }
    }
}
