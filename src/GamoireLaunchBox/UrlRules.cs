using System;
using System.Diagnostics.CodeAnalysis;

namespace GamoireLaunchBox
{
    public static class UrlRules
    {
        public const string DefaultServer = "https://gamoire.app";

        public static bool TryGetOrigin(string? input, [NotNullWhen(true)] out Uri? origin)
        {
            origin = null;
            if (string.IsNullOrWhiteSpace(input)) return false;
            if (!Uri.TryCreate(input.Trim(), UriKind.Absolute, out var uri)) return false;
            if (!string.IsNullOrEmpty(uri.UserInfo)) return false;

            var allowed = uri.Scheme == Uri.UriSchemeHttps
                || (uri.Scheme == Uri.UriSchemeHttp && IsLoopbackHost(uri.Host));
            if (!allowed) return false;

            origin = new Uri(uri.GetLeftPart(UriPartial.Authority));
            return true;
        }

        public static bool IsLoopbackHost(string host) =>
            host == "localhost" || host == "127.0.0.1" || host == "[::1]" || host == "::1";

        public const string InvalidServerMessage = "The server URL must start with https:// (http:// is allowed only for localhost).";

        /// <summary>Why Connect cannot run for these typed and saved server URLs, or null when it can.</summary>
        public static string? ConnectBlock(string? typed, string? saved)
        {
            if (!TryGetOrigin(typed, out var typedOrigin) || !TryGetOrigin(saved, out var savedOrigin)) return InvalidServerMessage;
            return OriginKey(typedOrigin) != OriginKey(savedOrigin)
                ? "Save, then open these settings again and choose Connect."
                : null;
        }

        public static string OriginKey(Uri origin) =>
            origin.Scheme + "://" + origin.Authority.ToLowerInvariant();

        public static Uri Endpoint(Uri origin, string path) => new Uri(origin, "/api/v1/" + path);

        public static Uri PairingPage(Uri origin) => new Uri(origin, "/connect/launcher");

        public static Uri IntegrationsPage(Uri origin) => new Uri(origin, "/profile?tab=integrations");
    }
}
