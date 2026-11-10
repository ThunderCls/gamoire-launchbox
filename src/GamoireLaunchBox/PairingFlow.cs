using System;
using System.Threading;
using System.Threading.Tasks;

namespace GamoireLaunchBox
{
    public static class PairingFlow
    {
        public const string DeniedNotice = "Pairing was denied in Gamoire.";
        public const string ExpiredNotice = "The pairing code expired. Connect again.";

        /// <summary>Returns true when tokens were saved. The browser URL is built from the configured origin only.</summary>
        public static async Task<bool> RunAsync(
            GamoireApiClient client, TokenStore tokens, GamoireState state, string pluginVersion,
            Action<Uri> openBrowser, CancellationToken ct, Func<TimeSpan, CancellationToken, Task>? delay = null)
        {
            delay ??= Task.Delay;
            state.BeginPairing();

            var start = await client.StartPairingAsync(Environment.MachineName, pluginVersion, ct).ConfigureAwait(false);
            if (!start.IsSuccess)
            {
                state.EndPairing(StartFailure(start));
                return false;
            }

            var pair = GamoireApiClient.Parse<PairStartResponse>(start);
            if (pair == null || string.IsNullOrEmpty(pair.DeviceCode) || string.IsNullOrEmpty(pair.UserCode))
            {
                state.EndPairing("Unexpected answer from the server.");
                return false;
            }

            state.ShowPairingCode(GamoireState.FormatCode(pair.UserCode));
            try
            {
                openBrowser(UrlRules.PairingPage(client.Origin));
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception)
            {
                state.SetNotice("Open " + UrlRules.PairingPage(client.Origin) + " in your browser and type the code.");
            }

            var interval = TimeSpan.FromSeconds(Math.Max(pair.Interval, 1));
            var deadline = DateTime.UtcNow.AddSeconds(pair.ExpiresIn > 0 ? pair.ExpiresIn : 600);

            while (true)
            {
                await delay(interval, ct).ConfigureAwait(false);
                if (DateTime.UtcNow > deadline)
                {
                    state.EndPairing(ExpiredNotice);
                    return false;
                }

                var poll = await client.PollPairingAsync(pair.DeviceCode, ct).ConfigureAwait(false);
                if (poll.IsSuccess)
                {
                    var granted = GamoireApiClient.Parse<TokenPairResponse>(poll);
                    if (granted == null || !granted.IsComplete)
                    {
                        state.EndPairing("Unexpected answer from the server.");
                        return false;
                    }

                    var stored = granted.ToStored("");
                    tokens.Save(client.Origin, stored);
                    state.SetConnected(stored.UserName);
                    return true;
                }

                if (poll.Is("authorization-pending") || poll.IsTransient) continue;
                if (poll.Is("slow-down") || poll.Status == 429)
                {
                    interval += TimeSpan.FromSeconds(5);
                    continue;
                }

                if (poll.Is("access-denied"))
                {
                    state.EndPairing(DeniedNotice);
                    return false;
                }

                if (poll.Is("expired-token"))
                {
                    state.EndPairing(ExpiredNotice);
                    return false;
                }

                state.EndPairing(poll.Status == 426 ? Outdated(poll) : "Pairing failed. Connect again.");
                return false;
            }
        }

        public static string Outdated(ApiResponse response) =>
            response.Detail ?? "Update the Gamoire plugin in LaunchBox.";

        private static string StartFailure(ApiResponse response)
        {
            if (response.Status == 426) return Outdated(response);
            if (response.Status == 429) return "Too many attempts. Try again in a few minutes.";
            if (response.Status == ApiResponse.NetworkFailure) return "Can't reach the server.";
            return response.Detail ?? "Couldn't start pairing (error " + response.Status + "). Try again.";
        }
    }
}
