using System.Collections.ObjectModel;
using System.IO;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using CodexIsland.App.Services;
using CodexIsland.Core.Models;
using CodexIsland.Core.Quota;
using CodexIsland.Core.Signals;
using MediaBrush = System.Windows.Media.Brush;

namespace CodexIsland.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly IQuotaService _quotaService;
    private readonly IProjectSignalService _projectSignalService;
    private readonly SystemStatsService _systemStatsService = new();
    private readonly CompletionTransitionDetector _completionTransitionDetector = new();
    private CancellationTokenSource? _signalPulseCts;
    private bool _projectRefreshInFlight;
    private bool _quotaRefreshInFlight;
    private bool _isBusy;
    private bool _isExpanded = true;
    private bool _currentSignalForceFastBlink;
    private string _cpuText = "CPU --%";
    private string _ramText = "RAM --%";
    private string _gpuText = "GPU --";
    private string _netTrafficText = "0K / 0K";
    private ModelProfile _activeModel = ModelProfile.BuiltIn[0];
    private bool _modelMenuOpen;
    private readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexIsland", "model.json");
    private QuotaSnapshot _quota = QuotaSnapshot.Loading();
    private ProjectStatusSnapshot _project = ProjectStatusSnapshot.Ready();

    public MainViewModel(IQuotaService quotaService, IProjectSignalService projectSignalService)
    {
        _quotaService = quotaService;
        _projectSignalService = projectSignalService;
        ToggleExpandedCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
        RefreshCommand = new RelayCommand(async () => await RefreshAsync().ConfigureAwait(true), () => !IsBusy);
        RefreshQuotaCommand = new RelayCommand(() => _ = RefreshQuotaAsync());
        RefreshProjectsCommand = new RelayCommand(() => _ = RefreshProjectAsync());
        ProjectItems = new ObservableCollection<ProjectItemViewModel>
        {
            new("Codex project", ProjectSignal.Ready, "local monitor", "now", "Codex project\nlocal monitor", null, null, null, null)
        };

        SwitchToModelCommand = new RelayCommand(profile => { if (profile is ModelProfile mp) ActiveModel = mp; });
        ModelChoices = ModelProfile.BuiltIn;
        LoadModelChoice();

        _systemStatsService.StatsUpdated += (_, snapshot) => ApplyStats(snapshot);
        _systemStatsService.Start();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    /// Fired when a transient island pulse should start.
    public event EventHandler<SignalPulseEventArgs>? PulseRequested;

    /// Fired when the user clicks the island to acknowledge the bounce.
    public event EventHandler? BounceAcknowledged;

    public ICommand ToggleExpandedCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand RefreshQuotaCommand { get; }
    public RelayCommand RefreshProjectsCommand { get; }
    public ObservableCollection<ProjectItemViewModel> ProjectItems { get; }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                RefreshCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(RefreshGlyph));
            }
        }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            OnPropertyChanged(nameof(IsExpanded));
            OnPropertyChanged(nameof(ChevronGlyph));
        }
    }

    public string ChevronGlyph => IsExpanded ? "\uE70E" : "\uE70D";

    public IReadOnlyList<ModelProfile> ModelChoices { get; }
    public RelayCommand SwitchToModelCommand { get; }

    public ModelProfile ActiveModel
    {
        get => _activeModel;
        set
        {
            if (_activeModel == value) return;
            _activeModel = value;
            OnPropertyChanged(nameof(ActiveModel));
            OnPropertyChanged(nameof(ActiveModelName));
            OnPropertyChanged(nameof(IsCodexModel));
            ModelMenuOpen = false;
            PersistModelChoice();
            _ = RefreshQuotaForModelAsync();
        }
    }

    public bool IsCodexModel => ActiveModel.Id.StartsWith("codex", StringComparison.OrdinalIgnoreCase);

    public bool ModelMenuOpen
    {
        get => _modelMenuOpen;
        set => SetField(ref _modelMenuOpen, value);
    }

    public string ActiveModelName => ActiveModel.Name;
    public string RefreshGlyph => IsBusy ? "..." : "\uE72C";
    public string Title => "Codex Island";
    public string RunningCount => ProjectItems.Count.ToString();
    public string CpuText
    {
        get => _cpuText;
        private set => SetField(ref _cpuText, value);
    }
    public string RamText
    {
        get => _ramText;
        private set => SetField(ref _ramText, value);
    }
    public string GpuText
    {
        get => _gpuText;
        private set => SetField(ref _gpuText, value);
    }
    public string NetTrafficText
    {
        get => _netTrafficText;
        private set => SetField(ref _netTrafficText, value);
    }

    public ProjectSignal CurrentSignal => _project.Signal;
    public bool CurrentSignalForceFastBlink
    {
        get => _currentSignalForceFastBlink;
        private set => SetField(ref _currentSignalForceFastBlink, value);
    }
    public string ProjectStateText => ProjectSignalMapper.DisplayName(_project.Signal);
    public string ProjectMetaText => _project.LastEvent is null ? "local monitor" : _project.LastEvent;
    public string UpdatedText => $"updated {_quota.FetchedAt:HH:mm:ss}";
    public double RemainingPercent => _quota.RemainingPercent ?? 0;
    public string RemainingText => _quota.RemainingPercent is int value ? $"{value}%" : "--%";
    public string QuotaCaption => !IsCodexModel
        ? ActiveModelName
        : _quota.Health switch
    {
        QuotaHealth.Loading => "Reading Codex quota",
        QuotaHealth.Green => "quota healthy",
        QuotaHealth.Yellow => "quota below 10%",
        QuotaHealth.Red => "quota empty",
        QuotaHealth.Stale => "quota stale",
        QuotaHealth.Error => "quota unavailable",
        _ => "quota unknown"
    };
    public string QuotaWindowText => _quota.RemainingPercent is int value ? $"{value}% left" : "--";
    public string ResetText => _quota.ResetsAt is DateTimeOffset reset
        ? $"resets {FormatReset(reset)}"
        : (_quota.Health == QuotaHealth.Error ? "Codex quota unavailable" : "waiting for snapshot");
    public string WeeklyQuotaText => _quota.WeeklyRemainingPercent is int value ? $"{value}% left" : "--";
    public string WeeklyResetText => _quota.WeeklyResetsAt is DateTimeOffset reset
        ? $"resets {FormatReset(reset)}"
        : (_quota.Health == QuotaHealth.Error ? "Codex quota unavailable" : "waiting for snapshot");
    public double WeeklyProgressFraction => Math.Clamp((_quota.WeeklyRemainingPercent ?? 0) / 100d, 0d, 1d);
    public double WeeklyProgressBarWidth => WeeklyProgressFraction * 114d;
    public string SecondaryQuotaText => !IsCodexModel
        ? "custom provider"
        : _quota.Health == QuotaHealth.Error
        ? ShortError(_quota.ErrorMessage)
        : "local app-server";
    public QuotaHealth QuotaHealth => _quota.Health;
    public MediaBrush ProjectBrush => BrushForProject(_project.Signal);
    public MediaBrush QuotaBrush => BrushForQuota(_quota.Health);
    public MediaBrush RedDotBrush => DominantLamp == LampState.Red ? RedBrush : InactiveDot;
    public MediaBrush YellowDotBrush => DominantLamp == LampState.Yellow ? YellowBrush : InactiveDot;
    public MediaBrush GreenDotBrush => DominantLamp == LampState.Green ? GreenBrush : InactiveDot;

    private LampState DominantLamp
    {
        get
        {
            if (_project.Signal is ProjectSignal.Blocked || _quota.Health is QuotaHealth.Red or QuotaHealth.Error)
            {
                return LampState.Red;
            }

            if (_project.Signal is ProjectSignal.Permission or ProjectSignal.Attention or ProjectSignal.Stale ||
                _quota.Health is QuotaHealth.Yellow or QuotaHealth.Stale or QuotaHealth.Unknown)
            {
                return LampState.Yellow;
            }

            return LampState.Green;
        }
    }

    public async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await Task.WhenAll(RefreshQuotaAsync(), RefreshProjectAsync()).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
            RefreshCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(RefreshGlyph));
        }
    }

    public async Task RefreshQuotaAsync()
    {
        if (_quotaRefreshInFlight)
        {
            return;
        }

        _quotaRefreshInFlight = true;
        try
        {
            _quota = await _quotaService.GetSnapshotAsync().ConfigureAwait(true);
            RefreshQuotaComputedProperties();
        }
        finally
        {
            _quotaRefreshInFlight = false;
        }
    }

    public async Task RefreshProjectAsync()
    {
        if (_projectRefreshInFlight)
        {
            return;
        }

        _projectRefreshInFlight = true;
        try
        {
            var previousSignal = _project.Signal;
            var snapshots = await Task.Run(() => _projectSignalService.GetRecentProjects())
                .ConfigureAwait(true);

            RefreshProjectItems(snapshots);
            _project = AggregateProjectStatus(snapshots);

            if (previousSignal != _project.Signal)
            {
                if (_project.Signal == ProjectSignal.Working)
                {
                    _ = TriggerSignalPulseAsync(_project.Signal, TimeSpan.FromSeconds(1.8), false);
                }
            }

            if (_completionTransitionDetector.ShouldStartPersistentBounce(_project.Signal))
            {
                _ = TriggerSignalPulseAsync(_project.Signal, TimeSpan.FromSeconds(3.2), true);
            }

            RefreshProjectComputedProperties();
        }
        finally
        {
            _projectRefreshInFlight = false;
        }
    }

    private async Task RefreshQuotaForModelAsync()
    {
        if (!IsCodexModel)
        {
            _quota = new QuotaSnapshot(
                "custom", ActiveModel.Name, "third-party",
                null, null, null, null, null, null,
                DateTimeOffset.Now, QuotaHealth.Unknown,
                null, $"Using {ActiveModel.Name} — quota not available via Codex");
            RefreshQuotaComputedProperties();
            return;
        }

        await RefreshQuotaAsync();
    }

    private void PersistModelChoice()
    {
        try
        {
            var dir = Path.GetDirectoryName(_settingsPath);
            if (dir is not null) Directory.CreateDirectory(dir);
            File.WriteAllText(_settingsPath,
                System.Text.Json.JsonSerializer.Serialize(new { modelId = _activeModel.Id }));
        }
        catch
        {
            // Best-effort persistence only.
        }
    }

    private void LoadModelChoice()
    {
        try
        {
            if (!File.Exists(_settingsPath)) return;
            var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(_settingsPath));
            var modelId = json.RootElement.TryGetProperty("modelId", out var id) ? id.GetString() : null;
            if (modelId is not null)
            {
                var found = ModelProfile.BuiltIn.FirstOrDefault(m => m.Id == modelId);
                if (found is not null) _activeModel = found;
            }
        }
        catch
        {
            // Fall back to default model.
        }
    }

    private void RefreshProjectComputedProperties()
    {
        OnPropertyChanged(nameof(RunningCount));
        OnPropertyChanged(nameof(CurrentSignal));
        OnPropertyChanged(nameof(ProjectStateText));
        OnPropertyChanged(nameof(ProjectMetaText));
        OnPropertyChanged(nameof(ProjectBrush));
        OnPropertyChanged(nameof(RedDotBrush));
        OnPropertyChanged(nameof(YellowDotBrush));
        OnPropertyChanged(nameof(GreenDotBrush));
    }

    private void RefreshQuotaComputedProperties()
    {
        OnPropertyChanged(nameof(CpuText));
        OnPropertyChanged(nameof(RamText));
        OnPropertyChanged(nameof(GpuText));
        OnPropertyChanged(nameof(NetTrafficText));
        OnPropertyChanged(nameof(UpdatedText));
        OnPropertyChanged(nameof(RemainingPercent));
        OnPropertyChanged(nameof(RemainingText));
        OnPropertyChanged(nameof(QuotaCaption));
        OnPropertyChanged(nameof(QuotaWindowText));
        OnPropertyChanged(nameof(ResetText));
        OnPropertyChanged(nameof(WeeklyQuotaText));
        OnPropertyChanged(nameof(WeeklyResetText));
        OnPropertyChanged(nameof(WeeklyProgressFraction));
        OnPropertyChanged(nameof(WeeklyProgressBarWidth));
        OnPropertyChanged(nameof(SecondaryQuotaText));
        OnPropertyChanged(nameof(QuotaHealth));
        OnPropertyChanged(nameof(QuotaBrush));
    }

    private void RefreshProjectItems(IEnumerable<ProjectStatusSnapshot> snapshots)
    {
        ProjectItems.Clear();
        foreach (var item in snapshots)
        {
            var title = ProjectListTitle(item);
            var detail = ProjectSignalMapper.DisplayName(item.Signal);
            var updated = RelativeTime(item.UpdatedAt);
            ProjectItems.Add(new ProjectItemViewModel(
                title,
                item.Signal,
                string.IsNullOrWhiteSpace(item.DisplayName) ? detail : item.DisplayName,
                updated,
                $"{title}\n{item.DisplayName ?? item.ProjectName ?? "Codex project"}\n{detail}\n{item.LastEvent ?? "local monitor"}\nupdated {item.UpdatedAt:MM-dd HH:mm:ss}",
                item.WorkingDirectory,
                item.ProjectRoot,
                item.ProjectName,
                item.ProjectId));
        }

        if (ProjectItems.Count == 0)
        {
            ProjectItems.Add(new ProjectItemViewModel("No Codex project", ProjectSignal.Ready, "waiting", "", "No Codex project", null, null, null, null));
        }

        OnPropertyChanged(nameof(RunningCount));
    }

    private static string ProjectListTitle(ProjectStatusSnapshot item)
    {
        if (!string.IsNullOrWhiteSpace(item.ProjectName))
        {
            return item.ProjectName!;
        }

        if (!string.IsNullOrWhiteSpace(item.ProjectRoot))
        {
            return Path.GetFileName(item.ProjectRoot!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        return string.IsNullOrWhiteSpace(item.DisplayName) ? "Codex project" : item.DisplayName;
    }

    public void AcknowledgeBounce()
    {
        _signalPulseCts?.Cancel();
        CurrentSignalForceFastBlink = false;
        _completionTransitionDetector.Acknowledge(_project.Signal);
        BounceAcknowledged?.Invoke(this, EventArgs.Empty);
    }

    private async Task TriggerSignalPulseAsync(ProjectSignal signal, TimeSpan duration, bool forceFastBlink)
    {
        _signalPulseCts?.Cancel();
        _signalPulseCts = new CancellationTokenSource();
        var token = _signalPulseCts.Token;

        CurrentSignalForceFastBlink = forceFastBlink;
        PulseRequested?.Invoke(this, new SignalPulseEventArgs(signal, duration));

        try
        {
            await Task.Delay(duration, token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!token.IsCancellationRequested)
        {
            CurrentSignalForceFastBlink = false;
            BounceAcknowledged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void OpenProject(ProjectItemViewModel? item)
    {
        if (item is null) return;

        var targetPath = item.ProjectRoot ?? item.WorkingDirectory;
        if (!string.IsNullOrWhiteSpace(targetPath) && Directory.Exists(targetPath))
        {
            if (TryOpenCodexDesktop(targetPath))
            {
                return;
            }
        }

        if (!string.IsNullOrWhiteSpace(item.ThreadId))
        {
            if (TryResumeCodexThread(item.ThreadId!, targetPath))
            {
                return;
            }
        }

        if (!string.IsNullOrWhiteSpace(targetPath))
        {
            TryOpenCodexDesktop(targetPath);
            return;
        }

        TryOpenCodexDesktop(Environment.CurrentDirectory);
    }

    private static bool TryOpenCodexDesktop(string targetPath)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c start \"\" codex app {QuoteForCmd(targetPath)}",
                UseShellExecute = true,
                WorkingDirectory = Directory.Exists(targetPath) ? targetPath : Environment.CurrentDirectory
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryResumeCodexThread(string threadId, string? targetPath)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c start \"\" cmd /k codex resume {QuoteForCmd(threadId)}",
                UseShellExecute = true,
                WorkingDirectory = !string.IsNullOrWhiteSpace(targetPath) && Directory.Exists(targetPath)
                    ? targetPath
                    : Environment.CurrentDirectory
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string QuoteForCmd(string value)
        => $"\"{value.Replace("\"", "\"\"")}\"";

    public sealed class SignalPulseEventArgs : EventArgs
    {
        public SignalPulseEventArgs(ProjectSignal signal, TimeSpan duration)
        {
            Signal = signal;
            Duration = duration;
        }

        public ProjectSignal Signal { get; }
        public TimeSpan Duration { get; }
    }

    private static ProjectStatusSnapshot AggregateProjectStatus(IEnumerable<ProjectStatusSnapshot> projects)
    {
        var items = projects
            .Where(item => !item.DisplayName.StartsWith("No Codex", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (items.Count == 0)
        {
            return ProjectStatusSnapshot.Ready();
        }

        var currentProjectRoot = items.First().ProjectRoot;
        var relevant = string.IsNullOrWhiteSpace(currentProjectRoot)
            ? items
            : items.Where(item => string.Equals(item.ProjectRoot, currentProjectRoot, StringComparison.OrdinalIgnoreCase)).ToList();
        var signal = relevant.Select(item => item.Signal).OrderByDescending(PriorityForSignal).First();
        var first = relevant.First(item => item.Signal == signal);
        return new ProjectStatusSnapshot(
            first.ProjectId,
            first.DisplayName,
            signal,
            first.LastEvent,
            first.UpdatedAt,
            first.IsFresh,
            first.WorkingDirectory,
            first.ProjectRoot,
            first.ProjectName);
    }

    private static int PriorityForSignal(ProjectSignal signal)
    {
        return signal switch
        {
            ProjectSignal.Permission => 100,
            ProjectSignal.Blocked => 95,
            ProjectSignal.Attention => 90,
            ProjectSignal.Working => 80,
            ProjectSignal.Thinking => 70,
            ProjectSignal.ToolDone => 60,
            ProjectSignal.Completed => 50,
            ProjectSignal.Stale => 40,
            ProjectSignal.Paused => 10,
            _ => 20
        };
    }

    private void ApplyStats(SystemStatsSnapshot snapshot)
    {
        CpuText = $"CPU {snapshot.CpuPercent:0}%";
        RamText = $"RAM {snapshot.MemoryPercent:0}%";
        GpuText = snapshot.GpuPercent < 0 ? "GPU --" : $"GPU {snapshot.GpuPercent:0}%";
        NetTrafficText = $"{FormatBytes(snapshot.NetDownBytesPerSec)} / {FormatBytes(snapshot.NetUpBytesPerSec)}";
    }

    private static string FormatBytes(double bytesPerSecond)
    {
        if (bytesPerSecond >= 1024 * 1024)
        {
            return $"{bytesPerSecond / 1024 / 1024:0.#}M";
        }

        return $"{bytesPerSecond / 1024:0}K";
    }

    private static string RelativeTime(DateTimeOffset updatedAt)
    {
        var span = DateTimeOffset.Now - updatedAt;
        if (span.TotalSeconds < 15)
        {
            return "now";
        }

        if (span.TotalMinutes < 60)
        {
            return $"{Math.Max(1, (int)span.TotalMinutes)}m";
        }

        return $"{Math.Max(1, (int)span.TotalHours)}h";
    }

    private static string ShortError(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "check Codex login";
        }

        if (message.Contains("login required", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("authentication required", StringComparison.OrdinalIgnoreCase))
        {
            return "run codex login";
        }

        var oneLine = message.Replace("\r", " ").Replace("\n", " ").Trim();
        return oneLine.Length <= 48 ? oneLine : oneLine[..48] + "...";
    }

    private static string FormatReset(DateTimeOffset reset)
    {
        var span = reset - DateTimeOffset.Now;
        if (span < TimeSpan.Zero)
        {
            return "now";
        }

        if (span.TotalHours >= 1)
        {
            return $"in {(int)span.TotalHours}h {span.Minutes}m";
        }

        return $"in {Math.Max(0, span.Minutes)}m";
    }

    private static MediaBrush BrushForProject(ProjectSignal signal)
    {
        return signal switch
        {
            ProjectSignal.Permission or ProjectSignal.Attention => YellowBrush,
            ProjectSignal.Blocked => RedBrush,
            ProjectSignal.Thinking or ProjectSignal.Working or ProjectSignal.ToolDone => GreenBrush,
            ProjectSignal.Completed or ProjectSignal.Ready => GreenBrush,
            ProjectSignal.Stale or ProjectSignal.Paused => GrayBrush,
            _ => BlueBrush
        };
    }

    private static MediaBrush BrushForQuota(QuotaHealth health)
    {
        return health switch
        {
            QuotaHealth.Green => GreenBrush,
            QuotaHealth.Yellow => YellowBrush,
            QuotaHealth.Red or QuotaHealth.Error => RedBrush,
            _ => GrayBrush
        };
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private enum LampState
    {
        Green,
        Yellow,
        Red
    }

    private static readonly SolidColorBrush GreenBrush = DesignTokens.Frozen(DesignTokens.SignalGreen);
    private static readonly SolidColorBrush YellowBrush = DesignTokens.Frozen(DesignTokens.SignalYellow);
    private static readonly SolidColorBrush RedBrush = DesignTokens.Frozen(DesignTokens.SignalRed);
    private static readonly SolidColorBrush BlueBrush = DesignTokens.Frozen(DesignTokens.SignalWorkingBlue);
    private static readonly SolidColorBrush GrayBrush = DesignTokens.Frozen(DesignTokens.SignalStaleGray);
    private static readonly SolidColorBrush InactiveDot = DesignTokens.Frozen(DesignTokens.InactiveDot);

    private static void TryStartProcess(string fileName, string arguments)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore launcher failures and keep the island responsive.
        }
    }

    private static string ResolveCodexCommand()
    {
        var env = Environment.GetEnvironmentVariable("CODEX_CLI_PATH");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env))
        {
            return env;
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var npmCmd = Path.Combine(appData, "npm", "codex.cmd");
        if (File.Exists(npmCmd))
        {
            return npmCmd;
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var candidate = Path.Combine(localAppData, "OpenAI", "Codex", "bin", "codex.exe");
        if (File.Exists(candidate))
        {
            return candidate;
        }

        return "codex";
    }
}
