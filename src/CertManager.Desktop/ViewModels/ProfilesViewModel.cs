using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using CertManager.Core.Entities;
using CertManager.Desktop.Common;
using CertManager.Desktop.Services;

namespace CertManager.Desktop.ViewModels;

public class ProfileItemViewModel
{
    public ProfileItemViewModel(CertificateProfile profile)
    {
        ProfileId = profile.ProfileId;
        ProfileName = profile.ProfileName;
        AuthorityId = profile.AuthorityId;
        ValidationMethod = profile.DefaultValidationMethod.ToString();
        KeyAlgorithm = $"{profile.KeyAlgorithm} ({profile.KeySizeOrCurve})";
        RenewalThreshold = $"{profile.RenewalThresholdPercent}% lifetime";
        UseAri = profile.UseAri ? "Enabled" : "Disabled";
        Enabled = profile.Enabled;
        CreatedAtLocal = profile.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd");
    }

    public Guid ProfileId { get; }
    public string ProfileName { get; }
    public string AuthorityId { get; }
    public string ValidationMethod { get; }
    public string KeyAlgorithm { get; }
    public string RenewalThreshold { get; }
    public string UseAri { get; }
    public bool Enabled { get; }
    public string CreatedAtLocal { get; }
}

public class ProfilesViewModel : ViewModelBase
{
    private readonly ServiceIpcClient _ipcClient;
    private bool _isLoading;

    public ProfilesViewModel()
    {
        _ipcClient = new ServiceIpcClient();
        RefreshCommand = new RelayCommand(async () => await LoadProfilesAsync());
    }

    public ObservableCollection<ProfileItemViewModel> Profiles { get; } = new();

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public ICommand RefreshCommand { get; }

    public async Task LoadProfilesAsync()
    {
        if (IsLoading) return;
        IsLoading = true;

        try
        {
            var profiles = await _ipcClient.GetProfilesAsync();
            Profiles.Clear();
            foreach (var p in profiles)
            {
                Profiles.Add(new ProfileItemViewModel(p));
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
