using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Unbroken.LaunchBox.Plugins.Services.Logging;
using Unbroken.LaunchBox.Plugins.Services.Notifications;

namespace GamoireLaunchBox
{
    public enum PluginMode
    {
        LaunchBox,
        BigBox
    }

    /// <summary>The one shared instance behind LaunchBox's separately constructed plugin classes.</summary>
    public sealed class GamoirePlugin
    {
        private static readonly TimeSpan StartupFallback = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan Periodic = TimeSpan.FromMinutes(60);
        private static readonly Lazy<GamoirePlugin> Shared = new Lazy<GamoirePlugin>(() => new GamoirePlugin());

        private readonly string folder = GamoireSettings.DefaultFolder();
        private readonly TokenStore tokens;
        private readonly IGameSource games = new LaunchBoxGameSource();
        private readonly string version;
        private readonly object gate = new object();
        private readonly Lazy<IconAssets> icon = new Lazy<IconAssets>(() => new IconAssets());
        private readonly GamoireSettings settings;
        private IPluginLogger? logger;
        private INotificationManager? notifier;
        private Session? session;
        private CancellationTokenSource? pairing;
        private Timer? startupTimer;
        private Timer? periodicTimer;
        private GamoireWindow? window;
        private string? lastLaunchedId;
        private volatile bool startupSyncPending;
        private volatile bool terminalStop;
        private volatile bool shutDown;
        private volatile PluginMode mode = (Environment.ProcessPath ?? "").Contains("BigBox", StringComparison.OrdinalIgnoreCase)
            ? PluginMode.BigBox
            : PluginMode.LaunchBox;
        private int disconnecting;

        private GamoirePlugin()
        {
            version = typeof(GamoirePlugin).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
            tokens = new TokenStore(folder);
            settings = GamoireSettings.Load(folder);
            State = new GamoireState();
        }

        public static GamoirePlugin Instance => Shared.Value;

        public GamoireState State { get; }

        public PluginMode Mode => mode;

        public GamoireSettings Settings => settings;

        public IconAssets Icon => icon.Value;

        public void Attach(IPluginLogger pluginLogger, INotificationManager notifications)
        {
            logger = pluginLogger;
            notifier = notifications;
        }

        public void OnInitialized()
        {
            RefreshStateFromTokens();
            startupSyncPending = true;
            startupTimer ??= new Timer(_ => RunStartupSync(), null, StartupFallback, Timeout.InfiniteTimeSpan);
            periodicTimer ??= new Timer(_ => RunPeriodicSync(), null, Periodic, Periodic);
            Log("Gamoire plugin initialised");
        }

        public void OnStartupCompleted(PluginMode startedIn)
        {
            mode = startedIn;
            RunStartupSync();
        }

        public void OnShutdown()
        {
            shutDown = true;
            CancelPairing(null);
            startupTimer?.Dispose();
            periodicTimer?.Dispose();
            lock (gate)
            {
                session?.Dispose();
                session = null;
            }
        }

        public void OnGameLaunched(string? id) => lastLaunchedId = id;

        public void OnGameExited()
        {
            var id = lastLaunchedId;
            if (id != null && !shutDown) CurrentSession()?.Sync.QueueGame(id, TimeSpan.FromSeconds(2));
        }

        public void ShowWindow()
        {
            if (mode == PluginMode.BigBox) return;
            OnUiThread(() =>
            {
                if (window != null)
                {
                    window.Activate();
                    return;
                }

                window = new GamoireWindow(new GamoireWindowModel(this));
                var owner = Application.Current?.MainWindow;
                if (owner != null && owner.IsLoaded) window.Owner = owner;
                window.Closed += (_, _) => window = null;
                window.Show();
                window.Activate();
            });
        }

        public void Connect(string serverUrl)
        {
            if (!UrlRules.TryGetOrigin(serverUrl, out var origin))
            {
                State.SetNotice(UrlRules.InvalidServerMessage);
                return;
            }

            if (State.IsConnected) return;
            settings.ServerUrl = origin.ToString().TrimEnd('/');
            settings.Save(folder);
            var active = EnsureSession(origin);
            RefreshStateFromTokens();
            if (State.IsConnected)
            {
                terminalStop = false;
                active.Sync.RequestFullSync();
                return;
            }

            var source = new CancellationTokenSource();
            var token = source.Token;
            CancelPairing(source);
            Task.Run(async () =>
            {
                try
                {
                    var paired = await PairingFlow.RunAsync(active.Client, tokens, State, version, OpenBrowser, token).ConfigureAwait(false);
                    Log(paired ? "Gamoire: this PC is connected" : "Gamoire: pairing ended without a connection");
                    if (paired)
                    {
                        terminalStop = false;
                        active.Sync.RequestFullSync();
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    Log("Gamoire pairing failed: " + ex.GetType().Name);
                    State.EndPairing("Pairing failed. Connect again.");
                }
            });
        }

        public void Disconnect()
        {
            Task.Run(async () =>
            {
                try
                {
                    var active = CurrentSession();
                    if (active == null) return;
                    Interlocked.Increment(ref disconnecting);
                    CancelPairing(null);
                    active.Sync.Clear();
                    try
                    {
                        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                        await active.Client.DisconnectDeviceAsync(timeout.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    finally
                    {
                        tokens.Delete(active.Client.Origin);
                        Interlocked.Decrement(ref disconnecting);
                    }

                    State.SetNotConnected();
                    Log("Gamoire: disconnected by the user");
                }
                catch (Exception ex)
                {
                    Log("Gamoire disconnect failed: " + ex.GetType().Name);
                }
            });
        }

        public void SyncNow()
        {
            if (!State.IsConnected)
            {
                State.SetNotice("Connect first.");
                return;
            }

            terminalStop = false;
            CurrentSession()?.Sync.RequestFullSync();
        }

        public void SetSkipHidden(bool value)
        {
            if (settings.SkipHidden == value) return;
            settings.SkipHidden = value;
            settings.Save(folder);
        }

        public void OpenInGamoire()
        {
            if (!UrlRules.TryGetOrigin(settings.ServerUrl, out var origin)) return;
            try
            {
                Open(UrlRules.IntegrationsPage(origin));
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Win32Exception)
            {
                Log("Gamoire could not open the browser");
            }
        }

        public void OpenPairingPage()
        {
            var active = CurrentSession();
            if (active == null) return;
            try
            {
                Open(UrlRules.PairingPage(active.Client.Origin));
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Win32Exception)
            {
                Log("Gamoire could not open the browser");
            }
        }

        private void RunStartupSync()
        {
            if (!startupSyncPending || shutDown) return;
            startupSyncPending = false;
            startupTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            terminalStop = false;
            if (State.IsConnected) CurrentSession()?.Sync.RequestFullSync();
        }

        private void RunPeriodicSync()
        {
            if (shutDown || terminalStop || startupSyncPending || !State.IsConnected || State.IsSyncing) return;
            CurrentSession()?.Sync.RequestFullSync();
        }

        private void RefreshStateFromTokens()
        {
            var active = CurrentSession();
            var stored = active == null ? null : tokens.Load(active.Client.Origin);
            if (stored == null) State.SetNotConnected();
            else State.SetConnected(stored.UserName);
        }

        private Session? CurrentSession() =>
            UrlRules.TryGetOrigin(settings.ServerUrl, out var origin) ? EnsureSession(origin) : null;

        private Session EnsureSession(Uri origin)
        {
            lock (gate)
            {
                if (session != null && UrlRules.OriginKey(session.Client.Origin) == UrlRules.OriginKey(origin)) return session;
                session?.Dispose();
                var client = new GamoireApiClient(origin, version, tokens);
                var sync = new SyncService(client, State, games, () => new SyncOptions(settings.SkipHidden), TimeProvider.System, Log);
                sync.Terminal += OnTerminal;
                sync.Outdated += Notify;
                client.Disconnected += () =>
                {
                    if (Volatile.Read(ref disconnecting) > 0) return;
                    sync.Clear();
                    State.SetDisconnectedByServer();
                    Log("Gamoire: the server rejected this PC's tokens; disconnected");
                    Notify("This PC was disconnected. Open Tools > Gamoire to connect again.");
                };
                session = new Session(client, sync);
                return session;
            }
        }

        private void OnTerminal(string notice)
        {
            terminalStop = true;
            Notify(notice);
        }

        private void Notify(string text)
        {
            var manager = notifier;
            if (mode != PluginMode.LaunchBox || manager == null) return;
            OnUiThread(() =>
            {
                try
                {
                    manager.ShowErrorAsync(new PluginNotificationDisplayRequest("Gamoire: " + text), CancellationToken.None)
                        .ContinueWith(
                            t => Log("Gamoire notification failed: " + t.Exception?.GetBaseException().GetType().Name),
                            TaskContinuationOptions.OnlyOnFaulted);
                }
                catch (Exception ex)
                {
                    Log("Gamoire notification failed: " + ex.GetType().Name);
                }
            });
        }

        private void CancelPairing(CancellationTokenSource? replacement)
        {
            var old = Interlocked.Exchange(ref pairing, replacement);
            if (old == null) return;
            old.Cancel();
            old.Dispose();
        }

        private void Log(string message)
        {
            try
            {
                logger?.Log(message);
            }
            catch (Exception)
            {
            }
        }

        private static void OnUiThread(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess()) action();
            else dispatcher.BeginInvoke(action);
        }

        private static void OpenBrowser(Uri url) => Open(url);

        // Only URLs built from the validated origin reach here: https, or http on a loopback host.
        private static void Open(Uri url)
        {
            var allowed = url.Scheme == Uri.UriSchemeHttps || (url.Scheme == Uri.UriSchemeHttp && UrlRules.IsLoopbackHost(url.Host));
            if (!allowed) throw new InvalidOperationException("Refusing to open a non-https address.");
            Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });
        }

        private sealed class Session(GamoireApiClient client, SyncService sync) : IDisposable
        {
            public GamoireApiClient Client { get; } = client;

            public SyncService Sync { get; } = sync;

            public void Dispose()
            {
                Sync.Clear();
                Sync.Dispose();
                Client.Dispose();
            }
        }
    }
}
