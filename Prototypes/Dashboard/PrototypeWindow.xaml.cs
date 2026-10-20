using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EarningsTracker.Enums;

namespace EarningsTracker.Prototypes;

public partial class PrototypeWindow : Window
{
    public DashboardViewModel ViewModel { get; } = new();
    private IInputElement? _previewFocusOrigin;
    private bool _previewWasOpen;

    public PrototypeWindow()
    {
        InitializeComponent();
        DataContext = ViewModel;
        ViewModel.CanNavigate = () => !ViewModel.IsSessions || SessionsExplorer.ViewModel.TryLeave();
        SessionsExplorer.ViewModel.GameRequested += game => ViewModel.OpenGamePreview(game,
            $"Synthetic game preview from Sessions\n\n{game}\n\nClose this preview to return to the same selected session. The separate Games prototype is available from the sidebar; sample records are not synchronized.");
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        Closing += (_, e) =>
        {
            if (ViewModel.IsSessions && !SessionsExplorer.ViewModel.TryLeave()) e.Cancel = true;
        };
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        foreach (var item in Navigation.Items.OfType<ListBoxItem>())
            item.IsSelected = Equals(item.Tag, ViewModel.Page);
        if (ViewModel.IsPreviewOpen && !_previewWasOpen)
        {
            _previewWasOpen = true;
            _previewFocusOrigin = Keyboard.FocusedElement;
            // Keep the underlying shell out of the keyboard and mouse interaction scope.
            Shell.IsEnabled = false;
            ClosePreviewButton.Focus();
        }
        else if (!ViewModel.IsPreviewOpen && _previewWasOpen)
        {
            _previewWasOpen = false;
            Shell.IsEnabled = true;
            if (_previewFocusOrigin != null) Keyboard.Focus(_previewFocusOrigin);
            _previewFocusOrigin = null;
        }
    }

    private void Navigation_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is DashboardViewModel vm && e.AddedItems.OfType<ListBoxItem>().FirstOrDefault()?.Tag is NavigationPage page && vm.Page != page)
            vm.Page = page;
    }

    private void Period_Checked(object sender, RoutedEventArgs e)
    {
        if (DataContext is DashboardViewModel vm && sender is RadioButton { Tag: string index })
            vm.PeriodIndex = int.Parse(index, System.Globalization.CultureInfo.InvariantCulture);
    }

    private void PageRegion_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var stacked = e.NewSize.Width < 850;
        foreach (var (grid, panel) in new[] { (AnalysisGrid, DistributionPanel), (ActivityGrid, GamesPanel) })
        {
            grid.ColumnDefinitions[1].Width = new GridLength(stacked ? 0 : 16);
            grid.ColumnDefinitions[2].Width = stacked ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            Grid.SetColumn(panel, stacked ? 0 : 2);
            Grid.SetRow(panel, stacked ? 1 : 0);
            panel.Margin = stacked ? new Thickness(0, 16, 0, 0) : new Thickness(0);
        }
    }

    internal static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && ViewModel.IsPreviewOpen)
        {
            ViewModel.ClosePreviewCommand.Execute(null);
            e.Handled = true;
        }
    }
    private void Minimize_Click(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
