using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CertManager.Core.Entities;
using CertManager.Desktop.Common;
using CertManager.Desktop.Services;
using CertManager.Infrastructure.Logging;
using CertManager.Service;

namespace CertManager.Desktop.ViewModels;

public class LogItemViewModel
{
    public LogItemViewModel(NotificationRecord record)
    {
        Id = record.Id;
        Channel = string.IsNullOrWhiteSpace(record.Channel) ? "System" : record.Channel;
        Level = string.IsNullOrWhiteSpace(record.Level) ? "Info" : record.Level;
        Subject = record.Subject ?? string.Empty;
        Body = record.Body ?? string.Empty;
        Delivered = record.Delivered;
        SentAtLocal = record.SentAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

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
    }

    public Guid Id { get; }
    public string Channel { get; }
    public string Level { get; }
    public string NormalizedLevel { get; }
    public string Subject { get; }
    public string Body { get; }
    public bool Delivered { get; }
    public string SentAtLocal { get; }
    public string LevelBadgeColor { get; }
    public string LevelBadgeBackground { get; }
    public string Icon { get; }
}

public class LogsViewModel : ViewModelBase
{
    private readonly ServiceIpcClient _ipcClient;
    private bool _isLoading;
    private string _filter = "All Events";
    private string _searchQuery = string.Empty;
    private List<NotificationRecord> _cachedRecords = new();

    public LogsViewModel()
    {
        _ipcClient = new ServiceIpcClient();
        RefreshCommand = new RelayCommand(async () => await LoadLogsAsync());
        ClearLogsCommand = new RelayCommand(async () => await ClearLogsAsync());
        OpenLogsFolderCommand = new RelayCommand(OpenLogsFolder);
    }

    public ObservableCollection<LogItemViewModel> Logs { get; } = new();

    public ObservableCollection<string> FilterOptions { get; } = new()
    {
        "All Events",
        "Errors",
        "Warnings",
        "Info"
    };

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
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

    public ICommand RefreshCommand { get; }
    public ICommand ClearLogsCommand { get; }
    public ICommand OpenLogsFolderCommand { get; }

    public async Task LoadLogsAsync()
    {
        if (IsLoading) return;
        IsLoading = true;

        try
        {
            await ServiceHostBuilder.EnsureInProcessEngineRunningAsync();
            var items = await _ipcClient.GetNotificationsAsync();
            _cachedRecords = items.ToList();

            ApplyFilter();
        }
        catch
        {
            // Transient IPC or offline error
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplyFilter()
    {
        Logs.Clear();
        var query = _cachedRecords.AsEnumerable();

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
            query = query.Where(i => string.Equals(i.Level, "Info", StringComparison.OrdinalIgnoreCase));
        }

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
            Logs.Add(new LogItemViewModel(item));
        }
    }

    public async Task ClearLogsAsync()
    {
        try
        {
            await _ipcClient.ClearNotificationsAsync();
            _cachedRecords.Clear();
            ApplyFilter();
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
}
