using System;
using System.Collections.Generic;

namespace GamoireLaunchBox
{
    public static class ProgressMap
    {
        private const string Unplayed = "Not Started / Unplayed";

        private static readonly Dictionary<string, string> Statuses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Unplayed] = "Not Played",
            ["Not Started / Won't Play"] = "Not Played",
            ["Not Started / Want to Play"] = "Plan to Play",
            ["Active / In Progress"] = "Playing",
            ["Active / Continuous"] = "Playing",
            ["Active / Paused"] = "On Hold",
            ["Done / Beaten"] = "Beaten",
            ["Done / Completed"] = "Completed",
            ["Done / Mastered"] = "Completed",
            ["Done / Dropped"] = "Abandoned"
        };

        public static bool IsUnstarted(string? progress) =>
            string.IsNullOrWhiteSpace(progress) || string.Equals(progress.Trim(), Unplayed, StringComparison.OrdinalIgnoreCase);

        public static string Status(bool completed, string? progress, int playTimeSeconds)
        {
            if (completed) return "Completed";
            if (progress != null && Statuses.TryGetValue(progress.Trim(), out var status)) return status;
            return playTimeSeconds > 0 ? "Played" : "Not Played";
        }
    }
}
