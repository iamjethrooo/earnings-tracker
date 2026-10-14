using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace EarningsTracker.Utilities;

// Materialize before replacing so a failed enumeration cannot discard the displayed snapshot.
internal sealed class BatchObservableCollection<T> : ObservableCollection<T>
{
    internal void ReplaceAll(IEnumerable<T> values)
    {
        var items = values.ToList();
        CheckReentrancy();
        Items.Clear();
        foreach (var item in items) Items.Add(item);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
