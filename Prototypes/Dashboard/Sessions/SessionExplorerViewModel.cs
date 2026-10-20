using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using EarningsTracker.Commands;

namespace EarningsTracker.Prototypes.Sessions;

public enum SessionEditField { None, Duration, Earnings, Verification, Reason }

public sealed record ExplorerSession(int Id, string Game, DateTime Recorded, decimal Minutes, decimal Earnings,
    string Status, string Source, string SourceId, DateTime? Posted, string SourceValues, string Note = "")
{
    public int Revision { get; set; } = 1;
    public string Amount => DashboardViewModel.Usd(Earnings);
    public string Duration => $"{Minutes:0.##} min";
    public string RecordedText => Recorded.ToString("MMM d, yyyy · HH:mm", CultureInfo.GetCultureInfo("en-US"));
    public string PostedText => Posted?.ToString("MMM d, yyyy", CultureInfo.GetCultureInfo("en-US")) ?? "Unknown · retained as recorded";
    public string Identifier => $"SESSION / {Id:00000}";
    public string NoteText => Note.Length == 0 ? "No local changes" : Note;
    public Brush StatusBrush => Resource(Status switch { "Verified" => "VerifiedTextColor", "Pending" => "PendingTextColor", "Rejected" => "RejectedTextColor", _ => "SecondaryTextColor" });
    public Brush StatusBackground => Resource(Status switch { "Verified" => "VerifiedBackgroundColor", "Pending" => "PendingBackgroundColor", "Rejected" => "RejectedBackgroundColor", _ => "NotUploadedBackgroundColor" });
    private static Brush Resource(string key) => (Brush)Application.Current.FindResource(key);
}

public sealed class SessionExplorerViewModel : INotifyPropertyChanged
{
    private readonly ObservableCollection<ExplorerSession> _rows = new(CreateRows());
    private ExplorerSession? _selected;
    private string _search = "", _status = "All statuses", _game = "All games", _date = "Last 30 days", _sort = "Newest first";
    private string _example = "Populated", _saveResult = "Normal";
    private string _draftMinutes = "", _draftEarnings = "", _draftStatus = "Pending", _draftNote = "";
    private string _validation = "", _feedback = "";
    private string _feedbackColor = "PendingTextColor";
    private int _editRevision;
    private bool _isEditing, _isBusy, _savedRefreshFailed, _isImportOpen;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<string>? GameRequested;
    public event Action<SessionEditField>? ValidationFailed;
    public ICollectionView Rows { get; }
    public string[] Statuses { get; } = ["All statuses", "Verified", "Pending", "Rejected", "Not uploaded"];
    public string[] EditStatuses { get; } = ["Verified", "Pending", "Rejected", "Not uploaded"];
    public string[] Games { get; } = ["All games", "Orbital Drift", "Mosslight Valley", "Neon Circuit", "The Last Outpost"];
    public string[] Dates { get; } = ["Last 7 days", "Last 30 days", "All sample dates"];
    public string[] Sorts { get; } = ["Newest first", "Oldest first", "Earnings high to low", "Duration high to low", "Game A–Z"];
    public string[] Examples { get; } = ["Populated", "Empty", "Filtered empty", "Loading", "Stale", "Error"];
    public string[] SaveResults { get; } = ["Normal", "Conflict", "Saved · refresh failed"];
    public RelayCommand EditCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand CloseDetailsCommand { get; }
    public RelayCommand ClearFiltersCommand { get; }
    public RelayCommand RetryCommand { get; }
    public RelayCommand GameCommand { get; }
    public RelayCommand ImportCommand { get; }
    public RelayCommand CloseImportCommand { get; }

    public SessionExplorerViewModel()
    {
        Rows = CollectionViewSource.GetDefaultView(_rows);
        Rows.Filter = Matches;
        EditCommand = new(_ => BeginEdit(), _ => SelectedSession != null && CanBrowse);
        CancelCommand = new(_ => CancelEdit(), _ => IsEditing && !IsBusy);
        CloseDetailsCommand = new(_ => { if (TryLeave()) SelectedSession = null; }, _ => !IsBusy);
        ClearFiltersCommand = new(_ => ClearFilters(), _ => CanBrowse);
        RetryCommand = new(_ => Retry(), _ => CanBrowse);
        GameCommand = new(game => GameRequested?.Invoke((string)game!), _ => CanBrowse);
        ImportCommand = new(_ => { _isImportOpen = true; Notify(); }, _ => CanBrowse);
        CloseImportCommand = new(_ => { _isImportOpen = false; Notify(); });
        ApplySort();
        _selected = Rows.Cast<ExplorerSession>().First();
    }

    public ExplorerSession? SelectedSession
    {
        get => _selected;
        set
        {
            if (IsEditing || IsBusy) { Notify(); return; }
            _selected = value;
            _validation = "";
            ValidationField = SessionEditField.None;
            Notify();
        }
    }
    public string Search { get => _search; set { if (!CanBrowse) return; _search = value; Refresh(); } }
    public string StatusFilter { get => _status; set { if (!CanBrowse) return; _status = value; Refresh(); } }
    public string GameFilter { get => _game; set { if (!CanBrowse) return; _game = value; Refresh(); } }
    public string DateFilter { get => _date; set { if (!CanBrowse) return; _date = value; Refresh(); } }
    public string Sort { get => _sort; set { if (!CanBrowse) return; _sort = value; ApplySort(); Notify(); } }
    public string Example
    {
        get => _example;
        set { if (!CanBrowse) return; _example = value; _feedback = ""; Refresh(); }
    }
    public string SaveResult { get => _saveResult; set { if (!IsBusy) { _saveResult = value; Notify(); } } }
    public string DraftMinutes { get => _draftMinutes; set { _draftMinutes = value; DraftChanged(SessionEditField.Duration); } }
    public string DraftEarnings { get => _draftEarnings; set { _draftEarnings = value; DraftChanged(SessionEditField.Earnings); } }
    public string DraftStatus { get => _draftStatus; set { _draftStatus = value; DraftChanged(SessionEditField.Verification); } }
    public string DraftNote { get => _draftNote; set { _draftNote = value; DraftChanged(SessionEditField.Reason); } }
    public bool IsEditing => _isEditing;
    public bool IsBusy => _isBusy;
    public bool CanBrowse => !IsEditing && !IsBusy;
    public bool CanEditFields => IsEditing && !IsBusy;
    public bool ShowReadDetails => ShowDetails && !IsEditing;
    public bool ShowDetails => SelectedSession != null && ShowRows;
    public bool IsImportOpen => _isImportOpen;
    public bool ShowRows => Example is "Populated" or "Stale";
    public bool ShowState => !ShowRows || VisibleCount == 0;
    public bool HasFilters => Search.Length > 0 || StatusFilter != "All statuses" || GameFilter != "All games" || DateFilter != "All sample dates";
    public bool HasUnsavedChanges => IsEditing && SelectedSession is { } row &&
        (_draftMinutes != row.Minutes.ToString(CultureInfo.CurrentCulture) || _draftEarnings != row.Earnings.ToString(CultureInfo.CurrentCulture) ||
         _draftStatus != row.Status || _draftNote != row.Note);
    public string DraftLabel => IsBusy ? "Saving sample changes…" : HasUnsavedChanges ? "Unsaved changes" : "Editing session";
    public string Validation => _validation;
    public SessionEditField ValidationField { get; private set; }
    public string DurationValidation => ValidationField == SessionEditField.Duration ? Validation : "";
    public string EarningsValidation => ValidationField == SessionEditField.Earnings ? Validation : "";
    public string VerificationValidation => ValidationField == SessionEditField.Verification ? Validation : "";
    public string ReasonValidation => ValidationField == SessionEditField.Reason ? Validation : "";
    public string Feedback => _feedback;
    public Brush FeedbackBrush => (Brush)Application.Current.FindResource(_feedbackColor);
    public bool HasFeedback => _feedback.Length > 0;
    public bool ShowRefreshRetry => _savedRefreshFailed;
    public bool IsStale => Example == "Stale";
    public int VisibleCount => Rows.Cast<ExplorerSession>().Count();
    public int TotalCount => _rows.Count;
    public string CountLabel => Example switch
    {
        "Loading" => "Loading sample snapshot…", "Error" => "Session count unavailable", "Empty" => "No sample sessions",
        _ => $"{VisibleCount:N0} of {TotalCount:N0} sessions"
    };
    public string BasisLabel => "USD · recording date · synthetic snapshot Oct 19, 2026";
    public string StateTitle => Example switch
    {
        "Empty" => "Your session history starts here", "Loading" => "Loading sessions…", "Error" => "Sessions unavailable", _ => "No sessions match"
    };
    public string StateDescription => Example switch
    {
        "Empty" => "There are no sessions in this sample library. Preview an import to see how new recordings would arrive.",
        "Loading" => "Static loading example. Records and totals remain hidden until a complete snapshot is available.",
        "Error" => "Simulated first-load failure. No records are shown. Retry restores the synthetic snapshot.",
        _ => "Try another game, status or recording-date range, or clear your filters."
    };
    public bool StateCanRetry => Example == "Error";
    public bool StateCanClear => Example == "Filtered empty" || (ShowRows && VisibleCount == 0);
    public bool StateCanImport => Example == "Empty";

    private bool Matches(object item)
    {
        if (Example is "Empty" or "Filtered empty" or "Loading" or "Error") return false;
        var row = (ExplorerSession)item;
        var from = DateFilter switch { "Last 7 days" => DashboardViewModel.AsOf.Date.AddDays(-6), "Last 30 days" => DashboardViewModel.AsOf.Date.AddDays(-29), _ => DateTime.MinValue };
        return row.Recorded >= from && (StatusFilter == "All statuses" || row.Status == StatusFilter) &&
            (GameFilter == "All games" || row.Game == GameFilter) &&
            (Search.Length == 0 || row.Game.Contains(Search.Trim(), StringComparison.OrdinalIgnoreCase) ||
             row.SourceId.Contains(Search.Trim(), StringComparison.OrdinalIgnoreCase));
    }
    private void Refresh()
    {
        var selected = _selected;
        Rows.Refresh();
        // Collection-view resets may clear the bound selection while filtering; restore it if still present.
        _selected = selected != null && Rows.Contains(selected) ? selected : null;
        Notify();
    }
    private void ApplySort()
    {
        using (Rows.DeferRefresh())
        {
            Rows.SortDescriptions.Clear();
            Rows.SortDescriptions.Add(Sort switch
            {
                "Oldest first" => new(nameof(ExplorerSession.Recorded), ListSortDirection.Ascending),
                "Earnings high to low" => new(nameof(ExplorerSession.Earnings), ListSortDirection.Descending),
                "Duration high to low" => new(nameof(ExplorerSession.Minutes), ListSortDirection.Descending),
                "Game A–Z" => new(nameof(ExplorerSession.Game), ListSortDirection.Ascending),
                _ => new(nameof(ExplorerSession.Recorded), ListSortDirection.Descending)
            });
            Rows.SortDescriptions.Add(new(nameof(ExplorerSession.Id), ListSortDirection.Ascending));
        }
    }
    public void ClearFilters()
    {
        if (!CanBrowse) return;
        _search = ""; _status = "All statuses"; _game = "All games"; _date = "All sample dates";
        if (Example == "Filtered empty") _example = "Populated";
        Refresh();
    }
    private void BeginEdit()
    {
        if (SelectedSession is not { } row || !CanBrowse) return;
        _editRevision = row.Revision;
        _draftMinutes = row.Minutes.ToString(CultureInfo.CurrentCulture);
        _draftEarnings = row.Earnings.ToString(CultureInfo.CurrentCulture);
        _draftStatus = row.Status;
        _draftNote = row.Note;
        _validation = ""; ValidationField = SessionEditField.None; _feedback = ""; _isEditing = true;
        Notify();
    }
    private void DraftChanged(SessionEditField field)
    {
        // Editing an unrelated field must not hide the outstanding error.
        if (ValidationField == field) { _validation = ""; ValidationField = SessionEditField.None; }
        Notify();
    }
    private void SetValidation(SessionEditField field, string message)
    {
        ValidationField = field;
        _validation = message;
    }
    private void CancelEdit()
    {
        if (IsBusy) return;
        _isEditing = false; _validation = ""; ValidationField = SessionEditField.None;
        SetFeedback("Draft cancelled. Sample record unchanged.", "SecondaryTextColor");
        Notify();
    }
    public bool TryLeave()
    {
        if (IsBusy || HasUnsavedChanges)
        {
            SetFeedback(IsBusy ? "A sample save is in progress. Wait before leaving." : "You have unsaved changes. Save or Cancel this draft before leaving.");
            Notify();
            return false;
        }
        if (IsEditing) CancelEdit();
        if (_isImportOpen) { _isImportOpen = false; Notify(); }
        return true;
    }
    public async Task SaveAsync()
    {
        if (!IsEditing || IsBusy || SelectedSession is not { } row) return;
        if (!decimal.TryParse(DraftMinutes, NumberStyles.Number, CultureInfo.CurrentCulture, out var minutes) || minutes <= 0 || minutes > 1440)
            SetValidation(SessionEditField.Duration, "Enter a duration greater than 0 and no more than 1,440 minutes.");
        else if (!decimal.TryParse(DraftEarnings, NumberStyles.Number, CultureInfo.CurrentCulture, out var earnings) || earnings < 0)
            SetValidation(SessionEditField.Earnings, "Enter nonnegative earnings in USD. Invalid input is not rounded or made positive.");
        else if (!EditStatuses.Contains(DraftStatus)) SetValidation(SessionEditField.Verification, "Choose a verification status.");
        else if (string.IsNullOrWhiteSpace(DraftNote) || DraftNote.Length > 240) SetValidation(SessionEditField.Reason, "Add a change reason (1–240 characters).");
        else
        {
            _isBusy = true; _feedback = ""; Notify();
            try
            {
                // A short dispatcher-friendly delay demonstrates the busy guard; no write or service call occurs.
                await Task.Delay(900);
                if (SaveResult == "Conflict" && row.Revision == _editRevision) row.Revision++;
                if (row.Revision != _editRevision)
                {
                    SetFeedback("Conflict: this sample changed after editing began. Your draft is retained. Cancel, inspect the current sample, then Edit again.");
                    return;
                }
                var updated = row with { Minutes = minutes, Earnings = earnings, Status = DraftStatus, Note = DraftNote.Trim(), Revision = row.Revision + 1 };
                _isEditing = false;
                _rows[_rows.IndexOf(row)] = updated;
                _selected = updated;
                _savedRefreshFailed = SaveResult == "Saved · refresh failed";
                SetFeedback(_savedRefreshFailed
                    ? "Saved in memory — refresh failed. Do not save again. Retry refresh to dismiss this synthetic warning."
                    : "Changes saved to this in-memory sample only.", _savedRefreshFailed ? "PendingTextColor" : "VerifiedTextColor");
            }
            finally { _isBusy = false; Notify(); }
            return;
        }
        Notify();
        ValidationFailed?.Invoke(ValidationField);
    }
    private void Retry()
    {
        if (!CanBrowse) return;
        if (_savedRefreshFailed)
        {
            _savedRefreshFailed = false;
            SetFeedback("Sample refresh complete. The saved change was not repeated.", "VerifiedTextColor");
        }
        else { _example = "Populated"; _feedback = ""; }
        Refresh();
    }
    public void ReportError(Exception error)
    {
        System.Diagnostics.Trace.TraceError(error.ToString());
        SetFeedback("The prototype operation could not finish. Cancel the draft and try again.", "RejectedTextColor");
        Notify();
    }
    private void SetFeedback(string message, string color = "PendingTextColor")
    {
        _feedback = message;
        _feedbackColor = color;
    }
    private void Notify()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        foreach (var command in new[] { EditCommand, CancelCommand, CloseDetailsCommand, ClearFiltersCommand, RetryCommand, GameCommand, ImportCommand })
            command?.RaiseCanExecuteChanged();
    }
    private static IEnumerable<ExplorerSession> CreateRows()
    {
        string[] games = ["Orbital Drift", "Mosslight Valley", "Neon Circuit", "The Last Outpost"];
        string[] statuses = ["Pending", "Verified", "Verified", "Verified", "Pending", "Verified", "Rejected", "Not uploaded"];
        for (var i = 0; i < 1500; i++)
        {
            var recorded = DashboardViewModel.AsOf.Date.AddDays(-(i / 18)).AddHours(21).AddMinutes(-(i % 18) * 37);
            var minutes = 30m + i * 7 % 151;
            var earnings = 2.50m + i * 13 % 117 / 10m;
            var status = statuses[i % statuses.Length];
            yield return new(i + 1, games[i % games.Length], recorded, minutes, earnings, status,
                i % 13 == 0 ? "Manual" : "Recordings", $"SAMPLE-{20481 + i}", i % 3 == 0 ? null : recorded.Date.AddDays(1),
                $"Recorded: {recorded:yyyy-MM-dd HH:mm}\nDuration: {minutes} min\nEarnings: {DashboardViewModel.Usd(earnings)}\nStatus: {status}\nSynthetic source evidence; retained after edits.");
        }
    }
}
