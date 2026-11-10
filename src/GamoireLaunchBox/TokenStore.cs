using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GamoireLaunchBox
{
    /// <summary>One DPAPI-protected file per server origin, so tokens never travel to another server.</summary>
    public sealed class TokenStore(string directory)
    {
        public string PathFor(Uri origin)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(UrlRules.OriginKey(origin)));
            return Path.Combine(directory, "tokens-" + Convert.ToHexString(hash, 0, 8).ToLowerInvariant() + ".bin");
        }

        public void Save(Uri origin, StoredTokens tokens)
        {
            Directory.CreateDirectory(directory);
            var json = JsonSerializer.Serialize(tokens, GamoireApiClient.Json);
            var protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(json), Entropy(origin), DataProtectionScope.CurrentUser);
            var path = PathFor(origin);
            var temp = path + ".tmp";
            File.WriteAllBytes(temp, protectedBytes);
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }

        /// <summary>Null when missing or unreadable (another PC or user, damaged file): the plugin is then not connected.</summary>
        public StoredTokens? Load(Uri origin)
        {
            try
            {
                var path = PathFor(origin);
                if (!File.Exists(path)) return null;
                var bytes = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy(origin), DataProtectionScope.CurrentUser);
                var tokens = JsonSerializer.Deserialize<StoredTokens>(Encoding.UTF8.GetString(bytes), GamoireApiClient.Json);
                return string.IsNullOrEmpty(tokens?.RefreshToken) || string.IsNullOrEmpty(tokens.AccessToken) ? null : tokens;
            }
            catch (Exception ex) when (ex is CryptographicException || ex is IOException || ex is JsonException || ex is UnauthorizedAccessException)
            {
                return null;
            }
        }

        public void Delete(Uri origin)
        {
            try
            {
                var path = PathFor(origin);
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }

        private static byte[] Entropy(Uri origin) => Encoding.UTF8.GetBytes("gamoire-launcher|" + UrlRules.OriginKey(origin));
    }
}
