using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace GamoireLaunchBox
{
    public static class GameMapper
    {
        public const int MaxName = 256;
        public const int MaxHint = 64;

        private static readonly Regex SteamId = new Regex(@"^[0-9]{1,20}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex SteamPath = new Regex(
            @"^steam://(?:rungameid|run)/([0-9]{1,20})(?:[/?#]|$)", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        /// <summary>Emulated games count only only with a sign of play or curation.</summary>
        public static bool ShouldSend(GameFacts g, bool skipHidden)
        {
            if (skipHidden && g.Hide) return false;
            if (!g.Emulated) return true;
            return g.PlayTimeSeconds > 0 || g.LastPlayed != null || g.Favorite
                || g.StarRatingFloat > 0 || g.Completed || !ProgressMap.IsUnstarted(g.Progress);
        }

        public static List<LauncherGameItem> Map(IEnumerable<GameFacts> games, bool skipHidden)
        {
            var items = new List<LauncherGameItem>();
            foreach (var game in games)
            {
                if (!ShouldSend(game, skipHidden)) continue;
                var item = ToItem(game);
                if (item != null) items.Add(item);
            }

            return items;
        }

        /// <summary>Null when the name is empty after cleaning: the server requires a name.</summary>
        public static LauncherGameItem? ToItem(GameFacts g)
        {
            var name = CleanName(g.Title);
            if (name == null) return null;

            var storefront = StorefrontMap.Key(g.StorefrontId, g.Source, g.ApplicationPath);
            var steamId = (storefront == "steam" ? SteamIdOf(g.StorefrontItemId) : null) ?? SteamIdFromPath(g.ApplicationPath);
            var year = g.ReleaseYear ?? g.ReleaseDate?.Year;

            return new LauncherGameItem
            {
                Id = g.Id,
                Name = name,
                PluginId = Guid.Empty,
                Emulated = g.Emulated,
                Storefront = storefront,
                SteamAppId = steamId,
                StoreGameId = storefront == "steam" ? steamId : null,
                ReleaseYear = year >= 1950 && year <= 2100 ? year : null,
                Platform = Hint(g.Platform),
                PlaytimeSeconds = g.PlayTimeSeconds,
                LastPlayedUtc = ToUtc(g.LastPlayed),
                ModifiedUtc = ToUtc(g.Modified),
                AddedUtc = ToUtc(g.Added),
                Status = ProgressMap.Status(g.Completed, g.Progress, g.PlayTimeSeconds),
                Favorite = g.Favorite,
                Score = ScoreOf(g.StarRatingFloat)
            };
        }

        /// <summary>Control, format, private-use and unassigned characters and unpaired surrogates removed, trimmed, clipped on a pair boundary.</summary>
        public static string? CleanName(string? text)
        {
            if (text == null) return null;
            var builder = new StringBuilder(text.Length);
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                    {
                        var category = CharUnicodeInfo.GetUnicodeCategory(text, i);
                        if (!IsStripped(category)) builder.Append(c).Append(text[i + 1]);
                        i++;
                    }

                    continue;
                }

                if (char.IsLowSurrogate(c)) continue;
                if (!IsStripped(CharUnicodeInfo.GetUnicodeCategory(c))) builder.Append(c);
            }

            var cleaned = Text.Clip(builder.ToString().Trim(), MaxName).Trim();
            return cleaned.Length == 0 ? null : cleaned;
        }

        private static int ScoreOf(float stars)
        {
            if (!(stars > 0)) return 0;
            return (int)Math.Clamp(Math.Round(stars * 20.0), 0, 100);
        }

        private static string? SteamIdOf(string? id)
        {
            var trimmed = id?.Trim();
            return trimmed != null && SteamId.IsMatch(trimmed) ? trimmed : null;
        }

        private static string? SteamIdFromPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            var match = SteamPath.Match(path.Trim());
            return match.Success ? match.Groups[1].Value : null;
        }

        private static string? Hint(string? text)
        {
            var cleaned = text?.Trim();
            if (string.IsNullOrEmpty(cleaned)) return null;
            if (cleaned.Any(char.IsControl)) return null;
            return Text.Clip(cleaned, MaxHint);
        }

        private static bool IsStripped(UnicodeCategory category) =>
            category == UnicodeCategory.Control || category == UnicodeCategory.Format
            || category == UnicodeCategory.PrivateUse || category == UnicodeCategory.OtherNotAssigned
            || category == UnicodeCategory.Surrogate;

        private static DateTime? ToUtc(DateTime? value)
        {
            if (value == null) return null;
            var date = value.Value;
            return date.Kind == DateTimeKind.Utc ? date : DateTime.SpecifyKind(date.ToUniversalTime(), DateTimeKind.Utc);
        }
    }
}
