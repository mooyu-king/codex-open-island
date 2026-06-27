using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using CodexIsland.App.ViewModels;
using CodexIsland.Core.Quota;
using CodexIsland.Core.Signals;
using Forms = System.Windows.Forms;

namespace CodexIsland.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly DispatcherTimer _projectRefreshTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _quotaRefreshTimer = new() { Interval = TimeSpan.FromSeconds(45) };
    private readonly DispatcherTimer _fadeTimer = new() { Interval = TimeSpan.FromSeconds(12) };
    private readonly Forms.NotifyIcon _notifyIcon;
    private bool _isExitRequested;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel(new CodexQuotaService(), new LocalProjectSignalService());
        DataContext = _viewModel;
        _viewModel.BounceRequested += (_, _) => BounceIsland();

        _projectRefreshTimer.Tick += async (_, _) => await _viewModel.RefreshProjectAsync().ConfigureAwait(true);
        _quotaRefreshTimer.Tick += async (_, _) => await _viewModel.RefreshQuotaAsync().ConfigureAwait(true);
        _fadeTimer.Tick += (_, _) =>
        {
            _fadeTimer.Stop();
            Opacity = 0.42;
        };

        _notifyIcon = CreateTrayIcon();
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        PositionAtTopCenter();
        ApplyPinVisual();
        _projectRefreshTimer.Start();
        _quotaRefreshTimer.Start();
        await _viewModel.RefreshAsync().ConfigureAwait(true);
    }

    private Forms.NotifyIcon CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();

        var showItem = menu.Items.Add("Show Codex Island", null, (_, _) => ShowIsland());
        menu.Items.Add("Hide", null, (_, _) => Dispatcher.Invoke(() => Hide()));
        menu.Items.Add("Always on top", null, (_, _) => Dispatcher.Invoke(() => Topmost = !Topmost));
        menu.Items.Add("Exit", null, (_, _) =>
        {
            _isExitRequested = true;
            Dispatcher.Invoke(() => Close());
        });

        var icon = new Forms.NotifyIcon
        {
            Icon = CreateTrayIconImage(),
            Text = "Codex Island",
            Visible = true,
            ContextMenuStrip = menu
        };
        icon.DoubleClick += (_, _) => ShowIsland();
        return icon;
    }

    private static System.Drawing.Icon CreateTrayIconImage()
    {
        var bmp = new System.Drawing.Bitmap(32, 32);
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        // Outer glow ring
        using var glowBrush = new System.Drawing.Drawing2D.LinearGradientBrush(
            new System.Drawing.Rectangle(0, 0, 32, 32),
            System.Drawing.Color.FromArgb(110, 255, 165),
            System.Drawing.Color.FromArgb(50, 215, 75),
            System.Drawing.Drawing2D.LinearGradientMode.Vertical);
        g.FillEllipse(glowBrush, 1, 1, 30, 30);

        // Inner dark circle
        using var innerBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(10, 12, 16));
        g.FillEllipse(innerBrush, 5, 5, 22, 22);

        // Center dot (green when idle)
        using var dotBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(50, 215, 75));
        g.FillEllipse(dotBrush, 11, 11, 10, 10);

        // Rim highlight
        using var rimPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(80, 255, 255, 255), 1f);
        g.DrawEllipse(rimPen, 5, 5, 22, 22);

        return System.Drawing.Icon.FromHandle(bmp.GetHicon());
    }

    private void ShowIsland()
    {
        Dispatcher.Invoke(() =>
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        });
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_isExitRequested)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsClickOnButton(e.OriginalSource))
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            _viewModel.IsExpanded = !_viewModel.IsExpanded;
            return;
        }

        try
        {
            DragMove();
        }
        catch
        {
            // DragMove can throw if the mouse button is released during startup.
        }
    }

    private static bool IsClickOnButton(object? source)
    {
        var element = source as System.Windows.DependencyObject;
        while (element is not null)
        {
            if (element is System.Windows.Controls.Button)
            {
                return true;
            }

            element = System.Windows.Media.VisualTreeHelper.GetParent(element);
        }

        return false;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Hide();

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        ApplyPinVisual();
    }

    private void ProjectCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is System.Windows.Controls.Button)
        {
            return;
        }

        if (sender is FrameworkElement element &&
            element.DataContext is ProjectItemViewModel item)
        {
            _viewModel.OpenProject(item);
        }
    }

    private void ProjectOpenButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element &&
            element.DataContext is ProjectItemViewModel item)
        {
            _viewModel.OpenProject(item);
        }
    }

    private void Window_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _fadeTimer.Stop();
        Opacity = 1;
    }

    private void Window_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _fadeTimer.Stop();
        _fadeTimer.Start();
    }

    private void PositionAtTopCenter()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left + (workArea.Width - ActualWidth) / 2;
        Top = workArea.Top + 10;
    }

    private void ApplyPinVisual()
    {
        PinButton.Background = Topmost
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x32, 0xD7, 0x4B))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x24, 0x26, 0x2B));
        PinButton.Foreground = Topmost
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xFF, 0xFF))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x9A, 0xA0, 0xA6));
        PinButton.ToolTip = Topmost ? "Always on top (on)" : "Always on top (off)";
    }

    private void BounceIsland()
    {
        var ease = new BackEase { Amplitude = 0.28, EasingMode = EasingMode.EaseOut };
        var scaleX = new DoubleAnimation(1.055, TimeSpan.FromMilliseconds(210))
        {
            EasingFunction = ease,
            AutoReverse = true
        };
        var scaleY = new DoubleAnimation(1.055, TimeSpan.FromMilliseconds(210))
        {
            EasingFunction = ease,
            AutoReverse = true
        };
        IslandScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scaleX);
        IslandScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scaleY);
    }
}
