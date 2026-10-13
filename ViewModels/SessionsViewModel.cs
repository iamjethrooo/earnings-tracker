using Microsoft.EntityFrameworkCore;
using nerve_earnings_tracker.Data;
using nerve_earnings_tracker.Enums;
using nerve_earnings_tracker.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using nerve_earnings_tracker.Utilities;

namespace nerve_earnings_tracker.ViewModels
{
    class SessionsViewModel : INotifyPropertyChanged
    {
        private string _searchText = string.Empty;
        private SessionStatus? _selectedStatus;
        private bool _onlyUnlinked;
        private int _selectedSortIndex;
        private readonly DbContextOptions<AppDbContext>? _databaseOptions;
        private Task? _loadTask;
        private bool _reloadRequested;

        public ObservableCollection<SessionItemViewModel> Sessions { get; } = new BatchObservableCollection<SessionItemViewModel>();
        public ObservableCollection<Game> AvailableGames { get; } = new BatchObservableCollection<Game>();
        public ObservableCollection<SessionStatusFilterItem> StatusFilters { get; } = new();
        public ICollectionView SessionsView { get; }
        public event PropertyChangedEventHandler? PropertyChanged;
        public bool IsLoading { get; private set; }
        public string? LoadError { get; private set; }

        public IReadOnlyList<string> SortOptions { get; } = ["Newest first", "Oldest first", "Game name", "Highest earnings", "Longest duration"];
        public int SelectedSortIndex
        {
            get => _selectedSortIndex;
            set
            {
                if (_selectedSortIndex == value || value < 0 || value >= SortOptions.Count) return;
                _selectedSortIndex = value;
                ApplySort();
                OnPropertyChanged(nameof(SelectedSortIndex));
            }
        }

        private void ApplySort()
        {
            var (property, direction) = _selectedSortIndex switch
            {
                1 => ("Session.RecordedDate", ListSortDirection.Ascending),
                2 => ("Session.GameName", ListSortDirection.Ascending),
                3 => ("Session.Earnings", ListSortDirection.Descending),
                4 => ("Session.Duration", ListSortDirection.Descending),
                _ => ("Session.RecordedDate", ListSortDirection.Descending)
            };
            using (SessionsView.DeferRefresh())
            {
                SessionsView.SortDescriptions.Clear();
                SessionsView.SortDescriptions.Add(new SortDescription(property, direction));
                if (property != "Session.RecordedDate")
                    SessionsView.SortDescriptions.Add(new SortDescription("Session.RecordedDate", ListSortDirection.Descending));
                SessionsView.SortDescriptions.Add(new SortDescription("Session.Id", ListSortDirection.Descending));
            }
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText == value)
                    return;
                _searchText = value;
                SessionsView.Refresh();
                OnPropertyChanged(nameof(SearchText));
                OnPropertyChanged(nameof(SessionHistoryText));
            }
        }

        public SessionsViewModel() : this(null) { }

        internal SessionsViewModel(DbContextOptions<AppDbContext>? databaseOptions)
        {
            _databaseOptions = databaseOptions;
            SessionsView = CollectionViewSource.GetDefaultView(Sessions);
            SessionsView.Filter = FilterSession;
            ApplySort();
            StatusFilters.Add(new SessionStatusFilterItem("All", null, true));
            StatusFilters.Add(new SessionStatusFilterItem("Not uploaded", SessionStatus.NotUploaded));
            StatusFilters.Add(new SessionStatusFilterItem("Verified", SessionStatus.Verified));
            StatusFilters.Add(new SessionStatusFilterItem("Rejected", SessionStatus.Rejected));
            StatusFilters.Add(new SessionStatusFilterItem("Pending", SessionStatus.Pending));
            StatusFilters.Add(new SessionStatusFilterItem("Ambiguous", null, onlyUnlinked: true));
        }

        public string SessionHistoryText
        {
            get
            {
                if (IsLoading)
                    return "Loading sessions…";
                if (LoadError != null)
                    return $"Unable to load sessions: {LoadError}";
                return $"Showing {SessionsView.Cast<SessionItemViewModel>().Count()} of {Sessions.Count} sessions";
            }
        }

        public void SelectStatus(SessionStatusFilterItem selected)
        {
            _selectedStatus = selected.Status;
            _onlyUnlinked = selected.OnlyUnlinked;
            foreach (var option in StatusFilters)
                option.SetSelected(option == selected);
            SessionsView.Refresh();
            OnPropertyChanged(nameof(SessionHistoryText));
        }

        public Task LoadSessionsAsync()
        {
            _reloadRequested = true;
            if (_loadTask != null) return _loadTask;
            IsLoading = true;
            LoadError = null;
            OnPropertyChanged(nameof(IsLoading));
            OnPropertyChanged(nameof(SessionHistoryText));
            return _loadTask = LoadCoreAsync();
        }

        private async Task LoadCoreAsync()
        {
            // Yield before reading, including providers whose async queries complete synchronously.
            await Task.Yield();
            try
            {
                do
                {
                    _reloadRequested = false;
                    try
                    {
                        var snapshot = await Task.Run(async () =>
                        {
                            using var db = _databaseOptions == null ? new AppDbContext() : new AppDbContext(_databaseOptions);
                            var sessions = await db.Sessions.Include(session => session.Game).AsNoTracking()
                                .OrderByDescending(session => session.RecordedDate).ThenByDescending(session => session.Id).ToListAsync();
                            var games = await db.Games.AsNoTracking().OrderBy(game => game.IsArchived)
                                .ThenBy(game => game.Name).ThenBy(game => game.Id).ToListAsync();
                            var artwork = await db.GameMetadata.AsNoTracking().ToDictionaryAsync(x => x.GameId);
                            return (sessions, games, artwork);
                        });
                        if (_reloadRequested) continue; // A change during the read requires a fresh snapshot.
                        var previous = Sessions.ToDictionary(item => item.Session.Id);
                        var rows = snapshot.sessions.Select(session =>
                        {
                            var item = previous.GetValueOrDefault(session.Id) ?? new SessionItemViewModel(session);
                            item.RefreshSession(session);
                            item.SetArtwork(session.GameId is int id ? snapshot.artwork.GetValueOrDefault(id) : null);
                            return item;
                        }).ToList();
                        var drafts = rows.Where(item => item.IsEditing).Select(item => (item, gameId: item.DraftGame?.Id)).ToList();
                        using (SessionsView.DeferRefresh())
                        {
                            ((BatchObservableCollection<Game>)AvailableGames).ReplaceAll(snapshot.games);
                            ((BatchObservableCollection<SessionItemViewModel>)Sessions).ReplaceAll(rows);
                        }
                        // A ComboBox may clear its selection during a Reset. Restore it with the new game instance.
                        foreach (var draft in drafts)
                            draft.item.RestoreDraftGame(snapshot.games.FirstOrDefault(game => game.Id == draft.gameId));
                        LoadError = null;
                        UpdateStatusCounts();
                    }
                    catch (Exception ex)
                    {
                        if (!_reloadRequested) LoadError = ex.Message;
                    }
                } while (_reloadRequested);
            }
            finally
            {
                IsLoading = false;
                _loadTask = null;
                OnPropertyChanged(nameof(IsLoading));
                OnPropertyChanged(nameof(SessionHistoryText));
            }
        }

        private void UpdateStatusCounts()
        {
            var counts = Sessions.GroupBy(item => item.Session.Status).ToDictionary(group => group.Key, group => group.Count());
            var unlinked = Sessions.Count(item => !item.Session.GameId.HasValue);
            foreach (var option in StatusFilters)
            {
                var count = Sessions.Count;
                if (option.OnlyUnlinked)
                    count = unlinked;
                else if (option.Status.HasValue)
                    count = counts.GetValueOrDefault(option.Status.Value);
                option.SetCount(count);
            }
        }

        private bool FilterSession(object item)
        {
            if (item is not SessionItemViewModel sessionItem)
                return false;

            if (_selectedStatus.HasValue && sessionItem.Session.Status != _selectedStatus.Value)
                return false;
            if (_onlyUnlinked && sessionItem.Session.GameId.HasValue)
                return false;

            return string.IsNullOrWhiteSpace(_searchText) ||
                sessionItem.Session.GameName.Contains(_searchText.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class SessionStatusFilterItem : INotifyPropertyChanged
    {
        private int _count;
        private bool _isSelected;
        public string Name { get; }
        public SessionStatus? Status { get; }
        public bool OnlyUnlinked { get; }
        public string? Description => OnlyUnlinked
            ? "Saved sessions without a linked game. Unresolved imports remain in the import preview."
            : null;
        public int Count
        {
            get => _count;
            private set { _count = value; OnPropertyChanged(nameof(Count)); OnPropertyChanged(nameof(DisplayText)); }
        }
        public bool IsSelected
        {
            get => _isSelected;
            private set { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); }
        }
        public string DisplayText => $"{Name} ({Count})";
        public event PropertyChangedEventHandler? PropertyChanged;

        public SessionStatusFilterItem(string name, SessionStatus? status, bool isSelected = false, bool onlyUnlinked = false)
        {
            Name = name;
            Status = status;
            IsSelected = isSelected;
            OnlyUnlinked = onlyUnlinked;
        }

        public void SetCount(int count) => Count = count;
        public void SetSelected(bool selected) => IsSelected = selected;

        private void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
