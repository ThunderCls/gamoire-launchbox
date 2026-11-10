using System;
using System.Linq;
using Xunit;

namespace GamoireLaunchBox.Tests
{
    public class GameMapperTests
    {
        public static GameFacts Facts(
            string title = "Game", bool emulated = false, string? storefrontId = null, string? itemId = null,
            string? source = null, string? path = null, int playTime = 0, DateTime? lastPlayed = null, bool completed = false,
            bool favorite = false, float stars = 0, string? progress = null, bool hide = false, string id = "g1") =>
            new GameFacts(id, title, "Windows", emulated, storefrontId, itemId, source, path, null, null, playTime,
                lastPlayed, null, null, completed, favorite, stars, progress, hide);

        [Fact]
        public void Steam_GameCarriesStoreIds()
        {
            var item = GameMapper.ToItem(Facts("Skyrim", storefrontId: "Steam", itemId: "489830"))!;

            Assert.Equal("steam", item.Storefront);
            Assert.Equal("489830", item.SteamAppId);
            Assert.Equal("489830", item.StoreGameId);
            Assert.Equal(Guid.Empty, item.PluginId);
        }

        [Fact]
        public void Epic_GameHasNoStoreIds()
        {
            var item = GameMapper.ToItem(Facts("Hogwarts", storefrontId: "epic", itemId: "fa4240e5abcdef"))!;

            Assert.Equal("epic", item.Storefront);
            Assert.Null(item.SteamAppId);
            Assert.Null(item.StoreGameId);
            Assert.Null(item.EpicSlug);
        }

        [Theory]
        [InlineData("steam://rungameid/220")]
        [InlineData("steam://run/220")]
        public void SteamIdIsReadFromTheLaunchUrl(string path)
        {
            var item = GameMapper.ToItem(Facts(path: path))!;

            Assert.Equal("steam", item.Storefront);
            Assert.Equal("220", item.SteamAppId);
            Assert.Equal("220", item.StoreGameId);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("  ")]
        [InlineData("not started / unplayed")]
        public void EmulatedGameWithNothingIsNotSent(string? progress)
        {
            Assert.False(GameMapper.ShouldSend(Facts(emulated: true, progress: progress), false));
        }

        [Fact]
        public void EmulatedGameWithASignIsSent()
        {
            Assert.True(GameMapper.ShouldSend(Facts(emulated: true, favorite: true), false));
            Assert.True(GameMapper.ShouldSend(Facts(emulated: true, playTime: 1), false));
            Assert.True(GameMapper.ShouldSend(Facts(emulated: true, lastPlayed: DateTime.UtcNow), false));
            Assert.True(GameMapper.ShouldSend(Facts(emulated: true, stars: 1), false));
            Assert.True(GameMapper.ShouldSend(Facts(emulated: true, completed: true), false));
            Assert.True(GameMapper.ShouldSend(Facts(emulated: true, progress: "Active / Paused"), false));
        }

        [Fact]
        public void StoreGameIsSentEvenUnplayed() =>
            Assert.True(GameMapper.ShouldSend(Facts(storefrontId: "steam"), false));

        [Fact]
        public void HiddenGamesAreSkippedOnlyWhenAsked()
        {
            Assert.False(GameMapper.ShouldSend(Facts(hide: true), true));
            Assert.True(GameMapper.ShouldSend(Facts(hide: true), false));
        }

        [Fact]
        public void RomSetFlood_SendsNothing_ButStoreGamesStay()
        {
            var progress = new string?[] { null, "", "   ", "Not Started / Unplayed" };
            var roms = Enumerable.Range(0, 20000).Select(i => Facts(emulated: true, progress: progress[i % 4], id: "r" + i)).ToList();
            var store = Enumerable.Range(0, 3).Select(i => Facts(storefrontId: "steam", id: "s" + i));

            Assert.Empty(GameMapper.Map(roms, false));
            Assert.Equal(3, GameMapper.Map(roms.Concat(store), false).Count);
        }

        [Theory]
        [InlineData(4.5f, 90)]
        [InlineData(4f, 80)]
        [InlineData(0f, 0)]
        [InlineData(9f, 100)]
        [InlineData(float.NaN, 0)]
        public void StarsBecomeAScoreOutOfOneHundred(float stars, int score) =>
            Assert.Equal(score, GameMapper.ToItem(Facts(stars: stars))!.Score);

        [Theory]
        [InlineData(false, "Not Started / Unplayed", 0, "Not Played")]
        [InlineData(false, "Not Started / Won't Play", 0, "Not Played")]
        [InlineData(false, "Not Started / Want to Play", 0, "Plan to Play")]
        [InlineData(false, "Active / In Progress", 0, "Playing")]
        [InlineData(false, "Active / Continuous", 0, "Playing")]
        [InlineData(false, "Active / Paused", 0, "On Hold")]
        [InlineData(false, "Done / Beaten", 0, "Beaten")]
        [InlineData(false, "Done / Completed", 0, "Completed")]
        [InlineData(false, "Done / Mastered", 0, "Completed")]
        [InlineData(false, "Done / Dropped", 0, "Abandoned")]
        [InlineData(false, "something else", 10, "Played")]
        [InlineData(false, null, 0, "Not Played")]
        [InlineData(true, "Active / Paused", 0, "Completed")]
        public void ProgressMapsToAStatus(bool completed, string? progress, int seconds, string expected) =>
            Assert.Equal(expected, ProgressMap.Status(completed, progress, seconds));

        [Theory]
        [InlineData("Steam", null, "steam")]
        [InlineData(null, "Epic Games", "epic")]
        [InlineData(null, "Origin", "ea")]
        [InlineData(null, "uplay", "ubisoft")]
        [InlineData("GOG", null, "gog")]
        [InlineData("weird", null, "other")]
        [InlineData(null, "Somewhere", "other")]
        [InlineData(null, null, null)]
        public void StorefrontKey(string? id, string? source, string? expected) =>
            Assert.Equal(expected, StorefrontMap.Key(id, source, null));

        [Fact]
        public void EpicLaunchSchemeIsRecognised() =>
            Assert.Equal("epic", StorefrontMap.Key(null, null, "com.epicgames.launcher://apps/x?action=launch"));

        [Fact]
        public void LocalTimesArriveAsUtc()
        {
            var local = new DateTime(2025, 6, 1, 12, 0, 0, DateTimeKind.Local);

            var item = GameMapper.ToItem(Facts(lastPlayed: local))!;

            Assert.Equal(DateTimeKind.Utc, item.LastPlayedUtc!.Value.Kind);
            Assert.Equal(local.ToUniversalTime(), item.LastPlayedUtc);
        }

        [Fact]
        public void NameIsCleanedAndEmptyNamesDropped()
        {
            Assert.Equal("Doom", GameMapper.ToItem(Facts("  Do\u0000om​ "))!.Name);
            Assert.Null(GameMapper.ToItem(Facts("\u0000 ")));
        }
    }
}
