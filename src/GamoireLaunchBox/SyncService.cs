using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GamoireLaunchBox
{
    public enum SyncResult
    {
        Ok,
        Retry,
        Drop,
        Stop,
        Terminal
    }

    public sealed record SyncOptions(bool SkipHidden);

    /// <summary>
    /// Serial worker: one full sync or a few single-game calls at a time. Failures stay queued in memory and retry with
    /// exponential backoff (30 s up to 30 min); nothing here ever opens a dialog or logs a name or token.
    /// </summary>
    public sealed class SyncService : IDisposable
    {
        public const int BatchSize = 500;
        public static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(30);
        public static readonly TimeSpan BaseBackoff = TimeSpan.FromSeconds(30);

        private readonly GamoireApiClient client;
        private readonly GamoireState state;
        private readonly IGameSource games;
        private readonly Func<SyncOptions> options;
        private readonly Action<string> log;
        private readonly SemaphoreSlim work = new SemaphoreSlim(1, 1);
        private readonly object gate = new object();
        private readonly Dictionary<string, DateTimeOffset> pending = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        private readonly TimeProvider time;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly ITimer debounce;
        private readonly ITimer retry;
        private CancellationTokenSource? flushCts;
        private bool fullPending;
        private volatile bool rerun;
        private volatile bool halted;
        private int attempt;
        private int outdatedNotified;
        private volatile bool disposed;

        public SyncService(
            GamoireApiClient client, GamoireState state, IGameSource games, Func<SyncOptions> options,
            TimeProvider time, Action<string> log)
        {
            this.client = client;
            this.state = state;
            this.games = games;
            this.options = options;
            this.log = log;
            this.time = time;
            debounce = time.CreateTimer(_ => Kick(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            retry = time.CreateTimer(_ => Kick(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        /// <summary>
        /// Raised once per terminal answer with the notice to show. <c>state.Raise</c> (window status) and this event
        /// (notification) both fire for the same notice.
        /// </summary>
        public event Action<string>? Terminal;

        /// <summary>Raised on the first 426 of a streak (reset by a successful call), not on every backoff retry.</summary>
        public event Action<string>? Outdated;

        public static TimeSpan BackoffFor(int failures)
        {
            var seconds = BaseBackoff.TotalSeconds * Math.Pow(2, Math.Max(0, Math.Min(failures, 12) - 1));
            return TimeSpan.FromSeconds(Math.Min(seconds, MaxBackoff.TotalSeconds));
        }

        /// <summary>
        /// Terminal answers (access off, too many games, a full sync the server rejects) stop the queue until the next start
        /// or Sync now; a single bad game is dropped; everything else retries with backoff.
        /// </summary>
        public static SyncResult Classify(ApiResponse response, bool full = false)
        {
            if (response.IsSuccess) return SyncResult.Ok;
            if (response.Status == ApiResponse.NotConnected) return SyncResult.Stop;
            if (response.Status == 404 && response.MidRun) return SyncResult.Retry;
            if (response.Status == 404 || response.Status == 413) return SyncResult.Terminal;
            if (response.Status == 400 || response.Status == 422) return full ? SyncResult.Terminal : SyncResult.Drop;
            return SyncResult.Retry;
        }

        public static string TerminalNotice(ApiResponse response)
        {
            if (response.Status == 404) return "LaunchBox sync is switched off for your account in Gamoire.";
            if (response.Status == 413) return "Gamoire already holds the most launcher games it accepts for your account.";
            return "Gamoire rejected the library data. Update the plugin, then click Sync now.";
        }

        public void RequestFullSync()
        {
            halted = false;
            lock (gate) fullPending = true;
            Kick();
        }

        /// <summary>
        /// Ignored after a terminal answer until the next start or Sync now. The game is read when the timer fires,
        /// not now: LaunchBox writes play time a moment after the game exits.
        /// </summary>
        public void QueueGame(string id, TimeSpan delay)
        {
            if (halted) return;
            var due = time.GetUtcNow() + (delay > TimeSpan.Zero ? delay : TimeSpan.Zero);
            lock (gate) pending[id] = pending.TryGetValue(id, out var existing) && existing > due ? existing : due;
            if (delay <= TimeSpan.Zero) Kick();
            else ArmDebounce();
        }

        /// <summary>Fires for the earliest game not yet due; due games go out with whichever kick comes first.</summary>
        private void ArmDebounce()
        {
            if (disposed) return;
            lock (gate)
            {
                TimeSpan? wait = null;
                var now = time.GetUtcNow();
                foreach (var due in pending.Values.Where(d => d > now))
                {
                    var left = due - now;
                    if (wait == null || left < wait) wait = left;
                }

                try
                {
                    debounce.Change(wait ?? Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }

        /// <summary>Drops the queue and cancels the flush in flight.</summary>
        public void Clear()
        {
            CancellationTokenSource? inFlight;
            lock (gate)
            {
                fullPending = false;
                pending.Clear();
                attempt = 0;
                inFlight = flushCts;
            }

            try
            {
                inFlight?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            if (disposed) return;
            try
            {
                retry.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                debounce.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private void Kick()
        {
            if (disposed) return;
            Task.Run(async () =>
            {
                try
                {
                    await FlushAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    log("Gamoire sync failed: " + ex.GetType().Name);
                    ScheduleRetry();
                }
            });
        }

        public async Task FlushAsync()
        {
            if (disposed) return;
            if (!await work.WaitAsync(0).ConfigureAwait(false))
            {
                rerun = true;
                return;
            }

            var ok = false;
            try
            {
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
                {
                    lock (gate) flushCts = cts;
                    try
                    {
                        do
                        {
                            rerun = false;
                            ok = await FlushCoreAsync(cts.Token).ConfigureAwait(false);
                        }
                        while (rerun && ok && !disposed);
                    }
                    finally
                    {
                        lock (gate) flushCts = null;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                ok = false;
            }
            finally
            {
                if (!disposed) work.Release();
            }

            // A request that arrived while the loop was finishing must not wait for the next trigger.
            if (!disposed) ArmDebounce();
            if (ok && !disposed && (rerun || HasWork())) Kick();
        }

        private bool HasWork()
        {
            lock (gate)
            {
                var now = time.GetUtcNow();
                return fullPending || pending.Values.Any(d => d <= now);
            }
        }

        private async Task<bool> FlushCoreAsync(CancellationToken ct)
        {
            bool full;
            lock (gate) full = fullPending;
            if (full)
            {
                KeyValuePair<string, DateTimeOffset>[] queuedBefore;
                lock (gate)
                {
                    var now = time.GetUtcNow();
                    queuedBefore = pending.Where(p => p.Value <= now).ToArray();
                }

                var outcome = await FullSyncAsync(ct).ConfigureAwait(false);
                var result = Classify(outcome, full: true);
                if (!Handle(result, outcome)) return false;
                lock (gate)
                {
                    fullPending = false;
                    foreach (var (id, due) in queuedBefore) RemoveIfUnchanged(id, due);
                }
            }

            KeyValuePair<string, DateTimeOffset>[] picked;
            lock (gate)
            {
                var now = time.GetUtcNow();
                picked = pending.Where(p => p.Value <= now).ToArray();
            }

            foreach (var (id, due) in picked)
            {
                var game = games.ById(id);
                var settings = options();
                var item = game != null && GameMapper.ShouldSend(game, settings.SkipHidden)
                    ? GameMapper.ToItem(game)
                    : null;

                if (item != null)
                {
                    var response = await client.UpsertGameAsync(item, ct).ConfigureAwait(false);
                    if (!Handle(Classify(response), response)) return false;
                }

                lock (gate) RemoveIfUnchanged(id, due);
            }

            lock (gate) attempt = 0;
            return true;
        }

        /// <summary>A game queued again during its send keeps its newer due time.</summary>
        private void RemoveIfUnchanged(string id, DateTimeOffset due)
        {
            if (pending.TryGetValue(id, out var current) && current == due) pending.Remove(id);
        }

        /// <summary>Returns the successful finish response, or the first failing one.</summary>
        private async Task<ApiResponse> FullSyncAsync(CancellationToken ct)
        {
            try
            {
                state.SetSyncProgress(0, null);
                var start = await client.StartSyncAsync(ct).ConfigureAwait(false);
                if (!start.IsSuccess) return start;
                var settings = options();
                var items = GameMapper.Map(games.All(), settings.SkipHidden);
                var syncId = GamoireApiClient.Parse<SyncStartResponse>(start)?.SyncId ?? Guid.Empty;
                if (syncId == Guid.Empty) return ApiResponse.Of(ApiResponse.NetworkFailure);

                var batches = 0;
                var sentGames = 0;
                state.SetSyncProgress(0, items.Count);
                foreach (var chunk in items.Chunk(BatchSize))
                {
                    batches++;
                    var request = new LauncherBatchRequest { Batch = batches, Games = chunk };
                    var sent = await client.RetryTransientAsync(() => client.SendBatchAsync(syncId, request, ct), ct).ConfigureAwait(false);
                    if (!sent.IsSuccess)
                    {
                        sent.MidRun = true;
                        return sent;
                    }

                    sentGames += chunk.Length;
                    state.SetSyncProgress(sentGames, items.Count);
                }

                var finish = await client.FinishSyncAsync(syncId, new SyncFinishRequest { Batches = batches, Games = items.Count }, ct)
                    .ConfigureAwait(false);
                if (finish.IsSuccess)
                {
                    state.RecordSync(items.Count);
                    log("Gamoire full sync sent " + items.Count + " games in " + batches + " batches");
                }
                else finish.MidRun = true;
                return finish;
            }
            finally
            {
                state.EndSyncProgress();
            }
        }

        /// <summary>True when the caller may carry on; otherwise a retry is scheduled or the queue is dropped.</summary>
        private bool Handle(SyncResult result, ApiResponse response)
        {
            if (result == SyncResult.Ok) Interlocked.Exchange(ref outdatedNotified, 0);
            if (result == SyncResult.Ok || result == SyncResult.Drop) return true;
            if (response.Status == 426)
            {
                log("Gamoire sync refused: the plugin is too old (426)");
                var notice = PairingFlow.Outdated(response);
                state.Raise(notice);
                if (Interlocked.Exchange(ref outdatedNotified, 1) == 0) RaiseSafely(Outdated, notice);
            }

            if (result == SyncResult.Stop)
            {
                Clear();
                return false;
            }

            if (result == SyncResult.Terminal)
            {
                halted = true;
                Clear();
                log("Gamoire sync stopped until the next start or Sync now (status " + response.Status + ")");
                var terminalNotice = TerminalNotice(response);
                state.Raise(terminalNotice);
                RaiseSafely(Terminal, terminalNotice);
                return false;
            }

            log("Gamoire sync will retry (status " + response.Status + ")");
            ScheduleRetry();
            return false;
        }

        private void RaiseSafely(Action<string>? handler, string notice)
        {
            try
            {
                handler?.Invoke(notice);
            }
            catch (Exception ex)
            {
                log("Gamoire notice handler failed: " + ex.GetType().Name);
            }
        }

        private void ScheduleRetry()
        {
            if (disposed) return;
            int failures;
            lock (gate) failures = ++attempt;
            retry.Change(BackoffFor(failures), Timeout.InfiniteTimeSpan);
        }

        public void Dispose()
        {
            disposed = true;
            lifetime.Cancel();
            debounce.Dispose();
            retry.Dispose();
            lifetime.Dispose();
            work.Dispose();
        }
    }
}
