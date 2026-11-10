using System.Drawing;
using Unbroken.LaunchBox.Plugins;

namespace GamoireLaunchBox
{
    public sealed class GamoireMenu : ISystemMenuItemPlugin
    {
        public string Caption => "Gamoire";

        public Image IconImage => GamoirePlugin.Instance.Icon.Menu;

        public bool ShowInLaunchBox => true;

        public bool ShowInBigBox => false;

        public bool AllowInBigBoxWhenLocked => false;

        public void OnSelected() => GamoirePlugin.Instance.ShowWindow();
    }
}
