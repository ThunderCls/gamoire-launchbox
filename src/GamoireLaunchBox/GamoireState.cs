using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GamoireLaunchBox
{
    public abstract class ObservableBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>What the settings view shows; never persisted.</summary>
    public sealed class GamoireState : ObservableBase
    {
        private bool connected;
        private bool disconnectedByServer;
        private bool pairing;
        private string? userName;
        private string? pairingCode;
        private string? notice;
        private DateTime? lastSyncUtc;
        private int? gameCount;
        private SyncProgress? syncProgress;

        /// <summary>A running full sync; Total is null until the games are counted.</summary>
        private sealed class SyncProgress
        {
            public int Sent { get; set; }

            public int? Total { get; set; }
        }

        public bool IsConnected => connected;

        public bool IsPairing => pairing;

        public bool CanConnect => !connected;

        public bool IsSyncing => syncProgress != null;

        public bool CanSyncNow => connected && !IsSyncing;

        public bool HasPairingCode => !string.IsNullOrEmpty(pairingCode);

        public string? PairingCode => pairingCode;

        public string? Notice => notice;

        public bool HasNotice => !string.IsNullOrEmpty(notice);

        public string StatusLine
        {
            get
            {
                if (pairing) return "Waiting for you to approve this PC in Gamoire";
                if (disconnectedByServer) return "Disconnected by Gamoire — Connect again";
                if (!connected) return "Not connected";
                var line = "Connected as @" + userName + " on this PC";
                var progress = syncProgress;
                if (progress != null)
                {
                    return progress.Total is int total
                        ? line + " · syncing, sent " + progress.Sent.ToString("N0") + " of " + total.ToString("N0") + " games"
                        : line + " · syncing…";
                }

                if (lastSyncUtc != null)
                    line += " · last synced " + Ago(DateTime.UtcNow - lastSyncUtc.Value) + " (" + (gameCount ?? 0).ToString("N0") + " games)";
                return line;
            }
        }

        public void Touch() => Changed();

        public void SetConnected(string? user)
        {
            connected = true;
            disconnectedByServer = false;
            ResetPairing();
            userName = user;
            notice = null;
            Changed();
        }

        public void SetNotConnected()
        {
            connected = false;
            disconnectedByServer = false;
            ResetPairing();
            ClearConnection();
            Changed();
        }

        public void SetDisconnectedByServer()
        {
            ClearConnection();
            disconnectedByServer = true;
            ResetPairing();
            Changed();
        }

        public void BeginPairing()
        {
            pairing = true;
            pairingCode = null;
            notice = null;
            Changed();
        }

        public void ShowPairingCode(string code)
        {
            pairingCode = code;
            Changed();
        }

        public void EndPairing(string? endNotice)
        {
            ResetPairing();
            notice = endNotice;
            Changed();
        }

        public void SetNotice(string? text)
        {
            notice = text;
            Changed();
        }

        /// <summary>Raised for problems the user must act on outside the settings view (shown as a LaunchBox message).</summary>
        public event Action<string>? Alerted;

        public void Alert(string text) => Alerted?.Invoke(text);

        public void Raise(string text)
        {
            SetNotice(text);
            Alert(text);
        }

        /// <summary>A full sync is sending its batches; the status line shows the progress and Sync now is disabled.</summary>
        public void SetSyncProgress(int sent, int? total)
        {
            syncProgress = new SyncProgress { Sent = sent, Total = total };
            Changed();
        }

        public void EndSyncProgress()
        {
            syncProgress = null;
            Changed();
        }

        public void RecordSync(int games)
        {
            lastSyncUtc = DateTime.UtcNow;
            gameCount = games;
            notice = null;
            Changed();
        }

        public static string FormatCode(string? userCode)
        {
            var code = (userCode ?? "").Replace("-", "").Trim().ToUpperInvariant();
            return code.Length == 8 ? code.Substring(0, 4) + "-" + code.Substring(4) : code;
        }

        private static string Ago(TimeSpan span)
        {
            if (span.TotalSeconds < 60) return "just now";
            if (span.TotalMinutes < 60) return (int)span.TotalMinutes + " min ago";
            if (span.TotalHours < 48) return (int)span.TotalHours + " h ago";
            return (int)span.TotalDays + " d ago";
        }

        private void ResetPairing()
        {
            pairing = false;
            pairingCode = null;
        }

        private void ClearConnection()
        {
            connected = false;
            userName = null;
            lastSyncUtc = null;
            gameCount = null;
            notice = null;
        }

        private void Changed()
        {
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(IsPairing));
            OnPropertyChanged(nameof(CanConnect));
            OnPropertyChanged(nameof(IsSyncing));
            OnPropertyChanged(nameof(CanSyncNow));
            OnPropertyChanged(nameof(HasPairingCode));
            OnPropertyChanged(nameof(PairingCode));
            OnPropertyChanged(nameof(Notice));
            OnPropertyChanged(nameof(HasNotice));
            OnPropertyChanged(nameof(StatusLine));
        }
    }
}
