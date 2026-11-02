using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Extensions.Logging;

namespace BacpacExplorer.App;

public partial class MainWindow
{
    private static bool SupportsMica => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        UpdateBackdrop();
    }

    private void UpdateBackdrop()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || !SupportsMica)
            return;

        var useMica = ((App)Application.Current).UseMicaBackground && !SystemParameters.HighContrast;
        const int systemBackdropType = 38; // DWMWA_SYSTEMBACKDROP_TYPE
        var backdrop = useMica ? 2 : 1; // DWMSBT_MAINWINDOW (Mica) or DWMSBT_NONE
        var result = DwmSetWindowAttribute(handle, systemBackdropType, ref backdrop, sizeof(int));
        var margins = new FrameMargins { Left = useMica ? -1 : 0 };
        var frameResult = DwmExtendFrameIntoClientArea(handle, ref margins);
        if (result < 0 || frameResult < 0)
        {
            _logger.LogWarning("Could not apply window backdrop. DWM results: {BackdropResult}, {FrameResult}", result, frameResult);
            useMica = false;
            backdrop = 1;
            margins = default;
            DwmSetWindowAttribute(handle, systemBackdropType, ref backdrop, sizeof(int));
            DwmExtendFrameIntoClientArea(handle, ref margins);
        }

        // WPF's automatic backdrop is disabled; manage it here so it can be toggled at runtime.
        HwndSource.FromHwnd(handle).CompositionTarget.BackgroundColor = useMica ? Colors.Transparent : SystemColors.WindowColor;
        if (useMica)
        {
            Background = Brushes.Transparent;
            WorkspaceTabs.Background = Brushes.Transparent;
            DetailsTabs.Background = Brushes.Transparent;
        }
        else
        {
            SetResourceReference(BackgroundProperty, "SolidBackgroundFillColorSecondaryBrush");
            WorkspaceTabs.ClearValue(Control.BackgroundProperty);
            DetailsTabs.ClearValue(Control.BackgroundProperty);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FrameMargins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr handle, ref FrameMargins margins);
}
