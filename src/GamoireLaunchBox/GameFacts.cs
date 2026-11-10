using System;

namespace GamoireLaunchBox
{
    public sealed record GameFacts(
        string Id, string Title, string Platform, bool Emulated, string? StorefrontId,
        string? StorefrontItemId, string? Source, string? ApplicationPath, int? ReleaseYear, DateTime? ReleaseDate,
        int PlayTimeSeconds, DateTime? LastPlayed, DateTime? Added, DateTime? Modified, bool Completed, bool Favorite,
        float StarRatingFloat, string? Progress, bool Hide);
}
