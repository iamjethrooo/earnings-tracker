using EarningsTracker.Enums;
using EarningsTracker.Models;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using EarningsTracker.Utilities;

namespace EarningsTracker.ViewModels
{
    public class SessionItemViewModel : INotifyPropertyChanged
    {
        private bool _isEditing;
        private string? _editError;
        private string _initialDurationText = string.Empty;
        private string _initialEarningsText = string.Empty;

        public Session Session { get; private set; }
        public Game? Game => Session.Game;
        private GameMetadata? _artworkMetadata;
        private ImageSource? _cover;
        private bool _coverRequested;
        public ImageSource? Cover
        {
            get { if (!_coverRequested) { _coverRequested = true; _ = LoadCoverAsync(); } return _cover; }
        }
        internal void SetArtwork(GameMetadata? metadata)
        {
            if (_artworkMetadata?.CoverSha256 == metadata?.CoverSha256 && _artworkMetadata?.CoverUrl == metadata?.CoverUrl)
            { _artworkMetadata = metadata; return; }
            _artworkMetadata = metadata; _cover = null; _coverRequested = false;
            OnPropertyChanged(nameof(Cover));
        }
        private async Task LoadCoverAsync()
        {
            var metadata = _artworkMetadata;
            if (metadata == null || metadata.GameId != Session.GameId) return;
            var cover = await CatalogArtwork.LoadAsync(metadata.CoverUrl, metadata.CoverSha256);
            if (metadata.CoverSha256 != _artworkMetadata?.CoverSha256 || metadata.CoverUrl != _artworkMetadata?.CoverUrl
                || _artworkMetadata?.GameId != Session.GameId) return;
            _cover = cover; OnPropertyChanged(nameof(Cover));
        }
        public bool CanSelectGame => !Session.GameId.HasValue;
        public string StatusText => Session.Status == SessionStatus.NotUploaded
            ? "Not Uploaded"
            : Session.Status.ToString();
        public bool IsEditing
        {
            get => _isEditing;
            private set { _isEditing = value; OnPropertyChanged(); }
        }
        public string? EditError
        {
            get => _editError;
            private set { _editError = value; OnPropertyChanged(); }
        }

        public DateTime? DraftRecordedDate { get; set; }
        public DateTime? DraftPostedDate { get; set; }
        public string DraftDurationMinutes { get; set; } = string.Empty;
        public string DraftEarnings { get; set; } = string.Empty;
        public SessionStatus DraftStatus { get; set; }
        public bool DraftIsForceVerified { get; set; }
        public Game? DraftGame { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;

        public SessionItemViewModel(Session session)
        {
            Session = session;
        }

        internal void RefreshSession(Session session)
        {
            Session = session;
            OnPropertyChanged(nameof(Session));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(Game));
            OnPropertyChanged(nameof(CanSelectGame));
        }

        internal void RestoreDraftGame(Game? game)
        {
            DraftGame = game;
            OnPropertyChanged(nameof(DraftGame));
        }

        public void BeginEdit()
        {
            DraftRecordedDate = Session.RecordedDate == default ? Session.Date : Session.RecordedDate;
            DraftPostedDate = Session.PostedDate;
            DraftDurationMinutes = Session.Duration.TotalMinutes.ToString("0.##", CultureInfo.CurrentCulture);
            DraftEarnings = Session.Earnings.ToString("0.00", CultureInfo.CurrentCulture);
            _initialDurationText = DraftDurationMinutes;
            _initialEarningsText = DraftEarnings;
            DraftStatus = Session.Status;
            DraftIsForceVerified = Session.IsForceVerified;
            DraftGame = Session.Game;
            EditError = null;
            IsEditing = true;
            OnPropertyChanged(nameof(DraftRecordedDate));
            OnPropertyChanged(nameof(DraftPostedDate));
            OnPropertyChanged(nameof(DraftDurationMinutes));
            OnPropertyChanged(nameof(DraftEarnings));
            OnPropertyChanged(nameof(DraftStatus));
            OnPropertyChanged(nameof(DraftIsForceVerified));
            OnPropertyChanged(nameof(DraftGame));
        }

        public void CancelEdit()
        {
            EditError = null;
            IsEditing = false;
        }

        public bool TryCreateEditedSession(out Session edited, out string error)
        {
            edited = new Session();
            error = string.Empty;
            if (!DraftRecordedDate.HasValue)
                return Fail("Choose the date the session was recorded.", out error);
            if (!decimal.TryParse(DraftDurationMinutes, NumberStyles.Number, CultureInfo.CurrentCulture, out var minutes) || minutes < 0)
                return Fail("Enter a valid non-negative duration in minutes.", out error);
            if (!decimal.TryParse(DraftEarnings, NumberStyles.Number, CultureInfo.CurrentCulture, out var earnings) || earnings < 0)
                return Fail("Enter valid non-negative session earnings.", out error);
            if (DraftStatus == SessionStatus.Verified && DraftIsForceVerified && !DraftPostedDate.HasValue)
                return Fail("Choose a posted date for a force-verified session.", out error);

            // Display rounding must not change stored precision when another field is edited.
            var duration = Session.Duration;
            if (DraftDurationMinutes != _initialDurationText)
            {
                if (minutes > TimeSpan.MaxValue.Ticks / (decimal)TimeSpan.TicksPerMinute)
                    return Fail("Enter a duration within the supported range.", out error);
                duration = TimeSpan.FromTicks((long)decimal.Round(minutes * TimeSpan.TicksPerMinute));
            }
            if (DraftEarnings == _initialEarningsText)
                earnings = Session.Earnings;

            var originalRecordedDate = Session.RecordedDate == default ? Session.Date : Session.RecordedDate;
            var recordedDate = WithOriginalTime(DraftRecordedDate.Value, originalRecordedDate);
            var postedDate = DraftPostedDate;
            if (postedDate.HasValue && Session.PostedDate.HasValue)
                postedDate = WithOriginalTime(postedDate.Value, Session.PostedDate.Value);
            if (DraftStatus == SessionStatus.Verified && !postedDate.HasValue)
                postedDate = recordedDate;

            edited = new Session
            {
                Id = Session.Id,
                GameId = CanSelectGame && DraftGame != null ? DraftGame.Id : Session.GameId,
                GameName = CanSelectGame && DraftGame != null ? DraftGame.Name : Session.GameName,
                Game = CanSelectGame && DraftGame != null ? DraftGame : Session.Game,
                PayoutId = Session.PayoutId,
                Payout = Session.Payout,
                Date = recordedDate,
                RecordedDate = recordedDate,
                PostedDate = postedDate,
                Duration = duration,
                Earnings = earnings,
                Status = DraftStatus,
                IsForceVerified = DraftIsForceVerified
            };
            return true;
        }

        public void ShowEditError(string message) => EditError = message;

        public void ApplySavedSession(Session session)
        {
            if (Session.GameId != session.GameId) SetArtwork(null);
            Session = session;
            EditError = null;
            IsEditing = false;
            OnPropertyChanged(nameof(Session));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(Game));
            OnPropertyChanged(nameof(CanSelectGame));
        }

        // Calendar edits change the day, not the recording/verification time or timestamp kind.
        private static DateTime WithOriginalTime(DateTime selectedDate, DateTime original) =>
            DateTime.SpecifyKind(selectedDate.Date.Add(original.TimeOfDay), original.Kind);

        private static bool Fail(string message, out string error)
        {
            error = message;
            return false;
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
