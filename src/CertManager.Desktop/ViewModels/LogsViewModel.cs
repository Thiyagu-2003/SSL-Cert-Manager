using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CertManager.Core.Entities;
using CertManager.Desktop.Common;
using CertManager.Desktop.Services;
using CertManager.Infrastructure.Logging;
using CertManager.Service;
using Microsoft.Win32;

namespace CertManager.Desktop.ViewModels;

public class LogItemViewModel : ViewModelBase
{
    private bool _isExpanded;

    public LogItemViewModel(NotificationRecord record)
    {
        Id = record.Id;
        Channel = string.IsNullOrWhiteSpace(record.Channel) ? "System" : record.Channel;
        Level = string.IsNullOrWhiteSpace(record.Level) ? "Info" : record.Level;
        Subject = record.Subject ?? string.Empty;
        Body = record.Body ?? string.Empty;
        Delivered = record.Delivered;
        SentAt = record.SentAt;
        SentAtLocal = record.SentAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        SentAtUtc = record.SentAt.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'");

        LevelBadgeColor = Level.ToLowerInvariant() switch
        {
            "error" => "#EF4444",
            "warning" => "#F59E0B",
            _ => "#0284C7"
        };

        LevelBadgeBackground = Level.ToLowerInvariant() switch
        {
            "error" => "#FEF2F2",
            "warning" => "#FFFBEB",
            _ => "#EFF6FF"
        };

        NormalizedLevel = Level.ToLowerInvariant() switch
        {
            "error" => "Error",
            "warning" => "Warning",
            _ => "Info"
        };

        Icon = string.Empty;

        // Automatically detect if this log has an actionable troubleshooting path
        HasTroubleshootingAction = string.Equals(NormalizedLevel, "Error", StringComparison.OrdinalIgnoreCase) &&
            (Subject.Contains("challenge", StringComparison.OrdinalIgnoreCase) ||
             Subject.Contains("issuance", StringComparison.OrdinalIgnoreCase) ||
             Body.Contains("ACME", StringComparison.OrdinalIgnoreCase) ||
             Body.Contains("connection", StringComparison.OrdinalIgnoreCase) ||
             Body.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
             Body.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
             Body.Contains("port 80", StringComparison.OrdinalIgnoreCase) ||
             Body.Contains("firewall", StringComparison.OrdinalIgnoreCase));

        ToggleExpandCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
    }

    public Guid Id { get; }
    public string Channel { get; }
    public string Level { get; }
    public string NormalizedLevel { get; }
    public string Subject { get; }
    public string Body { get; }
    public bool Delivered { get; }
    public DateTime SentAt { get; }
    public string SentAtLocal { get; }
    public string SentAtUtc { get; }
    public string LevelBadgeColor { get; }
    public string LevelBadgeBackground { get; }
    public string Icon { get; }
    public bool HasTroubleshootingAction { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public ICommand ToggleExpandCommand { get; }

    public string ToMarkdownString()
    {
        return $"[{SentAtLocal}] [{NormalizedLevel.ToUpperInvariant()}] {Subject}\nChannel: {Channel} | UTC: {SentAtUtc}\nEvent ID: {Id}\nDetails:\n{Body}";
    }
}

public class LogsViewModel : ViewModelBase, IDisposable
{
    private readonly ServiceIpcClient _ipcClient;
    private readonly Action<string, string?>? _onNavigate;
    private bool _isLoading;
    private string _filter = "All Events";
    private string _timeFilter = "All Time";
    private string _channelFilter = "All Channels";
    private string _searchQuery = string.Empty;
    private string _toastMessage = string.Empty;
    private bool _isLiveAutoRefresh;
    private DispatcherTimer? _liveTimer;
    private List<NotificationRecord> _cachedRecords = new();
    private readonly HashSet<Guid> _expandedIds = new();

    private int _totalEventsCount;
    private int _errorCount;
    private int _warningCount;
    private int _infoCount;
    private bool _isDisposed;

    public LogsViewModel(Action<string, string?>? onNavigate = null)
    {
        _onNavigate = onNavigate;
        _ipcClient = new ServiceIpcClient();

        RefreshCommand = new RelayCommand(async () => await LoadLogsAsync());
        ClearLogsCommand = new RelayCommand(async () => await ClearLogsAsync());
        OpenLogsFolderCommand = new RelayCommand(OpenLogsFolder);

        // KPI Filter Commands
        FilterAllCommand = new RelayCommand(() => Filter = "All Events");
        FilterErrorsCommand = new RelayCommand(() => Filter = "Errors");
        FilterWarningsCommand = new RelayCommand(() => Filter = "Warnings");
        FilterInfoCommand = new RelayCommand(() => Filter = "Info");

        // Copy & Export Commands
        CopyLogItemCommand = new RelayCommand(param =>
        {
            if (param is LogItemViewModel item)
            {
                CopyItemToClipboard(item);
            }
        });
        CopyAllLogsCommand = new RelayCommand(CopyAllLogsToClipboard);
        ExportLogsCommand = new RelayCommand(ExportLogs);

        // Smart Action Command
        TroubleshootLogCommand = new RelayCommand(param =>
        {
            _onNavigate?.Invoke("Tutorials", "Troubleshooting");
        });

        // Live Auto-Refresh Command
        ToggleLiveAutoRefreshCommand = new RelayCommand(() =>
        {
            IsLiveAutoRefresh = !IsLiveAutoRefresh;
        });

        // Initialize Live Auto-Refresh Timer (3-second interval)
        _liveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _liveTimer.Tick += async (s, e) =>
        {
            if (_isLiveAutoRefresh && !_isLoading)
            {
                await LoadLogsAsync(silent: true);
            }
        };
    }

    public ObservableCollection<LogItemViewModel> Logs { get; } = new();

    public ObservableCollection<string> FilterOptions { get; } = new()
    {
        "All Events",
        "Errors",
        "Warnings",
        "Info"
    };

    public ObservableCollection<string> TimeFilterOptions { get; } = new()
    {
        "All Time",
        "Today",
        "Last 24 Hours",
        "Last 7 Days"
    };

    public ObservableCollection<string> ChannelOptions { get; } = new()
    {
        "All Channels"
    };

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public bool IsLiveAutoRefresh
    {
        get => _isLiveAutoRefresh;
        set
        {
            if (SetProperty(ref _isLiveAutoRefresh, value))
            {
                if (value)
                {
                    _liveTimer?.Start();
                    ShowToast("Live Auto-Refresh active (3s)");
                }
                else
                {
                    _liveTimer?.Stop();
                    ShowToast("Live Auto-Refresh paused");
                }
            }
        }
    }

    public string Filter
    {
        get => _filter;
        set
        {
            string val = string.IsNullOrWhiteSpace(value) ? "All Events" : value;
            if (SetProperty(ref _filter, val))
            {
                ApplyFilter();
            }
        }
    }

    public string TimeFilter
    {
        get => _timeFilter;
        set
        {
            string val = string.IsNullOrWhiteSpace(value) ? "All Time" : value;
            if (SetProperty(ref _timeFilter, val))
            {
                ApplyFilter();
            }
        }
    }

    public string ChannelFilter
    {
        get => _channelFilter;
        set
        {
            string val = string.IsNullOrWhiteSpace(value) ? "All Channels" : value;
            if (SetProperty(ref _channelFilter, val))
            {
                ApplyFilter();
            }
        }
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
            {
                ApplyFilter();
            }
        }
    }

    public string ToastMessage
    {
        get => _toastMessage;
        set
        {
            if (SetProperty(ref _toastMessage, value))
            {
                OnPropertyChanged(nameof(HasToastMessage));
            }
        }
    }

    public bool HasToastMessage => !string.IsNullOrWhiteSpace(_toastMessage);

    public int TotalEventsCount
    {
        get => _totalEventsCount;
        set => SetProperty(ref _totalEventsCount, value);
    }

    public int ErrorCount
    {
        get => _errorCount;
        set => SetProperty(ref _errorCount, value);
    }

    public int WarningCount
    {
        get => _warningCount;
        set => SetProperty(ref _warningCount, value);
    }

    public int InfoCount
    {
        get => _infoCount;
        set => SetProperty(ref _infoCount, value);
    }

    public ICommand RefreshCommand { get; }
    public ICommand ClearLogsCommand { get; }
    public ICommand OpenLogsFolderCommand { get; }
    public ICommand FilterAllCommand { get; }
    public ICommand FilterErrorsCommand { get; }
    public ICommand FilterWarningsCommand { get; }
    public ICommand FilterInfoCommand { get; }
    public ICommand CopyLogItemCommand { get; }
    public ICommand CopyAllLogsCommand { get; }
    public ICommand ExportLogsCommand { get; }
    public ICommand TroubleshootLogCommand { get; }
    public ICommand ToggleLiveAutoRefreshCommand { get; }

    public async Task LoadLogsAsync(bool silent = false)
    {
        if (!silent)
        {
            if (IsLoading) return;
            IsLoading = true;
        }

        try
        {
            await ServiceHostBuilder.EnsureInProcessEngineRunningAsync();
            var items = await _ipcClient.GetNotificationsAsync();
            _cachedRecords = items.ToList();

            // Populate distinct channels dynamically
            var distinctChannels = _cachedRecords
                .Select(r => string.IsNullOrWhiteSpace(r.Channel) ? "System" : r.Channel.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(c => c)
                .ToList();

            void UpdateUiState()
            {
                string currentCh = ChannelFilter;
                ChannelOptions.Clear();
                ChannelOptions.Add("All Channels");
                foreach (var ch in distinctChannels)
                {
                    ChannelOptions.Add(ch);
                }
                if (ChannelOptions.Contains(currentCh))
                {
                    _channelFilter = currentCh;
                }
                else
                {
                    _channelFilter = "All Channels";
                }
                OnPropertyChanged(nameof(ChannelFilter));

                // Update KPI metric counters
                TotalEventsCount = _cachedRecords.Count;
                ErrorCount = _cachedRecords.Count(r => string.Equals(r.Level, "Error", StringComparison.OrdinalIgnoreCase));
                WarningCount = _cachedRecords.Count(r => string.Equals(r.Level, "Warning", StringComparison.OrdinalIgnoreCase));
                InfoCount = _cachedRecords.Count(r => !string.Equals(r.Level, "Error", StringComparison.OrdinalIgnoreCase) && !string.Equals(r.Level, "Warning", StringComparison.OrdinalIgnoreCase));

                ApplyFilter();
            }

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.Invoke(UpdateUiState);
            }
            else
            {
                UpdateUiState();
            }
        }
        catch
        {
            // Transient IPC or offline error
        }
        finally
        {
            if (!silent)
            {
                IsLoading = false;
            }
        }
    }

    private void ApplyFilter()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(ApplyFilter);
            return;
        }

        // Remember expanded IDs
        foreach (var l in Logs)
        {
            if (l.IsExpanded)
            {
                _expandedIds.Add(l.Id);
            }
            else
            {
                _expandedIds.Remove(l.Id);
            }
        }

        Logs.Clear();
        var query = _cachedRecords.AsEnumerable();

        // Level Filter
        if (string.Equals(Filter, "Errors", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(i => string.Equals(i.Level, "Error", StringComparison.OrdinalIgnoreCase));
        }
        else if (string.Equals(Filter, "Warnings", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(i => string.Equals(i.Level, "Warning", StringComparison.OrdinalIgnoreCase));
        }
        else if (string.Equals(Filter, "Info", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(i => !string.Equals(i.Level, "Error", StringComparison.OrdinalIgnoreCase) && !string.Equals(i.Level, "Warning", StringComparison.OrdinalIgnoreCase));
        }

        // Time Filter
        var now = DateTime.UtcNow;
        if (string.Equals(TimeFilter, "Today", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(i => i.SentAt.ToLocalTime().Date == DateTime.Today);
        }
        else if (string.Equals(TimeFilter, "Last 24 Hours", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(i => i.SentAt >= now.AddHours(-24));
        }
        else if (string.Equals(TimeFilter, "Last 7 Days", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(i => i.SentAt >= now.AddDays(-7));
        }

        // Channel Filter
        if (!string.Equals(ChannelFilter, "All Channels", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(ChannelFilter))
        {
            query = query.Where(i => string.Equals(i.Channel, ChannelFilter, StringComparison.OrdinalIgnoreCase));
        }

        // Search Query
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            string term = SearchQuery.Trim().ToLowerInvariant();
            query = query.Where(i =>
                (!string.IsNullOrWhiteSpace(i.Subject) && i.Subject.ToLowerInvariant().Contains(term)) ||
                (!string.IsNullOrWhiteSpace(i.Body) && i.Body.ToLowerInvariant().Contains(term)) ||
                (!string.IsNullOrWhiteSpace(i.Channel) && i.Channel.ToLowerInvariant().Contains(term)) ||
                (!string.IsNullOrWhiteSpace(i.Level) && i.Level.ToLowerInvariant().Contains(term)));
        }

        foreach (var item in query)
        {
            var vm = new LogItemViewModel(item);
            if (_expandedIds.Contains(vm.Id))
            {
                vm.IsExpanded = true;
            }
            Logs.Add(vm);
        }
    }

    public async Task ClearLogsAsync()
    {
        try
        {
            await _ipcClient.ClearNotificationsAsync();
            _cachedRecords.Clear();
            _expandedIds.Clear();
            TotalEventsCount = 0;
            ErrorCount = 0;
            WarningCount = 0;
            InfoCount = 0;
            ApplyFilter();
            ShowToast("All activity events cleared.");
        }
        catch
        {
            // Silently ignore
        }
    }

    public void OpenLogsFolder()
    {
        try
        {
            string dir = FileLoggerHelper.GetLogsDirectory();
            if (Directory.Exists(dir))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true
                });
            }
        }
        catch
        {
            // Silently ignore
        }
    }

    private void CopyItemToClipboard(LogItemViewModel item)
    {
        try
        {
            Clipboard.SetText(item.ToMarkdownString());
            ShowToast($"Copied log: {item.Subject}");
        }
        catch (Exception ex)
        {
            ShowToast($"Copy failed: {ex.Message}");
        }
    }

    private void CopyAllLogsToClipboard()
    {
        try
        {
            if (Logs.Count == 0)
            {
                ShowToast("No logs to copy.");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"# CertFlow Audit Trail Export ({DateTime.Now:yyyy-MM-dd HH:mm:ss})");
            sb.AppendLine($"Total Records: {Logs.Count}");
            sb.AppendLine();

            foreach (var log in Logs)
            {
                sb.AppendLine(log.ToMarkdownString());
                sb.AppendLine(new string('-', 60));
            }

            Clipboard.SetText(sb.ToString());
            ShowToast($"Copied {Logs.Count} log records to clipboard.");
        }
        catch (Exception ex)
        {
            ShowToast($"Copy failed: {ex.Message}");
        }
    }

    private void ExportLogs()
    {
        try
        {
            if (Logs.Count == 0)
            {
                ShowToast("No logs to export.");
                return;
            }

            var sfd = new SaveFileDialog
            {
                Title = "Export CertFlow Audit Trail Logs",
                Filter = "CSV File (*.csv)|*.csv|Text File (*.txt)|*.txt|All Files (*.*)|*.*",
                FileName = $"CertFlow_AuditLogs_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };

            if (sfd.ShowDialog() == true)
            {
                string ext = Path.GetExtension(sfd.FileName).ToLowerInvariant();
                var sb = new StringBuilder();

                if (ext == ".csv")
                {
                    sb.AppendLine("\"TimestampLocal\",\"TimestampUTC\",\"Level\",\"Channel\",\"Subject\",\"Body\",\"EventId\"");
                    foreach (var log in Logs)
                    {
                        sb.AppendLine($"\"{EscapeCsv(log.SentAtLocal)}\",\"{EscapeCsv(log.SentAtUtc)}\",\"{EscapeCsv(log.NormalizedLevel)}\",\"{EscapeCsv(log.Channel)}\",\"{EscapeCsv(log.Subject)}\",\"{EscapeCsv(log.Body)}\",\"{log.Id}\"");
                    }
                }
                else
                {
                    sb.AppendLine($"================================================================================");
                    sb.AppendLine($" CertFlow SSL/TLS Manager - Audit Trail & Activity Log");
                    sb.AppendLine($" Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss} | Total Events: {Logs.Count}");
                    sb.AppendLine($"================================================================================");
                    sb.AppendLine();

                    foreach (var log in Logs)
                    {
                        sb.AppendLine(log.ToMarkdownString());
                        sb.AppendLine();
                    }
                }

                File.WriteAllText(sfd.FileName, sb.ToString(), Encoding.UTF8);
                ShowToast($"Exported {Logs.Count} logs to {Path.GetFileName(sfd.FileName)}");
            }
        }
        catch (Exception ex)
        {
            ShowToast($"Export failed: {ex.Message}");
        }
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Replace("\"", "\"\"");
    }

    private void ShowToast(string message)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => ShowToast(message));
            return;
        }

        ToastMessage = message;
        Task.Delay(3000).ContinueWith(_ =>
        {
            var d = System.Windows.Application.Current?.Dispatcher;
            if (d != null)
            {
                d.InvokeAsync(() =>
                {
                    if (ToastMessage == message)
                    {
                        ToastMessage = string.Empty;
                    }
                });
            }
            else if (ToastMessage == message)
            {
                ToastMessage = string.Empty;
            }
        });
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _liveTimer?.Stop();
    }
}
