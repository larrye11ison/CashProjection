using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace CashProjection.Views
{
    // Unpackaged WinUI 3 apps don't automatically pick up the exe's
    // <ApplicationIcon> for the window/taskbar icon — AppWindow needs it
    // set explicitly, via the Win32 window handle.
    internal static class WindowIconHelper
    {
        public static void SetIcon(Window window, string relativeIcoPath)
        {
            var hwnd = WindowNative.GetWindowHandle(window);
            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            appWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, relativeIcoPath));
        }
    }
}
