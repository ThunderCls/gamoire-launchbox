using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace GamoireLaunchBox
{
    public sealed class GamoireLaunching : IGameLaunchingPlugin
    {
        public void OnBeforeGameLaunching(IGame? game, IAdditionalApplication? app, IEmulator? emulator)
        {
        }

        public void OnAfterGameLaunched(IGame? game, IAdditionalApplication? app, IEmulator? emulator) =>
            GamoirePlugin.Instance.OnGameLaunched(game?.Id);

        public void OnGameExited() => GamoirePlugin.Instance.OnGameExited();
    }
}
