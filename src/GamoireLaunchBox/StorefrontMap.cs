using System;
using System.Collections.Generic;

namespace GamoireLaunchBox
{
    public static class StorefrontMap
    {
        private static readonly HashSet<string> Keys = new HashSet<string>(StringComparer.Ordinal)
        {
            "steam", "epic", "gog", "ea", "amazon", "ubisoft", "battlenet", "xbox", "itch", "humble", "rockstar"
        };

        private static readonly Dictionary<string, string> Sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Steam"] = "steam",
            ["Epic Games"] = "epic",
            ["GOG"] = "gog",
            ["EA"] = "ea",
            ["Origin"] = "ea",
            ["Amazon"] = "amazon",
            ["Ubisoft Connect"] = "ubisoft",
            ["Uplay"] = "ubisoft",
            ["Battle.net"] = "battlenet",
            ["Xbox"] = "xbox",
            ["itch.io"] = "itch",
            ["Humble"] = "humble",
            ["Rockstar"] = "rockstar"
        };

        public static string? Key(string? storefrontId, string? source, string? applicationPath)
        {
            var id = storefrontId?.Trim() ?? "";
            var lowered = id.ToLowerInvariant();
            if (Keys.Contains(lowered)) return lowered;

            var name = source?.Trim() ?? "";
            if (Sources.TryGetValue(name, out var fromSource)) return fromSource;

            var path = applicationPath?.Trim() ?? "";
            if (path.StartsWith("steam://", StringComparison.OrdinalIgnoreCase)) return "steam";
            if (path.StartsWith("com.epicgames.launcher://", StringComparison.OrdinalIgnoreCase)) return "epic";

            return id.Length > 0 || name.Length > 0 ? "other" : null;
        }
    }
}
