using System.Runtime.InteropServices;
using System.Windows;

namespace GamoireLaunchBox
{
    public partial class GamoireWindow : Window
    {
        private readonly GamoireWindowModel model;

        public GamoireWindow(GamoireWindowModel model)
        {
            this.model = model;
            DataContext = model;
            InitializeComponent();
            Icon = GamoirePlugin.Instance.Icon.Window;
            Loaded += (_, _) => model.State.Touch();
            Closed += (_, _) => model.Detach();
        }

        private void OnCopyCode(object sender, RoutedEventArgs e)
        {
            var code = model.State.PairingCode;
            if (string.IsNullOrEmpty(code)) return;
            try
            {
                Clipboard.SetText(code);
            }
            catch (COMException)
            {
            }
        }

        private void OnOpenBrowser(object sender, RoutedEventArgs e) => model.OpenPairingPage();
    }
}
