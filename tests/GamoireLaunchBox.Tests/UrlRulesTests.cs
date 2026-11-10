using System;
using System.IO;
using Xunit;

namespace GamoireLaunchBox.Tests
{
    public class UrlRulesTests
    {
        [Theory]
        [InlineData("http://localhost:8080", true)]
        [InlineData("http://127.0.0.1:8080", true)]
        [InlineData("http://[::1]:8080", true)]
        [InlineData("https://example.com", true)]
        [InlineData("https://gamoire.app/", true)]
        [InlineData("http://localhost.evil.com", false)]
        [InlineData("http://192.168.1.5", false)]
        [InlineData("http://example.com", false)]
        [InlineData("https://user:pass@example.com", false)]
        [InlineData("ftp://example.com", false)]
        [InlineData("", false)]
        [InlineData("gamoire.app", false)]
        public void Origin_Rules(string url, bool allowed)
        {
            Assert.Equal(allowed, UrlRules.TryGetOrigin(url, out _));
        }

        [Theory]
        [InlineData("http://example.com", "https://gamoire.app", UrlRules.InvalidServerMessage)]
        [InlineData("https://gamoire.app", "http://example.com", UrlRules.InvalidServerMessage)]
        [InlineData("https://other.example", "https://gamoire.app", "Save, then open these settings again and choose Connect.")]
        [InlineData("https://GAMOIRE.app/", "https://gamoire.app", null)]
        public void ConnectBlock_ChecksInvalidBeforeUnsaved(string typed, string saved, string? expected)
        {
            Assert.Equal(expected, UrlRules.ConnectBlock(typed, saved));
        }

        [Fact]
        public void PairingPage_IsBuiltOnTheConfiguredOrigin()
        {
            UrlRules.TryGetOrigin("https://gamoire.app/some/path?x=1", out var origin);
            Assert.NotNull(origin);

            Assert.Equal("https://gamoire.app/connect/launcher", UrlRules.PairingPage(origin).AbsoluteUri);
            Assert.Equal("https://gamoire.app/api/v1/launcher-sync/games", UrlRules.Endpoint(origin, "launcher-sync/games").AbsoluteUri);
        }

        [Fact]
        public void ApiClient_RefusesInsecureOrigins()
        {
            var store = new TokenStore(Path.GetTempPath());
            Assert.Throws<ArgumentException>(() => new GamoireApiClient(new Uri("http://example.com"), "1.0.0", store));
        }
    }
}
