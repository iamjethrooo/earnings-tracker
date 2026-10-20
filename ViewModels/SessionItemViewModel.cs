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
        private bool _isSaving;
        public bool IsSaving
        {
            get => _isSaving;
            private set { _isSaving = value; OnPropertyChanged(); }
        }
        public string? RecordedDateError { get; private set; }
        public string? PostedDateError { get; private set; }
        public string? DurationError { get; private set; }
        public string? EarningsError { get; private set; }
        public string? InvalidField { get; private set; }

        public bool TryBeginSave()
        {
            if (!IsEditing || IsSaving) return false;
            IsSaving = true;
            return true;
        }

        public void EndSave() => IsSaving = false;

        public Session Session { get; private set; }
        public Game? Game => Session.Game;
        public bool CanNavigate => Game != null;
        public string Identifier => $"Session #{Session.Id}";
        public string SourceId => Session.Payout?.SourceId ?? "Not recorded";
        public string SourceLabel { get; private set; } = "Not recorded";
        public string PostedText => Session.PostedDate?.ToString("MMM d, yyyy h:mm tt") ?? "Unknown — no posting date recorded";
        public IReadOnlyList<SessionChange> Evidence { get; private set; } = [];
        internal void SetSource(string source) { SourceLabel = source; OnPropertyChanged(nameof(SourceLabel)); OnPropertyChanged(nameof(SourceId)); }
        internal void SetEvidence(IReadOnlyList<SessionChange> evidence) { Evidence = evidence; OnPropertyChanged(nameof(Evidence)); }
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
            OnPropertyChanged(nameof(CanNavigate));
            OnPropertyChanged(nameof(CanSelectGame));
            OnPropertyChanged(nameof(Identifier));
            OnPropertyChanged(nameof(SourceId));
            OnPropertyChanged(nameof(PostedText));
        }

        internal void RestoreDraftGame(Game? game)
        {
            DraftGame = game;
            OnPropertyChanged(nameof(DraftGame));
        }

        public void BeginEdit()
        {
            if (IsEditing || IsSaving) return;
            ClearValidation();
            EditState = Utilities.SessionService.CaptureEditState(Session);
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
            if (IsSaving) return;
            ClearValidation();
            EditError = null;
            IsEditing = false;
        }

        public bool TryCreateEditedSession(out Session edited, out string error)
        {
            edited = new Session();
            error = string.Empty;
            ClearValidation();
            if (EditState != null && Utilities.SessionService.CaptureEditState(Session) != EditState)
                return Fail("This session changed while you were editing. Cancel the draft and edit the refreshed session again.", out error);
            if (!DraftRecordedDate.HasValue)
                return FieldError(nameof(DraftRecordedDate), "Choose the date the session was recorded.", out error);
            if (!decimal.TryParse(DraftDurationMinutes, NumberStyles.Number, CultureInfo.CurrentCulture, out var minutes) || minutes < 0)
                return FieldError(nameof(DraftDurationMinutes), "Enter a valid non-negative duration in minutes.", out error);
            if (!decimal.TryParse(DraftEarnings, NumberStyles.Number, CultureInfo.CurrentCulture, out var earnings) || earnings < 0)
                return FieldError(nameof(DraftEarnings), "Enter valid non-negative session earnings.", out error);
            if (DraftStatus == SessionStatus.Verified && DraftIsForceVerified && !DraftPostedDate.HasValue)
                return FieldError(nameof(DraftPostedDate), "Choose a posted date for a force-verified session.", out error);

            // Display rounding must not change stored precision when another field is edited.
            var duration = Session.Duration;
            if (DraftDurationMinutes != _initialDurationText)
            {
                if (minutes > TimeSpan.MaxValue.Ticks / (decimal)TimeSpan.TicksPerMinute)
                    return FieldError(nameof(DraftDurationMinutes), "Enter a duration within the supported range.", out error);
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

        internal Utilities.SessionService.EditState? EditState { get; private set; }

        public void ApplySavedSession(Session session)
        {
            // Persistence has committed. Retire the draft before any display notification can fail.
            IsEditing = false;
            if (Session.GameId != session.GameId) SetArtwork(null);
            Session = session;
            EditError = null;
            ClearValidation();
            OnPropertyChanged(nameof(Session));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(Game));
            OnPropertyChanged(nameof(CanNavigate));
            OnPropertyChanged(nameof(CanSelectGame));
            OnPropertyChanged(nameof(PostedText));
        }

        // Calendar edits change the day, not the recording/verification time or timestamp kind.
        private static DateTime WithOriginalTime(DateTime selectedDate, DateTime original) =>
            DateTime.SpecifyKind(selectedDate.Date.Add(original.TimeOfDay), original.Kind);

        private static bool Fail(string message, out string error)
        {
            error = message;
            return false;
        }

        private bool FieldError(string field, string message, out string error)
        {
            InvalidField = field;
            switch (field)
            {
                case nameof(DraftRecordedDate): RecordedDateError = message; break;
                case nameof(DraftPostedDate): PostedDateError = message; break;
                case nameof(DraftDurationMinutes): DurationError = message; break;
                case nameof(DraftEarnings): EarningsError = message; break;
            }
            NotifyValidation();
            return Fail(message, out error);
        }

        private void ClearValidation()
        {
            InvalidField = RecordedDateError = PostedDateError = DurationError = EarningsError = null;
            NotifyValidation();
        }

        private void NotifyValidation()
        {
            OnPropertyChanged(nameof(RecordedDateError));
            OnPropertyChanged(nameof(PostedDateError));
            OnPropertyChanged(nameof(DurationError));
            OnPropertyChanged(nameof(EarningsError));
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
