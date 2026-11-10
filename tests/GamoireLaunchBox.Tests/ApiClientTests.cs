using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GamoireLaunchBox.Tests
{
    public sealed class ApiClientTests : IDisposable
    {
        private static readonly Uri Origin = new Uri("https://gamoire.example");
        private readonly string directory = Path.Combine(Path.GetTempPath(), "gamoire-api-" + Guid.NewGuid().ToString("N"));

        private sealed class FakeHandler : HttpMessageHandler
        {
            public int Refreshes;
            public int FailRefreshesWithNetworkError;
            public HttpStatusCode RefreshStatus = HttpStatusCode.OK;

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                await Task.Delay(50, cancellationToken);
                if (request.RequestUri!.AbsolutePath.EndsWith("/launcher-pairing/refresh"))
                {
                    if (Interlocked.Increment(ref Refreshes) <= FailRefreshesWithNetworkError)
                        throw new HttpRequestException("lost");
                    var body = "{\"accessToken\":\"gma_new\",\"refreshToken\":\"gmr_new\",\"expiresIn\":3600,\"userName\":\"sam\"}";
                    return new HttpResponseMessage(RefreshStatus) { Content = new StringContent(body) };
                }

                var ok = request.Headers.Authorization?.Parameter == "gma_new" && request.Headers.Contains("X-Launcher-Version");
                return new HttpResponseMessage(ok ? HttpStatusCode.NoContent : HttpStatusCode.Unauthorized) { Content = new StringContent("") };
            }
        }

        private TokenStore ExpiredTokens()
        {
            var store = new TokenStore(directory);
            store.Save(Origin, new StoredTokens
            {
                AccessToken = "gma_old",
                RefreshToken = "gmr_old",
                AccessExpiresUtc = DateTime.UtcNow.AddMinutes(-5),
                UserName = "sam"
            });
            return store;
        }

        [Fact]
        public async Task Refresh_IsSingleFlight_AndPersistsTheNewPair()
        {
            var store = ExpiredTokens();
            var handler = new FakeHandler();
            using (var client = new GamoireApiClient(Origin, "1.0.0", store, handler))
            {
                var results = await Task.WhenAll(Enumerable.Range(0, 4)
                    .Select(_ => client.SendAuthedAsync(HttpMethod.Post, "launcher-sync/games", new { }, CancellationToken.None)));

                Assert.All(results, r => Assert.True(r.IsSuccess));
                Assert.Equal(1, handler.Refreshes);
                Assert.Equal("gmr_new", store.Load(Origin)!.RefreshToken);
            }
        }

        [Theory]
        [InlineData(2, true)]
        [InlineData(3, false)]
        public async Task LostRefreshAnswer_IsRetriedInsideTheGraceWindow(int failures, bool succeeds)
        {
            var store = ExpiredTokens();
            var handler = new FakeHandler { FailRefreshesWithNetworkError = failures };
            var pauses = new System.Collections.Generic.List<TimeSpan>();
            Func<TimeSpan, CancellationToken, Task> noWait = (span, token) =>
            {
                pauses.Add(span);
                return Task.CompletedTask;
            };
            using (var client = new GamoireApiClient(Origin, "1.0.0", store, handler, noWait))
            {
                var result = await client.SendAuthedAsync(HttpMethod.Post, "launcher-sync/games", new { }, CancellationToken.None);

                Assert.Equal(succeeds, result.IsSuccess);
                Assert.Equal(Math.Min(failures + (succeeds ? 1 : 0), 3), handler.Refreshes);
                Assert.True(pauses.Count <= 2);
                Assert.All(pauses, span => Assert.InRange(span.TotalSeconds, 2, 5));
                Assert.Equal(succeeds ? "gmr_new" : "gmr_old", store.Load(Origin)!.RefreshToken);
            }
        }

        [Fact]
        public async Task RefreshRejected_DeletesTokens_AndRaisesDisconnected()
        {
            var store = ExpiredTokens();
            var handler = new FakeHandler { RefreshStatus = HttpStatusCode.Unauthorized };
            var raised = 0;
            using (var client = new GamoireApiClient(Origin, "1.0.0", store, handler))
            {
                client.Disconnected += () => raised++;

                var result = await client.SendAuthedAsync(HttpMethod.Post, "launcher-sync/games", new { }, CancellationToken.None);

                Assert.Equal(ApiResponse.NotConnected, result.Status);
                Assert.Equal(1, raised);
                Assert.Null(store.Load(Origin));
            }
        }

        private sealed class RecordingHandler : HttpMessageHandler
        {
            public readonly System.Collections.Generic.List<string> UserAgents = new();
            public readonly System.Collections.Generic.List<string> Clients = new();
            public readonly System.Collections.Generic.List<string> Bodies = new();
            public HttpStatusCode Status = HttpStatusCode.NoContent;
            public string Body = "";

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                UserAgents.Add(request.Headers.UserAgent.ToString());
                Clients.Add(string.Join(",", request.Headers.TryGetValues("X-Launcher-Client", out var values) ? values : System.Array.Empty<string>()));
                Bodies.Add(request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
                return new HttpResponseMessage(Status) { Content = new StringContent(Body) };
            }
        }

        [Fact]
        public async Task EveryRequest_SendsTheUserAgent()
        {
            var store = new TokenStore(directory);
            store.Save(Origin, new StoredTokens
            {
                AccessToken = "gma_ok",
                RefreshToken = "gmr_ok",
                AccessExpiresUtc = DateTime.UtcNow.AddHours(1),
                UserName = "sam"
            });
            var handler = new RecordingHandler();
            using (var client = new GamoireApiClient(Origin, "1.0.0", store, handler))
            {
                await client.StartPairingAsync("PC", "1.0.0", CancellationToken.None);
                await client.SendAuthedAsync(HttpMethod.Post, "launcher-sync/games", new { }, CancellationToken.None);
            }

            Assert.Equal(2, handler.UserAgents.Count);
            Assert.All(handler.UserAgents, ua => Assert.Equal("Gamoire-LaunchBox/1.0.0", ua));
            Assert.All(handler.Clients, c => Assert.Equal("launchbox", c));
        }

        [Fact]
        public async Task PairingStart_SendsTheClientIdInCamelCase()
        {
            var handler = new RecordingHandler();
            using var client = new GamoireApiClient(Origin, "1.0.0", new TokenStore(directory), handler);

            await client.StartPairingAsync("PC", "1.0.0", CancellationToken.None);

            Assert.Equal("{\"clientId\":\"launchbox\",\"deviceName\":\"PC\",\"pluginVersion\":\"1.0.0\"}", handler.Bodies[0]);
        }

        [Fact]
        public async Task ProblemBody_WithMixedCasePropertyNames_Parses()
        {
            var handler = new RecordingHandler
            {
                Status = HttpStatusCode.BadRequest,
                Body = "{\"Type\":\"https://x/problems/slow-down\",\"DETAIL\":\"Wait a bit.\"}"
            };
            using var client = new GamoireApiClient(Origin, "1.0.0", new TokenStore(directory), handler);

            var result = await client.PollPairingAsync("code", CancellationToken.None);

            Assert.True(result.Is("slow-down"));
            Assert.Equal("Wait a bit.", result.Detail);
        }

        [Theory]
        [InlineData("{\"type\":\"https://x/problems/slow-down\",\"detail\":{\"a\":1}}", "slow-down")]
        [InlineData("{\"type\":5,\"detail\":true}", "5")]
        public async Task OneBadProblemField_DoesNotDropTheOther(string body, string expectedType)
        {
            var handler = new RecordingHandler { Status = HttpStatusCode.BadRequest, Body = body };
            using var client = new GamoireApiClient(Origin, "1.0.0", new TokenStore(directory), handler);

            var result = await client.PollPairingAsync("code", CancellationToken.None);

            Assert.EndsWith(expectedType, result.ProblemType);
            Assert.True(result.Detail == null || result.Detail == "true");
        }

        [Theory]
        [InlineData("not json")]
        [InlineData("[1,2]")]
        public async Task UnreadableProblemBody_IsIgnored(string body)
        {
            var handler = new RecordingHandler { Status = HttpStatusCode.BadRequest, Body = body };
            using var client = new GamoireApiClient(Origin, "1.0.0", new TokenStore(directory), handler);

            var result = await client.PollPairingAsync("code", CancellationToken.None);

            Assert.Null(result.ProblemType);
            Assert.Null(result.Detail);
        }

        public void Dispose()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
