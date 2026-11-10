using Xunit;

namespace GamoireLaunchBox.Tests
{
    public class SyncClassifyTests
    {
        [Theory]
        [InlineData(204, false, SyncResult.Ok)]
        [InlineData(404, false, SyncResult.Terminal)]
        [InlineData(413, true, SyncResult.Terminal)]
        [InlineData(400, false, SyncResult.Drop)]
        [InlineData(400, true, SyncResult.Terminal)]
        [InlineData(409, true, SyncResult.Retry)]
        [InlineData(429, false, SyncResult.Retry)]
        [InlineData(426, true, SyncResult.Retry)]
        [InlineData(503, false, SyncResult.Retry)]
        [InlineData(ApiResponse.NetworkFailure, false, SyncResult.Retry)]
        [InlineData(ApiResponse.NotConnected, false, SyncResult.Stop)]
        public void Classifies(int status, bool full, SyncResult expected)
        {
            Assert.Equal(expected, SyncService.Classify(ApiResponse.Of(status), full));
        }

        [Fact]
        public void NotFound_IsTerminalOnStart_ButRetriedWhenTheRunIsGone()
        {
            Assert.Equal(SyncResult.Terminal, SyncService.Classify(ApiResponse.Of(404), full: true));

            var staleRun = ApiResponse.Of(404);
            staleRun.MidRun = true;

            Assert.Equal(SyncResult.Retry, SyncService.Classify(staleRun, full: true));
        }

        [Fact]
        public void TerminalNotice_NamesLaunchBox() =>
            Assert.Equal("LaunchBox sync is switched off for your account in Gamoire.", SyncService.TerminalNotice(ApiResponse.Of(404)));

        [Theory]
        [InlineData(1, 30)]
        [InlineData(2, 60)]
        [InlineData(6, 960)]
        [InlineData(7, 1800)]
        [InlineData(40, 1800)]
        public void Backoff_DoublesUpToThirtyMinutes(int failures, int seconds)
        {
            Assert.Equal(seconds, SyncService.BackoffFor(failures).TotalSeconds);
        }
    }
}
