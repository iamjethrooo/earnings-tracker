using EarningsTracker.Enums;
using EarningsTracker.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Data;
using EarningsTracker.Utilities;

namespace EarningsTracker.ViewModels
{
    class GameDetailsViewModel : INotifyPropertyChanged
    {
        public Game Game { get; }
        private GameItemViewModel? _catalog;
        public GameItemViewModel? Catalog
        {
            get => _catalog;
            internal set { if (ReferenceEquals(_catalog, value)) return; _catalog = value; OnPropertyChanged(); }
        }
        public ObservableCollection<Session> Sessions { get; }
        public ICollectionView SessionsView { get; }
        public IReadOnlyList<SessionStatusFilterItem> StatusFilters { get; }
        public event PropertyChangedEventHandler? PropertyChanged;
        private SessionStatus? _selectedStatus;

        public GameDetailsViewModel(Game game, List<Session> sessions)
        {
            Game = game;
            Sessions = new ObservableCollection<Session>(sessions);
            SessionsView = CollectionViewSource.GetDefaultView(Sessions);
            StatusFilters = new[]
            {
                new SessionStatusFilterItem("All", null, true),
                new SessionStatusFilterItem("Not uploaded", SessionStatus.NotUploaded),
                new SessionStatusFilterItem("Verified", SessionStatus.Verified),
                new SessionStatusFilterItem("Rejected", SessionStatus.Rejected),
                new SessionStatusFilterItem("Pending", SessionStatus.Pending)
            };
            foreach (var filter in StatusFilters)
                filter.SetCount(filter.Status.HasValue ? Sessions.Count(s => s.Status == filter.Status) : Sessions.Count);
        }

        private void OnPropertyChanged(
    [CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(propertyName)
            );
        }

        public int SessionCount => Sessions.Count;
        public int FilteredSessionCount => SessionsView.Cast<Session>().Count();
        public string FilteredSessionString
        {
            get
            {
                return $"Sessions ({FilteredSessionCount})";
            }
        }
        public int VerifiedCount => Sessions.Count(s => s.Status == Enums.SessionStatus.Verified);
        public int RejectedCount => Sessions.Count(s => s.Status == Enums.SessionStatus.Rejected);
        public int NotUploadedCount => Sessions.Count(s => s.Status == Enums.SessionStatus.NotUploaded);
        public int PendingCount => Sessions.Count(s => s.Status == Enums.SessionStatus.Pending);
        public void FilterSessions(SessionStatus? status)
        {
            if (_selectedStatus == status) return;
            _selectedStatus = status;
            SessionsView.Filter = session =>
            {
                if (status == null)
                    return true;

                return ((Session)session).Status == status;
            };

            foreach (var filter in StatusFilters)
                filter.SetSelected(filter.Status == status);

            OnPropertyChanged(nameof(FilteredSessionCount));
            OnPropertyChanged(nameof(FilteredSessionString));
            OnPropertyChanged(nameof(SessionEmptyText));
        }
        public string SessionEmptyText => FilteredSessionCount > 0 ? "" : SessionCount == 0
            ? "No sessions for this game yet. Add a session to start tracking."
            : "No sessions match this status. Select All to see the full history.";
        public string AllFilterText => $"All ({SessionCount})";
        public string NotUploadedFilterText => $"Not uploaded ({NotUploadedCount})";
        public string VerifiedFilterText => $"Verified ({VerifiedCount})";
        public string RejectedFilterText => $"Rejected ({RejectedCount})";
        public string PendingFilterText => $"Pending ({PendingCount})";

        private IEnumerable<Session> CurrentCycleVerifiedSessions => Sessions.Where(s =>
            s.Status == SessionStatus.Verified &&
            (!Game.CapResetDate.HasValue || (s.RecordedDate == default ? s.Date : s.RecordedDate).Date >= Game.CapResetDate.Value.Date));

        public decimal TotalEarnings => CurrentCycleVerifiedSessions.Sum(s => s.Earnings);
        public decimal MaximumEarnings => Game.RatePerHour * Game.MinutesCap / 60;
        public decimal RemainingEarnings => Math.Max(0, MaximumEarnings - TotalEarnings);
        public decimal TotalMinutes => CurrentCycleVerifiedSessions.Sum(s => Convert.ToDecimal(s.Duration.TotalMinutes));
        public decimal RemainingMinutes => Math.Max(0, Game.MinutesCap - TotalMinutes);
        public string MinutesDescription => $"{TotalMinutes:0.##} min consumed · {RemainingMinutes:0.##} min remaining";

        public string CapText => $"{Game.MinutesCap}m";
        public decimal ProgressPercentage => Game.MinutesCap > 0 ? TotalMinutes / Game.MinutesCap * 100 : 0;
        public string MaxDurationText
        {
            get
            {
                int totalMinutes = (int)Game.MinutesCap;
                int hours = totalMinutes / 60;
                int minutes = totalMinutes % 60;

                if (minutes == 0)
                    return $"{hours}h";

                return $"{hours}h {minutes}m";
            }
        }
        public string ProgressDescription => Game.CapResetDate.HasValue
            ? $"of {MaxDurationText} · {ProgressPercentage:0}% · since {Game.CapResetDate.Value:M/d/yyyy}"
            : $"of {MaxDurationText} · {ProgressPercentage:0}% · all history";
        public string TotalDurationText
        {
            get
            {
                int totalSeconds = (int)(TotalMinutes * 60);

                int hours = totalSeconds / 3600;
                int minutes = (totalSeconds % 3600) / 60;

                return hours > 0 ? $"{hours}h {minutes}m" : $"{minutes}m";
            }
        }
        public string EarningsDescription => $"{CurrencyDisplay.FormatUsd(RemainingEarnings)} remaining · {CurrencyDisplay.FormatUsd(MaximumEarnings)} maximum";

        private static string FormatDuration(decimal totalMinutes)
        {
            int totalSeconds = (int)(totalMinutes * 60);

            int hours = totalSeconds / 3600;
            int minutes = (totalSeconds % 3600) / 60;
            int seconds = totalSeconds % 60;

            if (hours > 0)
                return $"{hours}h {minutes}m {seconds}s";

            if (minutes > 0)
                return $"{minutes}m {seconds}s";

            return $"{seconds}s";
        }
    }
}
