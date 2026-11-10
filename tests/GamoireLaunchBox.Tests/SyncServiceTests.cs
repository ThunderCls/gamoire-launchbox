using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GamoireLaunchBox.Tests
{
    public sealed class SyncServiceTests : IDisposable
    {
        private static readonly Uri Origin = new Uri("https://gamoire.example");
        private static readonly Guid SyncId = Guid.NewGuid();
        private readonly string directory = Path.Combine(Path.GetTempPath(), "gamoire-sync-" + Guid.NewGuid().ToString("N"));
        private readonly FakeTimeProvider time = new FakeTimeProvider();
        private readonly List<SyncService> services = new List<SyncService>();

        private sealed class RecordingHandler : HttpMessageHandler
        {
            private readonly object gate = new object();
            public readonly List<(string Path, string Body)> Requests = new List<(string, string)>();
            public HttpStatusCode StartStatus = HttpStatusCode.OK;
            public volatile TaskCompletionSource? BlockGames;

            public List<(string Path, string Body)> Snapshot()
            {
                lock (gate) return Requests.ToList();
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct);
                var path = request.RequestUri!.AbsolutePath;
                lock (gate) Requests.Add((path, body));
                if (path.EndsWith("/launcher-sync/games") && BlockGames is { } block) await block.Task;
                if (path.EndsWith("/sync/start"))
                {
                    return StartStatus == HttpStatusCode.OK
                        ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"syncId\":\"" + SyncId + "\"}") }
                        : new HttpResponseMessage(StartStatus) { Content = new StringContent("") };
                }

                return new HttpResponseMessage(HttpStatusCode.NoContent) { Content = new StringContent("") };
            }
        }

        private sealed class FakeSource : IGameSource
        {
            public Func<string, GameFacts?> ById = _ => null;
            public IReadOnlyList<GameFacts> Games = [];

            public IReadOnlyList<GameFacts> All() => Games;

            GameFacts? IGameSource.ById(string id) => ById(id);
        }

        private (SyncService Service, RecordingHandler Handler, GamoireState State) Create(FakeSource source, SyncOptions? options = null)
        {
            var store = new TokenStore(directory);
            store.Save(Origin, new StoredTokens
            {
                AccessToken = "gma", RefreshToken = "gmr", AccessExpiresUtc = DateTime.UtcNow.AddHours(1), UserName = "sam"
            });
            var handler = new RecordingHandler();
            var client = new GamoireApiClient(Origin, "1.0.0", store, handler, (_, _) => Task.CompletedTask);
            var state = new GamoireState();
            var service = new SyncService(client, state, source, () => options ?? new SyncOptions(true), time, _ => { });
            services.Add(service);
            return (service, handler, state);
        }

        private static async Task WaitFor(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!condition())
            {
                if (DateTime.UtcNow > deadline) throw new TimeoutException("Condition not met.");
                await Task.Delay(10);
            }
        }

        private static GameFacts Store(string id, int playTime = 0) =>
            GameMapperTests.Facts("Game " + id, storefrontId: "steam", itemId: "1", playTime: playTime, id: id);

        [Fact]
        public async Task QueuedGame_IsReadWhenTheTimerFires_NotWhenQueued()
        {
            var start = time.GetUtcNow();
            var source = new FakeSource
            {
                ById = id => Store(id, time.GetUtcNow() - start < TimeSpan.FromSeconds(1) ? 0 : 261)
            };
            var (service, handler, _) = Create(source);

            service.QueueGame("g1", TimeSpan.FromSeconds(2));
            time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(100);
            Assert.Empty(handler.Snapshot());

            time.Advance(TimeSpan.FromSeconds(1));
            await WaitFor(() => handler.Snapshot().Count == 1);

            var request = handler.Snapshot().Single();
            Assert.EndsWith("/launcher-sync/games", request.Path);
            using var json = JsonDocument.Parse(request.Body);
            Assert.Equal(261, json.RootElement.GetProperty("playtimeSeconds").GetInt64());
        }

        [Fact]
        public async Task GameQueuedAgainDuringItsSend_IsSentAgainAfterwards()
        {
            var playTime = 1;
            var source = new FakeSource { ById = id => Store(id, playTime) };
            var (service, handler, _) = Create(source);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            handler.BlockGames = release;

            service.QueueGame("g1", TimeSpan.Zero);
            await WaitFor(() => handler.Snapshot().Count == 1);

            playTime = 2;
            time.Advance(TimeSpan.FromSeconds(1));
            service.QueueGame("g1", TimeSpan.Zero);
            handler.BlockGames = null;
            release.SetResult();

            await WaitFor(() => handler.Snapshot().Count == 2);
            using var json = JsonDocument.Parse(handler.Snapshot()[1].Body);
            Assert.Equal(2, json.RootElement.GetProperty("playtimeSeconds").GetInt64());
        }

        [Fact]
        public async Task OtherKicks_DoNotFlushAGameBeforeItIsDue()
        {
            var start = time.GetUtcNow();
            var reads = new System.Collections.Concurrent.ConcurrentQueue<string>();
            var source = new FakeSource
            {
                ById = id =>
                {
                    reads.Enqueue(id);
                    return Store(id, id == "a" && time.GetUtcNow() - start < TimeSpan.FromSeconds(1) ? 0 : 261);
                }
            };
            var (service, handler, _) = Create(source);

            service.QueueGame("a", TimeSpan.FromSeconds(2));
            service.QueueGame("b", TimeSpan.Zero);
            await WaitFor(() => handler.Snapshot().Count == 1);
            await service.FlushAsync();

            Assert.DoesNotContain("a", reads);
            Assert.Contains("\"id\":\"b\"", handler.Snapshot().Single().Body);

            time.Advance(TimeSpan.FromSeconds(2));
            await WaitFor(() => handler.Snapshot().Count == 2);

            using var json = JsonDocument.Parse(handler.Snapshot()[1].Body);
            Assert.Equal("a", json.RootElement.GetProperty("id").GetString());
            Assert.Equal(261, json.RootElement.GetProperty("playtimeSeconds").GetInt64());
        }

        [Fact]
        public async Task FullSync_SendsBatchesOfFiveHundred_ThenFinishes()
        {
            var source = new FakeSource { Games = Enumerable.Range(0, 1200).Select(i => Store("g" + i)).ToList() };
            var (service, handler, _) = Create(source);

            service.RequestFullSync();
            await WaitFor(() => handler.Snapshot().Any(r => r.Path.EndsWith("/finish")));

            var requests = handler.Snapshot();
            var batches = requests.Where(r => r.Path.EndsWith("/batch")).ToList();
            Assert.Equal(3, batches.Count);
            var sizes = batches.Select(b => JsonDocument.Parse(b.Body).RootElement.GetProperty("games").GetArrayLength()).ToList();
            Assert.Equal(new[] { 500, 500, 200 }, sizes);
            using var finish = JsonDocument.Parse(requests.Last().Body);
            Assert.Equal(3, finish.RootElement.GetProperty("batches").GetInt32());
            Assert.Equal(1200, finish.RootElement.GetProperty("games").GetInt32());
        }

        [Fact]
        public async Task FullSync_DropsUnplayedRoms_ButSendsStoreGames()
        {
            var games = Enumerable.Range(0, 50).Select(i => GameMapperTests.Facts(emulated: true, id: "r" + i))
                .Concat(new[] { Store("s1"), Store("s2") }).ToList();

            var (service, handler, _) = Create(new FakeSource { Games = games });
            service.RequestFullSync();
            await WaitFor(() => handler.Snapshot().Any(r => r.Path.EndsWith("/finish")));
            using var finish = JsonDocument.Parse(handler.Snapshot().Last().Body);
            Assert.Equal(2, finish.RootElement.GetProperty("games").GetInt32());
        }

        [Fact]
        public async Task NotFoundOnStart_RaisesTerminalOnce_AndIgnoresQueuedGamesUntilTheNextFullSync()
        {
            var source = new FakeSource { ById = id => Store(id, 5), Games = [Store("g1")] };
            var (service, handler, _) = Create(source);
            handler.StartStatus = HttpStatusCode.NotFound;
            var notices = new System.Collections.Concurrent.ConcurrentQueue<string>();
            service.Terminal += notices.Enqueue;

            service.RequestFullSync();
            await WaitFor(() => notices.Count == 1);
            Assert.Equal("LaunchBox sync is switched off for your account in Gamoire.", notices.Single());

            service.QueueGame("g1", TimeSpan.Zero);
            await Task.Delay(150);
            Assert.DoesNotContain(handler.Snapshot(), r => r.Path.EndsWith("/launcher-sync/games"));
            Assert.Single(notices);

            handler.StartStatus = HttpStatusCode.OK;
            service.RequestFullSync();
            await WaitFor(() => handler.Snapshot().Any(r => r.Path.EndsWith("/finish")));
        }

        [Fact]
        public async Task Outdated_RaisedOncePerStreak_AndAgainAfterASuccess()
        {
            var (service, handler, _) = Create(new FakeSource { Games = [Store("g1")] });
            var raised = 0;
            service.Outdated += _ => Interlocked.Increment(ref raised);

            handler.StartStatus = HttpStatusCode.UpgradeRequired;
            service.RequestFullSync();
            await WaitFor(() => Volatile.Read(ref raised) == 1);
            for (var i = 0; i < 3; i++)
            {
                var before = handler.Snapshot().Count;
                time.Advance(SyncService.MaxBackoff);
                await WaitFor(() => handler.Snapshot().Count > before);
            }

            Assert.Equal(1, Volatile.Read(ref raised));

            handler.StartStatus = HttpStatusCode.OK;
            time.Advance(SyncService.MaxBackoff);
            await WaitFor(() => handler.Snapshot().Any(r => r.Path.EndsWith("/finish")));
            await Task.Delay(100);

            handler.StartStatus = HttpStatusCode.UpgradeRequired;
            service.RequestFullSync();
            await WaitFor(() => Volatile.Read(ref raised) == 2);
        }

        public void Dispose()
        {
            foreach (var service in services) service.Dispose();
            try
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
            catch (IOException)
            {
            }
        }
    }
}
