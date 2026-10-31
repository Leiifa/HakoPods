using System.Windows;
using HakoPods.Core.Audio;
using HakoPods.Core.Settings;
using HakoPods.Core.Startup;

namespace HakoPods.App;

/// <summary>
/// The first real settings surface. Functional, not styled: the design hand-off for it is
/// APW-311, and `atelier` is rate-limited, so this deliberately uses plain Windows controls
/// rather than inventing a look the popup would contradict.
///
/// It exists to teach `PreferredAudioEndpointId`, without which APW-203 cannot switch anything.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly JsonSettingsStore _store;
    private readonly AppSettings _settings;
    private readonly AudioEndpointRouter _router = new();

    public SettingsWindow(JsonSettingsStore store, AppSettings settings)
    {
        InitializeComponent();

        _store = store;
        _settings = settings;

        var endpoints = _router.ListRenderEndpointsDetailed();
        var current = settings.PreferredAudioEndpointId;

        EndpointList.ItemsSource = endpoints
            .Select(e => new EndpointChoice(e.Id, e.Name, e.Id == current))
            .ToList();

        var selected = EndpointList.Items.Cast<EndpointChoice>().FirstOrDefault(e => e.IsSelected);
        EndpointList.SelectedItem = selected;

        SwitchAudio.IsChecked = settings.SwitchDefaultAudioEndpoint;
        StartWithWindows.IsChecked = settings.StartWithWindows;
        AutoConnect.IsChecked = settings.AutoConnectOnLidOpen;
    }

    public AppSettings? SavedSettings { get; private set; }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var choice = EndpointList.SelectedItem as EndpointChoice;

        SavedSettings = _settings with
        {
            PreferredAudioEndpointId = choice?.Id ?? _settings.PreferredAudioEndpointId,
            SwitchDefaultAudioEndpoint = SwitchAudio.IsChecked == true,
            StartWithWindows = StartWithWindows.IsChecked == true,
            AutoConnectOnLidOpen = AutoConnect.IsChecked == true,
        };

        try
        {
            _store.Save(SavedSettings);
            AutostartService.SyncWith(SavedSettings.StartWithWindows, Environment.ProcessPath ?? string.Empty);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, "Could not save: " + ex.Message, "HakoPods",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
        Close();
    }

    private sealed class EndpointChoice
    {
        public EndpointChoice(string id, string? name, bool isSelected)
        {
            Id = id;
            Display = name ?? id;
            IsSelected = isSelected;
        }

        public string Id { get; }

        public string Display { get; }

        public bool IsSelected { get; }

        public override string ToString() => Display;
    }
}
