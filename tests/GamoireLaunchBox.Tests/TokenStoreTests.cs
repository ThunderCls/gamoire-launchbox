using System;
using System.IO;
using Xunit;

namespace GamoireLaunchBox.Tests
{
    public sealed class TokenStoreTests : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "gamoire-tokens-" + Guid.NewGuid().ToString("N"));

        private static StoredTokens Tokens(string tag) => new StoredTokens
        {
            AccessToken = "gma_" + tag,
            RefreshToken = "gmr_" + tag,
            AccessExpiresUtc = DateTime.UtcNow.AddHours(1),
            UserName = "user-" + tag
        };

        [Fact]
        public void KeyedByOrigin()
        {
            var store = new TokenStore(directory);
            var a = new Uri("https://a.example.com");
            var b = new Uri("http://localhost:8080");

            store.Save(a, Tokens("a"));
            store.Save(b, Tokens("b"));

            Assert.NotEqual(store.PathFor(a), store.PathFor(b));
            Assert.Equal(2, Directory.GetFiles(directory).Length);
            Assert.Equal("gmr_a", store.Load(a)!.RefreshToken);
            Assert.Equal("gmr_b", store.Load(b)!.RefreshToken);

            store.Delete(a);

            Assert.Null(store.Load(a));
            Assert.Equal("gmr_b", store.Load(b)!.RefreshToken);
            Assert.Single(Directory.GetFiles(directory));
        }

        [Fact]
        public void FileIsEncrypted_AndDamagedFileMeansNotConnected()
        {
            var store = new TokenStore(directory);
            var origin = new Uri("https://a.example.com");
            store.Save(origin, Tokens("a"));

            Assert.DoesNotContain("gmr_a", File.ReadAllText(store.PathFor(origin)));

            File.WriteAllBytes(store.PathFor(origin), new byte[] { 1, 2, 3, 4 });

            Assert.Null(store.Load(origin));
        }

        [Fact]
        public void OriginKey_IsCaseInsensitiveOnHost()
        {
            var store = new TokenStore(directory);
            store.Save(new Uri("https://A.Example.com"), Tokens("a"));

            Assert.NotNull(store.Load(new Uri("https://a.example.com")));
        }

        public void Dispose()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
