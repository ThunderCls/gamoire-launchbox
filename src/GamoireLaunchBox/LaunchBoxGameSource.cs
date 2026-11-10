using System;
using System.Collections.Generic;
using System.Linq;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Components.Storefronts;
using Unbroken.LaunchBox.Plugins.Data;

namespace GamoireLaunchBox
{
    public sealed class LaunchBoxGameSource : IGameSource
    {
        public IReadOnlyList<GameFacts> All()
        {
            var data = PluginHelper.DataManager;
            var platforms = new Dictionary<string, IPlatform>(StringComparer.OrdinalIgnoreCase);
            foreach (var platform in data.GetAllPlatforms())
            {
                if (!string.IsNullOrEmpty(platform.Name)) platforms[platform.Name] = platform;
            }

            return data.GetAllGames()
                .Select(game => Facts(game, game.Platform != null && platforms.TryGetValue(game.Platform, out var p) ? p : null))
                .ToList();
        }

        public GameFacts? ById(string id)
        {
            var data = PluginHelper.DataManager;
            var game = data.GetGameById(id);
            if (game == null) return null;
            return Facts(game, string.IsNullOrEmpty(game.Platform) ? null : data.GetPlatformByName(game.Platform));
        }

        private static GameFacts Facts(IGame game, IPlatform? platform)
        {
            var storefront = (game as IStorefrontItem)?.Storefront;
#pragma warning disable CS0618 // legacy flag, still set by older libraries
            var completed = game.Completed;
#pragma warning restore CS0618
            return new GameFacts(
                game.Id, game.Title ?? "", game.Platform ?? "",
                platform?.IsEmulated == true || !string.IsNullOrEmpty(game.EmulatorId),
                storefront?.StorefrontId, storefront?.ItemId, game.Source, game.ApplicationPath,
                game.ReleaseYear, game.ReleaseDate, game.PlayTime, game.LastPlayedDate, game.DateAdded, game.DateModified,
                completed, game.Favorite, game.StarRatingFloat, game.Progress, game.Hide);
        }
    }
}
