using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GamoireLaunchBox
{
    public sealed class ApiResponse
    {
        public const int NetworkFailure = 0;
        public const int NotConnected = -1;

        public int Status { get; set; }
        public string? ProblemType { get; set; }
        public string? Detail { get; set; }
        public string? Body { get; set; }

        /// <summary>Set on a batch or finish answer: a 404 there means the run is gone, not that access is off.</summary>
        public bool MidRun { get; set; }

        public bool IsSuccess => Status >= 200 && Status < 300;

        /// <summary>No answer or a server error: worth retrying.</summary>
        public bool IsTransient => Status == NetworkFailure || Status >= 500;

        public bool Is(string problem) => ProblemType != null && ProblemType.EndsWith("/" + problem, StringComparison.Ordinal);

        public static ApiResponse Of(int status) => new ApiResponse { Status = status };
    }

    public sealed class RefreshResult
    {
        public RefreshOutcome Outcome { get; set; }
        public ApiResponse? Failure { get; set; }

        public static RefreshResult Of(RefreshOutcome outcome, ApiResponse? failure = null) =>
            new RefreshResult { Outcome = outcome, Failure = failure };
    }

    public enum RefreshOutcome
    {
        Refreshed,
        Rejected,
        Failed
    }

    /// <summary>Talks to one configured origin: no redirects, https (or loopback http) only, single-flight token refresh.</summary>
    public sealed class GamoireApiClient : IDisposable
    {
        public const string ClientId = "launchbox";
        public const string VersionHeader = "X-Launcher-Version";
        public const string ClientHeader = "X-Launcher-Client";
        public const string UserAgentProduct = "Gamoire-LaunchBox";

        public static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
            PropertyNameCaseInsensitive = true,
            MaxDepth = 32
        };

        private static readonly TimeSpan RefreshAttemptTimeout = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan[] RetryDelays = { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4) };

        private readonly Func<TimeSpan, CancellationToken, Task> delay;
        private readonly HttpClient http;
        private readonly TokenStore tokens;
        private readonly SemaphoreSlim refreshGate = new SemaphoreSlim(1, 1);

        public GamoireApiClient(
            Uri origin, string pluginVersion, TokenStore tokens, HttpMessageHandler? handler = null,
            Func<TimeSpan, CancellationToken, Task>? delay = null)
        {
            if (!UrlRules.TryGetOrigin(origin.ToString(), out var checkedOrigin))
                throw new ArgumentException("The server URL must use https (http is allowed only for localhost).", nameof(origin));
            Origin = checkedOrigin;
            this.tokens = tokens;
            this.delay = delay ?? Task.Delay;
            http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
            {
                Timeout = TimeSpan.FromSeconds(60),
                MaxResponseContentBufferSize = 1024 * 1024
            };
            http.DefaultRequestHeaders.Add(VersionHeader, pluginVersion);
            http.DefaultRequestHeaders.Add(ClientHeader, ClientId);
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(UserAgentProduct, pluginVersion));
        }

        public Uri Origin { get; }

        /// <summary>Raised when the server rejects the refresh token and the local tokens are gone.</summary>
        public event Action? Disconnected;

        public Task<ApiResponse> StartPairingAsync(string deviceName, string pluginVersion, CancellationToken ct) =>
            SendAsync(HttpMethod.Post, "launcher-pairing/start",
                new PairStartRequest { ClientId = ClientId, DeviceName = deviceName, PluginVersion = pluginVersion }, null, ct);

        public Task<ApiResponse> PollPairingAsync(string deviceCode, CancellationToken ct) =>
            SendAsync(HttpMethod.Post, "launcher-pairing/token", new { deviceCode }, null, ct);

        public Task<ApiResponse> StartSyncAsync(CancellationToken ct) =>
            SendAuthedAsync(HttpMethod.Post, "launcher-sync/sync/start", new { }, ct);

        public Task<ApiResponse> SendBatchAsync(Guid syncId, LauncherBatchRequest batch, CancellationToken ct) =>
            SendAuthedAsync(HttpMethod.Post, "launcher-sync/sync/" + syncId.ToString("D") + "/batch", batch, ct);

        public Task<ApiResponse> FinishSyncAsync(Guid syncId, SyncFinishRequest finish, CancellationToken ct) =>
            SendAuthedAsync(HttpMethod.Post, "launcher-sync/sync/" + syncId.ToString("D") + "/finish", finish, ct);

        public Task<ApiResponse> UpsertGameAsync(LauncherGameItem item, CancellationToken ct) =>
            SendAuthedAsync(HttpMethod.Post, "launcher-sync/games", item, ct);

        public Task<ApiResponse> DisconnectDeviceAsync(CancellationToken ct) =>
            SendAuthedAsync(HttpMethod.Delete, "launcher-sync/device", null, ct);

        public static T? Parse<T>(ApiResponse response) where T : class
        {
            try
            {
                return JsonSerializer.Deserialize<T>(response.Body ?? "", Json);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        public async Task<ApiResponse> SendAuthedAsync(HttpMethod method, string path, object? body, CancellationToken ct)
        {
            var current = tokens.Load(Origin);
            if (current == null) return ApiResponse.Of(ApiResponse.NotConnected);

            if (current.AccessExpiresUtc <= DateTime.UtcNow.AddSeconds(60))
            {
                var refreshed = await RefreshAsync(current.AccessToken, ct).ConfigureAwait(false);
                if (refreshed.Outcome == RefreshOutcome.Rejected) return ApiResponse.Of(ApiResponse.NotConnected);
                if (refreshed.Outcome == RefreshOutcome.Failed) return refreshed.Failure ?? ApiResponse.Of(ApiResponse.NetworkFailure);
                current = tokens.Load(Origin);
                if (current == null) return ApiResponse.Of(ApiResponse.NotConnected);
            }

            var response = await SendAsync(method, path, body, current.AccessToken, ct).ConfigureAwait(false);
            if (response.Status != 401) return response;

            var retried = await RefreshAsync(current.AccessToken, ct).ConfigureAwait(false);
            if (retried.Outcome == RefreshOutcome.Rejected) return ApiResponse.Of(ApiResponse.NotConnected);
            if (retried.Outcome == RefreshOutcome.Failed) return retried.Failure ?? ApiResponse.Of(ApiResponse.NetworkFailure);
            var renewed = tokens.Load(Origin);
            return renewed == null
                ? ApiResponse.Of(ApiResponse.NotConnected)
                : await SendAsync(method, path, body, renewed.AccessToken, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Single-flight. A caller whose access token is no longer the stored one finds it already refreshed and reuses it.
        /// The new pair is written to disk before anyone uses it.
        /// </summary>
        public async Task<RefreshResult> RefreshAsync(string staleAccessToken, CancellationToken ct)
        {
            await refreshGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var stored = tokens.Load(Origin);
                if (stored == null) return RefreshResult.Of(RefreshOutcome.Rejected, null);
                if (stored.AccessToken != staleAccessToken && stored.AccessExpiresUtc > DateTime.UtcNow.AddSeconds(30))
                    return RefreshResult.Of(RefreshOutcome.Refreshed, null);

                // A lost answer must be retried inside the server's 60 s grace for the previous refresh token:
                // three attempts of at most 15 s with 2 s and 4 s pauses finish within about 51 s.
                var response = await RetryTransientAsync(
                    () => SendAsync(HttpMethod.Post, "launcher-pairing/refresh", new { refreshToken = stored.RefreshToken }, null, ct, RefreshAttemptTimeout), ct)
                    .ConfigureAwait(false);

                if (response.IsSuccess)
                {
                    var pair = Parse<TokenPairResponse>(response);
                    if (pair == null || !pair.IsComplete) return RefreshResult.Of(RefreshOutcome.Failed, response);
                    tokens.Save(Origin, pair.ToStored(stored.UserName));
                    return RefreshResult.Of(RefreshOutcome.Refreshed, null);
                }

                if (response.Status == 401)
                {
                    tokens.Delete(Origin);
                    Disconnected?.Invoke();
                    return RefreshResult.Of(RefreshOutcome.Rejected, null);
                }

                return RefreshResult.Of(RefreshOutcome.Failed, response);
            }
            finally
            {
                refreshGate.Release();
            }
        }

        /// <summary>Up to three attempts, pausing 2 s then 4 s after a transient failure.</summary>
        public async Task<ApiResponse> RetryTransientAsync(Func<Task<ApiResponse>> attempt, CancellationToken ct)
        {
            var response = await attempt().ConfigureAwait(false);
            for (var tries = 0; tries < RetryDelays.Length && response.IsTransient; tries++)
            {
                await delay(RetryDelays[tries], ct).ConfigureAwait(false);
                response = await attempt().ConfigureAwait(false);
            }

            return response;
        }

        private async Task<ApiResponse> SendAsync(
            HttpMethod method, string path, object? body, string? bearer, CancellationToken ct, TimeSpan? timeout = null)
        {
            try
            {
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(ct))
                using (var request = new HttpRequestMessage(method, UrlRules.Endpoint(Origin, path)))
                {
                    if (timeout != null) linked.CancelAfter(timeout.Value);
                    if (bearer != null) request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + bearer);
                    if (body != null)
                        request.Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json");

                    using (var response = await http.SendAsync(request, linked.Token).ConfigureAwait(false))
                    {
                        var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        var result = new ApiResponse { Status = (int)response.StatusCode, Body = text };
                        if (!result.IsSuccess) ReadProblem(result);
                        return result;
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is OperationCanceledException || ex is InvalidOperationException)
            {
                return ApiResponse.Of(ApiResponse.NetworkFailure);
            }
        }

        private static void ReadProblem(ApiResponse result)
        {
            try
            {
                using var document = JsonDocument.Parse(result.Body ?? "", new JsonDocumentOptions { MaxDepth = 32 });
                if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("Not an object.");
                result.ProblemType = ReadText(document.RootElement, "type");
                result.Detail = PlainText(ReadText(document.RootElement, "detail"));
            }
            catch (JsonException)
            {
                result.Detail = result.Status == 426 ? PlainText(result.Body) : null;
            }
        }

        private static string? ReadText(JsonElement problem, string name)
        {
            foreach (var property in problem.EnumerateObject())
            {
                if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                return property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => property.Value.GetRawText(),
                    _ => null
                };
            }

            return null;
        }

        public static string? PlainText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var builder = new StringBuilder();
            foreach (var c in text)
                if (!char.IsControl(c)) builder.Append(c);
            var cleaned = builder.ToString().Trim();
            return cleaned.Length == 0 ? null : Text.Clip(cleaned, 200);
        }

        public void Dispose()
        {
            http.Dispose();
            refreshGate.Dispose();
        }
    }
}
