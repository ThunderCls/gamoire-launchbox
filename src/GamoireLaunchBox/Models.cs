using System;
using System.Collections.Generic;

namespace GamoireLaunchBox
{
    public sealed class LauncherGameItem
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public Guid PluginId { get; set; }
        public bool Emulated { get; set; }
        public string? StoreGameId { get; set; }
        public string? SteamAppId { get; set; }
        public string? EpicSlug { get; set; }
        public int? ReleaseYear { get; set; }
        public string? Platform { get; set; }
        public long PlaytimeSeconds { get; set; }
        public DateTime? LastPlayedUtc { get; set; }
        public DateTime? ModifiedUtc { get; set; }
        public DateTime? AddedUtc { get; set; }
        public string? Status { get; set; }
        public bool Favorite { get; set; }
        public int? Score { get; set; }
        public string? Storefront { get; set; }
    }

    public sealed class LauncherBatchRequest
    {
        public int Batch { get; set; }
        public IReadOnlyList<LauncherGameItem> Games { get; set; } = [];
    }

    public sealed class SyncStartResponse
    {
        public Guid SyncId { get; set; }
    }

    public sealed class SyncFinishRequest
    {
        public int Batches { get; set; }
        public int Games { get; set; }
    }

    public sealed class PairStartRequest
    {
        public string ClientId { get; set; } = "";
        public string DeviceName { get; set; } = "";
        public string PluginVersion { get; set; } = "";
    }

    public sealed class PairStartResponse
    {
        public string DeviceCode { get; set; } = "";
        public string UserCode { get; set; } = "";
        public int Interval { get; set; }
        public int ExpiresIn { get; set; }
    }

    public sealed class TokenPairResponse
    {
        public string AccessToken { get; set; } = "";
        public string RefreshToken { get; set; } = "";
        public int ExpiresIn { get; set; }
        public string UserName { get; set; } = "";

        public bool IsComplete => !string.IsNullOrEmpty(AccessToken) && !string.IsNullOrEmpty(RefreshToken);

        public StoredTokens ToStored(string fallbackUserName) => new StoredTokens
        {
            AccessToken = AccessToken,
            RefreshToken = RefreshToken,
            AccessExpiresUtc = DateTime.UtcNow.AddSeconds(Math.Max(ExpiresIn, 60)),
            UserName = string.IsNullOrEmpty(UserName) ? fallbackUserName : UserName
        };
    }

    public sealed class StoredTokens
    {
        public string AccessToken { get; set; } = "";
        public string RefreshToken { get; set; } = "";
        public DateTime AccessExpiresUtc { get; set; }
        public string UserName { get; set; } = "";
    }
}
