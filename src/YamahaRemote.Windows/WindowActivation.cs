using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace YamahaRemote.Windows;

internal static class WindowActivation
{
    internal static void Restore(Window window)
    {
        nint mainHandle = new WindowInteropHelper(window).Handle;
        nint popup = GetLastActivePopup(mainHandle);
        if (window is MainWindow mainWindow)
            mainWindow.RestoreFromTray();
        else
        {
            if (!window.IsVisible)
                window.Show();
            if (window.WindowState == WindowState.Minimized)
                window.WindowState = WindowState.Normal;
            window.Activate();
        }
        nint target = popup != 0 && popup != mainHandle && IsWindowVisible(popup) ? popup : mainHandle;
        if (target == 0)
            return;
        if (IsIconic(target))
            ShowWindow(target, 9);
        SetForegroundWindow(target);
    }

    [DllImport("user32.dll")]
    private static extern nint GetLastActivePopup(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);
}
