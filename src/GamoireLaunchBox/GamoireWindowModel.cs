using System.ComponentModel;

namespace GamoireLaunchBox
{
    public sealed class GamoireWindowModel : ObservableBase
    {
        private readonly GamoirePlugin plugin;
        private string serverUrl;
        private bool skipHidden;

        public GamoireWindowModel(GamoirePlugin plugin)
        {
            this.plugin = plugin;
            serverUrl = plugin.Settings.ServerUrl;
            skipHidden = plugin.Settings.SkipHidden;
            ConnectCommand = new RelayCommand(() => plugin.Connect(ServerUrl));
            DisconnectCommand = new RelayCommand(plugin.Disconnect);
            SyncNowCommand = new RelayCommand(plugin.SyncNow);
            OpenCommand = new RelayCommand(plugin.OpenInGamoire);
            State.PropertyChanged += OnStateChanged;
        }

        public GamoireState State => plugin.State;

        public string ServerUrl
        {
            get => serverUrl;
            set
            {
                serverUrl = value;
                OnPropertyChanged();
            }
        }

        public bool SkipHidden
        {
            get => skipHidden;
            set
            {
                skipHidden = value;
                plugin.SetSkipHidden(value);
                OnPropertyChanged();
            }
        }

        public bool CanEditServer => State.CanConnect && !State.IsPairing;

        public RelayCommand ConnectCommand { get; }

        public RelayCommand DisconnectCommand { get; }

        public RelayCommand SyncNowCommand { get; }

        public RelayCommand OpenCommand { get; }

        public void OpenPairingPage() => plugin.OpenPairingPage();

        public void Detach() => State.PropertyChanged -= OnStateChanged;

        private void OnStateChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(GamoireState.CanConnect) || args.PropertyName == nameof(GamoireState.IsPairing))
                OnPropertyChanged(nameof(CanEditServer));
        }
    }
}
