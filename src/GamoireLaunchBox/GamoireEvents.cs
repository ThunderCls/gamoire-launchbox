using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;
using Unbroken.LaunchBox.Plugins.Services.Logging;
using Unbroken.LaunchBox.Plugins.Services.Notifications;

namespace GamoireLaunchBox
{
    public sealed class GamoireEvents : ISystemEventsPlugin
    {
        public GamoireEvents(IPluginLogger logger, INotificationManager notifications) =>
            GamoirePlugin.Instance.Attach(logger, notifications);

        public void OnEventRaised(string eventType)
        {
            var plugin = GamoirePlugin.Instance;
            if (eventType == SystemEventTypes.PluginInitialized) plugin.OnInitialized();
            else if (eventType == SystemEventTypes.LaunchBoxStartupCompleted) plugin.OnStartupCompleted(PluginMode.LaunchBox);
            else if (eventType == SystemEventTypes.BigBoxStartupCompleted) plugin.OnStartupCompleted(PluginMode.BigBox);
            else if (eventType == SystemEventTypes.LaunchBoxShutdownBeginning || eventType == SystemEventTypes.BigBoxShutdownBeginning)
                plugin.OnShutdown();
        }
    }
}
