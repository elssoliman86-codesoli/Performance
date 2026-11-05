using Performance.Services;
using System.IO;
using MessageBox = System.Windows.MessageBox;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace Performance;

public partial class MainWindow : Window
{
    private readonly HardwareMonitor _hardware = new();
    private readonly ProcessMonitor _processMonitor = new();
    private readonly NetworkMonitor _networkMonitor = new();
    private readonly StorageScanner _storageScanner = new();
    private readonly NvidiaProviderService _nvidia = new();
    private readonly ObservableCollection<ThreatFinding> _threatRows = new();
    private ThreatMonitorService? _threatMonitor;
    private readonly DispatcherTimer _threatTimer = new();
    private bool _threatBusy;
    private readonly PresentMonService _presentMon = new();
    private readonly GameWindowTracker _gameTracker = new();
    private readonly ObservableCollection<ProcessRow> _processRows = new();
    private readonly ObservableCollection<InstalledAppRow> _appRows = new();
    private readonly ObservableCollection<TcpConnectionRow> _connectionRows = new();
    private readonly ObservableCollection<StorageFileRow> _storageRows = new();
    private readonly ObservableCollection<ResidualFolderRow> _residualRows = new();
    private readonly DispatcherTimer _metricsTimer = new(), _processTimer = new(), _networkTimer = new();
    private readonly WinForms.NotifyIcon _trayIcon;
    private readonly OverlayWindow _overlay;
    private readonly System.Windows.Controls.ComboBox _residualCombo;
    private ICollectionView? _processView;
    private CancellationTokenSource? _scanCancellation;
    private bool _metricsBusy, _networkBusy, _allowExit, _isGaming;
    private SystemSnapshot? _lastSnapshot;
    private DateTime? _gameStartedUtc;
    private int _gamePid;
    private string _gameName = "";

    public MainWindow()
    {
        InitializeComponent();
        ProcessGrid.ItemsSource = _processRows;
        ThreatGrid.ItemsSource = _threatRows;
        ThreatReportPathText.Text = ThreatMonitorService.DefaultReportFolder;
        var provider = _nvidia.Load();
        AiBaseUrlBox.Text = provider.BaseUrl;
        if (!string.IsNullOrWhiteSpace(provider.Model)) AiModelBox.Text = provider.Model;
        _threatMonitor = new ThreatMonitorService(ThreatMonitorService.DefaultReportFolder);
        DashboardProcessGrid.ItemsSource = _processRows;
        AppsGrid.ItemsSource = _appRows;
        NetworkGrid.ItemsSource = _connectionRows;
        StorageGrid.ItemsSource = _storageRows;
        _processView = CollectionViewSource.GetDefaultView(_processRows);
        ProcessGrid.ItemsSource = _processView;

        // The residual-folder picker is injected into the existing details panel.
        _residualCombo = new System.Windows.Controls.ComboBox
        {
            Height = 27, Width = 440, HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Margin = new Thickness(0, 5, 0, 0), Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(22, 36, 58)),
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White),
            BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(52, 75, 107)),
            DisplayMemberPath = "Path", ItemsSource = _residualRows
        };
        if (ResidualText.Parent is StackPanel detailsPanel) detailsPanel.Children.Add(_residualCombo);

        StorageRootBox.Text = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        StorageRootBox.Margin = new Thickness(0, 0, 275, 0);

        _trayIcon = new WinForms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "Performance — System Monitor",
            Visible = true
        };
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Open Performance", null, (_, _) => ShowMainWindow());
        menu.Items.Add("Exit", null, (_, _) => ExitApplication());
        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => ShowMainWindow();

        _overlay = new OverlayWindow();
        _overlay.IslandClicked += ShowMainWindow;
        _overlay.PositionIsland();
        _overlay.Show();

        _metricsTimer.Interval = TimeSpan.FromSeconds(1);
        _metricsTimer.Tick += async (_, _) => await RefreshMetricsAsync();
        _processTimer.Interval = TimeSpan.FromSeconds(3);
        _processTimer.Tick += async (_, _) => await RefreshProcessesAsync();
        _networkTimer.Interval = TimeSpan.FromSeconds(8);
        _networkTimer.Tick += async (_, _) => await RefreshNetworkAsync();
        _threatTimer.Interval = TimeSpan.FromSeconds(10);
        _threatTimer.Tick += async (_, _) => await RefreshThreatAsync();

        Loaded += async (_, _) =>
        {
            _metricsTimer.Start();
            _processTimer.Start();
            _networkTimer.Start();
            _threatTimer.Start();
            await RefreshProcessesAsync();
            await RefreshNetworkAsync();
            await RefreshThreatAsync();
            await LoadAppsAsync();
        };
    }

    private async Task RefreshMetricsAsync()
    {
        if (_metricsBusy) return;
        _metricsBusy = true;
        try
        {
            var snapshot = await Task.Run(_hardware.Read);
            _lastSnapshot = snapshot;
            CpuValue.Text = $"{snapshot.CpuUsage:0}%";
            CpuTempValue.Text = snapshot.CpuTemperature.HasValue ? $"Package  {snapshot.CpuTemperature:0} °C" : "Temperature unavailable";
            CpuBar.Value = Math.Clamp(snapshot.CpuUsage, 0, 100);
            GpuValue.Text = snapshot.GpuUsage.HasValue ? $"{snapshot.GpuUsage:0}%" : "—";
            GpuTempValue.Text = snapshot.GpuTemperature.HasValue ? $"GPU  {snapshot.GpuTemperature:0} °C" : "Temperature unavailable";
            GpuBar.Value = Math.Clamp(snapshot.GpuUsage ?? 0, 0, 100);
            RamValue.Text = $"{snapshot.RamPercent:0}%";
            RamDetail.Text = $"{snapshot.RamUsedGb:0.0} / {snapshot.RamTotalGb:0.0} GB";
            RamBar.Value = Math.Clamp(snapshot.RamPercent, 0, 100);
            DriveValue.Text = $"{snapshot.DrivePercent:0}%";
            DriveDetail.Text = $"{snapshot.DriveUsedGb:0.0} / {snapshot.DriveTotalGb:0.0} GB";
            DriveBar.Value = Math.Clamp(snapshot.DrivePercent, 0, 100);
            GpuNameText.Text = snapshot.GpuName;
            HeaderClock.Text = DateTime.Now.ToString("ddd, dd MMM  •  HH:mm:ss");
            StatusText.Text = $"Updated {snapshot.SampledAt:HH:mm:ss}  •  CPU / memory / graphics telemetry";

            UpdateGameState();
            var duration = _gameStartedUtc.HasValue ? DateTime.UtcNow - _gameStartedUtc.Value : TimeSpan.Zero;
            GameDurationText.Text = duration.ToString(@"hh\:mm\:ss");
            GameNameText.Text = _isGaming ? _gameName : "No full-screen game detected";
            FpsText.Text = _presentMon.Fps.HasValue ? $"{_presentMon.Fps.Value:0} FPS" : "— FPS";
            SessionStateText.Text = _isGaming ? "GAME MODE ACTIVE" : "DESKTOP MODE";
            SessionStateText.Foreground = new System.Windows.Media.SolidColorBrush(_isGaming
                ? System.Windows.Media.Color.FromRgb(82, 215, 196)
                : System.Windows.Media.Color.FromRgb(140, 155, 181));
            _overlay.UpdateMetrics(snapshot, _presentMon.Fps, _gameName, duration);
            _overlay.SetGamingMode(_isGaming);
        }
        catch (Exception ex) { StatusText.Text = $"Telemetry warning: {ex.Message}"; }
        finally { _metricsBusy = false; }
    }

    private void UpdateGameState()
    {
        var foreground = _gameTracker.GetForegroundApp();
        bool active = foreground.IsFullscreen && foreground.Pid > 0;
        if (active)
        {
            if (_gamePid != foreground.Pid)
            {
                _presentMon.Stop();
                _gamePid = foreground.Pid;
                _gameName = foreground.Name;
                _gameStartedUtc = DateTime.UtcNow;
                _presentMon.Start(_gamePid);
            }
        }
        else if (_gamePid != 0)
        {
            _presentMon.Stop();
            _gamePid = 0;
            _gameStartedUtc = null;
            _gameName = "";
        }
        _isGaming = active;
        if (!_overlay.IsVisible) _overlay.Show();
    }

    private async Task RefreshProcessesAsync()
    {
        try
        {
            var rows = await Task.Run(_processMonitor.GetProcesses);
            int? selectedPid = (ProcessGrid.SelectedItem as ProcessRow)?.Pid;
            _processRows.Clear();
            foreach (var row in rows) _processRows.Add(row);
            _processView?.Refresh();
            if (selectedPid.HasValue) ProcessGrid.SelectedItem = _processRows.FirstOrDefault(x => x.Pid == selectedPid.Value);
        }
        catch { }
    }

    private async Task RefreshNetworkAsync()
    {
        if (_networkBusy) return;
        _networkBusy = true;
        try
        {
            var rows = await _networkMonitor.GetTcpConnectionsAsync();
            _connectionRows.Clear();
            foreach (var row in rows) _connectionRows.Add(row);
        }
        catch { }
        finally { _networkBusy = false; }
    }

    private async Task LoadAppsAsync()
    {
        try
        {
            var apps = await Task.Run(new InstalledAppsService().GetInstalledApps);
            _appRows.Clear();
            foreach (var app in apps) _appRows.Add(app);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Installed apps", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void ShowMainWindow()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(ShowMainWindow); return; }
        _overlay.Hide();
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void HideToBackground()
    {
        Hide();
        if (!_overlay.IsVisible) _overlay.Show();
        _overlay.PositionIsland();
    }

    private void ExitApplication()
    {
        _allowExit = true;
        _metricsTimer.Stop();
        _processTimer.Stop();
        _networkTimer.Stop();
        _threatTimer.Stop();
        _scanCancellation?.Cancel();
        _presentMon.Dispose();
        _hardware.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _overlay.Close();
        Close();
        System.Windows.Application.Current.Shutdown();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowExit) return;
        e.Cancel = true;
        HideToBackground();
        _trayIcon.ShowBalloonTip(1600, "Performance is still running",
            "The live island remains active. Use the tray icon to reopen or exit.", WinForms.ToolTipIcon.Info);
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Hide_Click(object sender, RoutedEventArgs e) => HideToBackground();
    private void NavDashboard_Click(object sender, RoutedEventArgs e) => MainTabs.SelectedIndex = 0;
    private void NavProcesses_Click(object sender, RoutedEventArgs e) => MainTabs.SelectedIndex = 1;
    private void NavNetwork_Click(object sender, RoutedEventArgs e) => MainTabs.SelectedIndex = 2;
    private async void NavApps_Click(object sender, RoutedEventArgs e)
    {
        MainTabs.SelectedIndex = 3;
        if (_appRows.Count == 0) await LoadAppsAsync();
    }
    private void NavStorage_Click(object sender, RoutedEventArgs e) => MainTabs.SelectedIndex = 4;
    private void NavThreat_Click(object sender, RoutedEventArgs e) => MainTabs.SelectedIndex = 5;
    private void NavAi_Click(object sender, RoutedEventArgs e) => MainTabs.SelectedIndex = 6;
    private async void RefreshThreat_Click(object sender, RoutedEventArgs e) => await RefreshThreatAsync();

    private void ChooseThreatReportFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new WinForms.FolderBrowserDialog { Description = "Choose where Performance stores monitoring reports", UseDescriptionForTitle = true, ShowNewFolderButton = true, SelectedPath = Directory.Exists(ThreatReportPathText.Text) ? ThreatReportPathText.Text : ThreatMonitorService.DefaultReportFolder };
        if (dialog.ShowDialog() == WinForms.DialogResult.OK)
        {
            ThreatReportPathText.Text = dialog.SelectedPath;
            _threatMonitor = new ThreatMonitorService(dialog.SelectedPath);
        }
    }

    private async void AnalyzeThreat_Click(object sender, RoutedEventArgs e)
    {
        if (ThreatGrid.SelectedItem is not ThreatFinding f)
        { MessageBox.Show(this, "Select a process first.", "AI analysis", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        try
        {
            ThreatDetailsText.Text += "\\n\\nAI analysis in progress…";
            string prompt = $"Analyze this Windows process telemetry as defensive triage. Explain evidence and uncertainty; do not recommend automatic deletion.\\nProcess: {f.ProcessName}\\nPID: {f.Pid}\\nPath: {f.ExecutablePath}\\nSHA256: {f.Sha256}\\nPublisher: {f.Publisher}\\nSignature status: {f.SignatureStatus}\\nRAM MB: {f.MemoryMb}\\nCPU %: {f.CpuPercent}\\nRisk heuristic: {f.Risk} {f.RiskScore}\\nEndpoints: {f.NetworkEndpoints}\\nSignals: {f.Reasons}";
            string analysis = await _nvidia.AnalyzeAsync(prompt);
            ThreatDetailsText.Text += "\\n\\nNVIDIA AI REVIEW\\n" + analysis;
        }
        catch (Exception ex) { ThreatDetailsText.Text += "\\nAI analysis unavailable: " + ex.Message; }
    }

    private async Task RefreshThreatAsync()
    {
        if (_threatBusy || _threatMonitor is null) return;
        _threatBusy = true;
        try
        {
            if (_processRows.Count == 0) await RefreshProcessesAsync();
            if (_connectionRows.Count == 0) await RefreshNetworkAsync();
            var processSnapshot = _processRows.ToList();
            var connectionSnapshot = _connectionRows.ToList();
            var rows = await Task.Run(async () => await _threatMonitor.CaptureAsync(processSnapshot, connectionSnapshot));
            int? selected = (ThreatGrid.SelectedItem as ThreatFinding)?.Pid;
            _threatRows.Clear();
            foreach (var row in rows) _threatRows.Add(row);
            if (selected.HasValue) ThreatGrid.SelectedItem = _threatRows.FirstOrDefault(x => x.Pid == selected.Value);
        }
        catch (Exception ex) { ThreatDetailsText.Text = "Threat scan warning: " + ex.Message; }
        finally { _threatBusy = false; }
    }

    private void ThreatGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThreatGrid.SelectedItem is not ThreatFinding f) return;
        ThreatDetailsText.Text = $"RISK: {f.Risk} ({f.RiskScore})\\nPROCESS: {f.ProcessName} / PID {f.Pid}\\nPATH: {f.ExecutablePath}\\nSHA-256: {f.Sha256}\\nPUBLISHER: {f.Publisher}\\nSIGNATURE: {f.SignatureStatus}\\nMEMORY: {f.MemoryMb:0.0} MB | CPU: {f.CpuPercent:0.0}%\\nFIRST SEEN: {f.FirstSeenUtc:u}\\nENDPOINTS: {f.NetworkEndpoints}\\nEVIDENCE: {f.Reasons}";
    }

    private async void ListAiModels_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string key = AiApiKeyBox.Password;
            if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("Enter the NVIDIA API key first.");
            AiStatusText.Text = "Loading models…";
            var models = await _nvidia.ListModelsAsync(AiBaseUrlBox.Text.Trim(), key);
            AiModelBox.ItemsSource = models;
            AiStatusText.Text = $"Loaded {models.Count} models from the configured endpoint.";
        }
        catch (Exception ex) { AiStatusText.Text = "Model list failed: " + ex.Message; }
    }

    private void SaveAiProvider_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _nvidia.Save(AiBaseUrlBox.Text, AiApiKeyBox.Password, AiModelBox.Text);
            AiApiKeyBox.Clear();
            AiStatusText.Text = "Provider saved. API key encrypted for this Windows user.";
        }
        catch (Exception ex) { AiStatusText.Text = "Save failed: " + ex.Message; }
    }

    private async void TestAiProvider_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(AiApiKeyBox.Password))
                _nvidia.Save(AiBaseUrlBox.Text, AiApiKeyBox.Password, AiModelBox.Text);
            AiStatusText.Text = "Testing selected model…";
            string result = await _nvidia.AnalyzeAsync("Reply with a short confirmation that the defensive analysis endpoint is reachable. Do not request or execute actions.");
            AiStatusText.Text = "Connection successful: " + result;
        }
        catch (Exception ex) { AiStatusText.Text = "Connection test failed: " + ex.Message; }
    }
    private async void RefreshProcesses_Click(object sender, RoutedEventArgs e) => await RefreshProcessesAsync();

    private void ProcessSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_processView is null) return;
        string query = ProcessSearchBox.Text.Trim();
        _processView.Filter = item => item is ProcessRow p && (query.Length == 0
            || p.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || p.Pid.ToString().Contains(query, StringComparison.OrdinalIgnoreCase)
            || p.Path.Contains(query, StringComparison.OrdinalIgnoreCase));
        _processView.Refresh();
    }

    private async void EndTask_Click(object sender, RoutedEventArgs e)
    {
        if (ProcessGrid.SelectedItem is not ProcessRow selected)
        {
            MessageBox.Show(this, "Select a process first.", "End task", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (selected.Pid <= 4 || selected.IsSystem)
        {
            MessageBox.Show(this, "System-session processes are protected from this quick action.", "End task", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var choice = MessageBox.Show(this,
            $"End {selected.Name} (PID {selected.Pid}) and its child processes? Unsaved work may be lost.",
            "Confirm end task", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (choice != MessageBoxResult.Yes) return;
        bool stopped = await Task.Run(() => ProcessMonitor.StopProcess(selected.Pid));
        if (!stopped) MessageBox.Show(this, "Windows refused to stop this process. Try running Performance as Administrator.", "End task", MessageBoxButton.OK, MessageBoxImage.Warning);
        await RefreshProcessesAsync();
    }

    private void AppsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AppsGrid.SelectedItem is InstalledAppRow app)
        {
            SelectedAppText.Text = $"{app.DisplayName}   •   {app.Publisher}";
            _residualRows.Clear();
            ResidualText.Text = app.InstallLocation;
        }
    }

    private async void FindResiduals_Click(object sender, RoutedEventArgs e)
    {
        if (AppsGrid.SelectedItem is not InstalledAppRow app) return;
        ResidualText.Text = "Searching known application-data locations…";
        var results = await Task.Run(() => DeepDeleteService.FindCandidates(app).Select(x => new ResidualFolderRow(x.Path, x.SizeMb)).ToList());
        _residualRows.Clear();
        foreach (var item in results) _residualRows.Add(item);
        _residualCombo.SelectedIndex = -1;
        ResidualText.Text = results.Count == 0 ? "No obvious matching folders found." : $"{results.Count} candidate folder(s). Select a path before deleting.";
    }

    private async void UninstallApp_Click(object sender, RoutedEventArgs e)
    {
        if (AppsGrid.SelectedItem is not InstalledAppRow app) return;
        if (string.IsNullOrWhiteSpace(app.UninstallString) && string.IsNullOrWhiteSpace(app.QuietUninstallString))
        {
            MessageBox.Show(this, "Windows has no registered uninstall command for this entry.", "Uninstall", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var confirm = MessageBox.Show(this,
            $"Launch the registered uninstaller for:\n\n{app.DisplayName}\n\nThe original uninstaller will run. Leftover folders are not deleted automatically.",
            "Confirm uninstall", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;
        bool launched = await Task.Run(() => InstalledAppsService.LaunchUninstaller(app));
        if (launched) MessageBox.Show(this, "The Windows uninstaller was started. After it finishes, scan for leftovers again.", "Uninstaller started", MessageBoxButton.OK, MessageBoxImage.Information);
        else MessageBox.Show(this, "Could not start the uninstaller. Try running Performance as Administrator.", "Uninstall error", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private async void DeleteResidual_Click(object sender, RoutedEventArgs e)
    {
        if (_residualCombo.SelectedItem is not ResidualFolderRow folder) return;
        var confirm = MessageBox.Show(this,
            $"Permanently delete this folder and all files inside it?\n\n{folder.Path}\n\nThis cannot be undone.",
            "Confirm residual cleanup", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;
        bool deleted = await Task.Run(() => InstalledAppsService.DeleteResidualFolder(folder.Path));
        if (deleted)
        {
            _residualRows.Remove(folder);
            ResidualText.Text = "Selected residual folder deleted.";
        }
        else MessageBox.Show(this, "Windows refused to delete this folder, or it is outside the allowed application-data locations.", "Cleanup", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void ChooseStorageRoot_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = "Choose a drive or folder to scan",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
            SelectedPath = Directory.Exists(StorageRootBox.Text) ? StorageRootBox.Text : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };
        if (dialog.ShowDialog() == WinForms.DialogResult.OK) StorageRootBox.Text = dialog.SelectedPath;
    }

    private async void ScanStorage_Click(object sender, RoutedEventArgs e)
    {
        string root = StorageRootBox.Text.Trim();
        if (!Directory.Exists(root))
        {
            MessageBox.Show(this, "Choose an existing folder or drive.", "Storage scan", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _scanCancellation?.Cancel();
        _scanCancellation = new CancellationTokenSource();
        var token = _scanCancellation.Token;
        StorageSummaryText.Text = "Scanning… large drives may take several minutes.";
        StorageCategoryText.Text = "";
        var progress = new Progress<(int Files, long Bytes)>(p =>
            StorageSummaryText.Text = $"Scanning {p.Files:N0} files  •  {p.Bytes / 1024d / 1024d / 1024d:0.0} GB examined…");
        try
        {
            var result = await _storageScanner.ScanAsync(root, progress, token);
            _storageRows.Clear();
            foreach (var item in result.LargestFiles) _storageRows.Add(item);
            StorageGrid.ItemsSource = _storageRows;
            StorageSummaryText.Text = $"Scanned {result.FilesScanned:N0} files  •  {result.TotalBytes / 1024d / 1024d / 1024d:0.00} GB total";
            StorageCategoryText.Text = string.Join("   ·   ", result.CategoryMb.OrderByDescending(x => x.Value).Take(5)
                .Select(x => $"{x.Key}: {x.Value / 1024d:0.0} GB"));
        }
        catch (OperationCanceledException) { StorageSummaryText.Text = "Scan cancelled."; }
        catch (Exception ex) { StorageSummaryText.Text = $"Scan failed: {ex.Message}"; }
    }
}
