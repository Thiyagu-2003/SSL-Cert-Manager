using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CertManager.Core.Entities;
using CertManager.Desktop.Common;
using CertManager.Desktop.Services;

namespace CertManager.Desktop.ViewModels;

public class LogItemViewModel
{
    public LogItemViewModel(NotificationRecord record)
    {
        Id = record.Id;
        Channel = record.Channel;
        Level = record.Level;
        Subject = record.Subject;
        Body = record.Body;
        Delivered = record.Delivered;
        SentAtLocal = record.SentAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

        LevelBadgeColor = Level.ToLowerInvariant() switch
        {
            "error" => "#EF4444",
            "warning" => "#F59E0B",
            _ => "#3B82F6"
        };

        LevelBadgeBackground = Level.ToLowerInvariant() switch
        {
            "error" => "#FEF2F2",
            "warning" => "#FFFBEB",
            _ => "#EFF6FF"
        };

        Icon = Level.ToLowerInvariant() switch
        {
            "error" => "🚨",
            "warning" => "⚠️",
            _ => "ℹ️"
        };
    }

    public Guid Id { get; }
    public string Channel { get; }
    public string Level { get; }
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
    private string _filter = "All";

    public LogsViewModel()
    {
        _ipcClient = new ServiceIpcClient();
        RefreshCommand = new RelayCommand(async () => await LoadLogsAsync());
    }

    public ObservableCollection<LogItemViewModel> Logs { get; } = new();

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
            if (SetProperty(ref _filter, value))
            {
                _ = LoadLogsAsync();
            }
        }
    }

    public ICommand RefreshCommand { get; }

    public async Task LoadLogsAsync()
    {
        if (IsLoading) return;
        IsLoading = true;

        try
        {
            var items = await _ipcClient.GetNotificationsAsync();
            Logs.Clear();

            var filtered = items.AsEnumerable();
            if (Filter != "All")
            {
                filtered = filtered.Where(i => string.Equals(i.Level, Filter, StringComparison.OrdinalIgnoreCase));
            }

            foreach (var item in filtered)
            {
                Logs.Add(new LogItemViewModel(item));
            }
        }
        catch
        {
            // Transient error
        }
        finally
        {
            IsLoading = false;
        }
    }
}
