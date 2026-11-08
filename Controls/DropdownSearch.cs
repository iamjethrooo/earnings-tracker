using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace EarningsTracker.Controls;

/// <summary>Searches a popup snapshot without filtering a selector's bound source or draft selection.</summary>
public static class DropdownSearch
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(DropdownSearch), new PropertyMetadata(false, EnabledChanged));
    public static readonly DependencyProperty MemberPathProperty = DependencyProperty.RegisterAttached(
        "MemberPath", typeof(string), typeof(DropdownSearch), new PropertyMetadata(""));
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(SearchState), typeof(DropdownSearch));

    public static bool GetIsEnabled(DependencyObject target) => (bool)target.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject target, bool value) => target.SetValue(IsEnabledProperty, value);
    public static string GetMemberPath(DependencyObject target) => (string)target.GetValue(MemberPathProperty);
    public static void SetMemberPath(DependencyObject target, string value) => target.SetValue(MemberPathProperty, value);

    private static void EnabledChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not ComboBox combo) return;
        if (combo.GetValue(StateProperty) is SearchState previous) previous.Detach();
        combo.SetValue(StateProperty, (bool)e.NewValue ? new SearchState(combo) : null);
    }

    private sealed class SearchState
    {
        private readonly ComboBox _combo;
        private TextBox? _search;
        private ListBox? _results;
        private TextBlock? _empty;

        public SearchState(ComboBox combo)
        {
            _combo = combo;
            combo.DropDownOpened += Opened;
            combo.DropDownClosed += Closed;
            combo.Unloaded += Unloaded;
        }

        public void Detach()
        {
            DisconnectPopup();
            _combo.DropDownOpened -= Opened;
            _combo.DropDownClosed -= Closed;
            _combo.Unloaded -= Unloaded;
        }

        private void Opened(object? sender, EventArgs e)
        {
            DisconnectPopup();
            _search = _combo.Template.FindName("PART_SearchBox", _combo) as TextBox;
            _results = _combo.Template.FindName("PART_SearchResults", _combo) as ListBox;
            _empty = _combo.Template.FindName("SearchEmpty", _combo) as TextBlock;
            if (_search == null || _results == null) return;
            _search.Text = "";
            var name = AutomationProperties.GetName(_combo);
            AutomationProperties.SetName(_search, string.IsNullOrWhiteSpace(name) ? "Search choices" : "Search " + name);
            _search.TextChanged += SearchChanged;
            _search.PreviewKeyDown += SearchKeyDown;
            _results.PreviewKeyDown += SearchKeyDown;
            _results.SelectionChanged += ResultSelectionChanged;
            _results.PreviewMouseUp += ResultClicked;
            ((INotifyCollectionChanged)_combo.Items).CollectionChanged += ItemsChanged;
            Refresh();
            _combo.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (_combo.IsDropDownOpen && _search != null) _search.Focus();
            }));
        }

        private void Closed(object? sender, EventArgs e) => DisconnectPopup();
        private void Unloaded(object sender, RoutedEventArgs e)
        {
            _combo.SetCurrentValue(ComboBox.IsDropDownOpenProperty, false);
            DisconnectPopup();
        }

        private void DisconnectPopup()
        {
            ((INotifyCollectionChanged)_combo.Items).CollectionChanged -= ItemsChanged;
            if (_search != null)
            {
                _search.TextChanged -= SearchChanged;
                _search.PreviewKeyDown -= SearchKeyDown;
                _search.Text = "";
            }
            if (_results != null)
            {
                _results.PreviewKeyDown -= SearchKeyDown;
                _results.PreviewMouseUp -= ResultClicked;
                _results.ItemsSource = null;
                _results.SelectionChanged -= ResultSelectionChanged;
            }
            _search = null;
            _results = null;
            _empty = null;
        }

        private void SearchChanged(object sender, TextChangedEventArgs e) => Refresh();
        private void ItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Refresh();

        // A popup highlight is not a ComboBox selection. Do not invoke the owning view's edit handlers.
        private void ResultSelectionChanged(object sender, SelectionChangedEventArgs e) => e.Handled = true;

        private void Refresh()
        {
            if (_search == null || _results == null) return;
            var query = _search.Text.Trim();
            var highlight = _results.SelectedItem ?? _combo.SelectedItem;
            var matches = _combo.Items.Cast<object>()
                .Where(item => Label(item).Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToArray();
            _results.ItemsSource = matches;
            _results.SelectedItem = matches.Contains(highlight) ? highlight : matches.FirstOrDefault();
            if (_empty != null) _empty.Visibility = matches.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private string Label(object item)
        {
            var path = GetMemberPath(_combo);
            if (string.IsNullOrEmpty(path)) path = _combo.DisplayMemberPath;
            if (string.IsNullOrEmpty(path)) path = TextSearch.GetTextPath(_combo);
            object? value = item;
            if (!string.IsNullOrEmpty(path))
                foreach (var part in path.Split('.'))
                {
                    if (value == null) break;
                    value = TypeDescriptor.GetProperties(value)[part]?.GetValue(value);
                }
            return (value is ContentControl control ? control.Content : value)?.ToString() ?? "";
        }

        private void SearchKeyDown(object sender, KeyEventArgs e)
        {
            if (_results == null) return;
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                CloseAndFocus();
            }
            else if (e.Key == Key.Enter)
            {
                e.Handled = true;
                Commit();
            }
            else if (e.Key is Key.Down or Key.Up)
            {
                e.Handled = true;
                if (_results.Items.Count == 0) return;
                var direction = e.Key == Key.Down ? 1 : -1;
                _results.SelectedIndex = Math.Clamp(_results.SelectedIndex + direction, 0, _results.Items.Count - 1);
                _results.ScrollIntoView(_results.SelectedItem);
            }
            else if (e.Key == Key.Tab)
            {
                e.Handled = true;
                CloseAndFocus();
                _combo.MoveFocus(new TraversalRequest(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)
                    ? FocusNavigationDirection.Previous : FocusNavigationDirection.Next));
            }
        }

        private void ResultClicked(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left || _results == null || e.OriginalSource is not DependencyObject source) return;
            if (ItemsControl.ContainerFromElement(_results, source) is not ListBoxItem item) return;
            _results.SelectedItem = item.Content;
            e.Handled = true;
            Commit();
        }

        private void Commit()
        {
            // Refreshes can remove a result between highlighting and activation. Never submit a stale identity.
            var selected = _results?.SelectedItem;
            if (selected == null || !_combo.Items.Contains(selected)) return;
            _combo.SetCurrentValue(Selector.SelectedItemProperty, selected);
            CloseAndFocus();
        }

        private void CloseAndFocus()
        {
            _combo.SetCurrentValue(ComboBox.IsDropDownOpenProperty, false);
            _combo.Focus();
        }
    }
}
