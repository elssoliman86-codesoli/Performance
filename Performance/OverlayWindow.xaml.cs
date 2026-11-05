using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Performance;

public partial class OverlayWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private bool _clickThrough;

    public event Action? IslandClicked;

    public OverlayWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => PositionIsland();
        SourceInitialized += (_, _) => ApplyWindowStyles();
        LocationChanged += (_, _) => { };
    }

    public void PositionIsland()
    {
        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        Top = 12;
    }

    public void SetGamingMode(bool active)
    {
        _clickThrough = active;
        ApplyWindowStyles();
        GameLabel.Text = active ? "PERFORMANCE  •  GAME SESSION" : "PERFORMANCE  •  DESKTOP MONITOR";
        Opacity = active ? 0.96 : 0.88;
    }

    public void UpdateMetrics(SystemSnapshot? snapshot, double? fps, string? gameName, TimeSpan duration)
    {
        ClockLabel.Text = DateTime.Now.ToString("HH:mm");
        DurationLabel.Text = duration.ToString(@"hh\:mm\:ss");
        FpsLabel.Text = fps.HasValue ? $"{fps.Value:0} FPS" : "— FPS";
        if (snapshot is not null)
        {
            GpuLabel.Text = snapshot.GpuTemperature.HasValue ? $"GPU {snapshot.GpuTemperature:0}°" : "GPU —°";
            CpuLabel.Text = snapshot.CpuTemperature.HasValue ? $"CPU {snapshot.CpuTemperature:0}°" : $"CPU {snapshot.CpuUsage:0}%";
        }
        if (!string.IsNullOrWhiteSpace(gameName) && _clickThrough)
            GameLabel.Text = $"{gameName.ToUpperInvariant()}  •  LIVE SESSION";
    }

    private void ApplyWindowStyles()
    {
        if (!IsLoaded && !IsVisible) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        long style = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        style |= WS_EX_NOACTIVATE;
        if (_clickThrough) style |= WS_EX_TRANSPARENT;
        else style &= ~WS_EX_TRANSPARENT;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(style));
    }

    private void Overlay_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_clickThrough) IslandClicked?.Invoke();
    }

    private static IntPtr GetWindowLongPtr(IntPtr hWnd, int index) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, index) : new IntPtr(GetWindowLong32(hWnd, index));
    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value) =>
        IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, index, value) : new IntPtr(SetWindowLong32(hWnd, index, value.ToInt32()));

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")] private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")] private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", EntryPoint = "SetWindowLong")] private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")] private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
}
