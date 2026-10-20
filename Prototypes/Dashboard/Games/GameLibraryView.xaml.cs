using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace EarningsTracker.Prototypes.Games;

public partial class GameLibraryView : UserControl
{
    public GameLibraryViewModel ViewModel { get; } = new();
    private IInputElement? _overlayFocus;
    private bool _overlayWasOpen;
    private bool _detailsWereOpen;

    public GameLibraryView()
    {
        InitializeComponent();
        DataContext = ViewModel;
        ViewModel.PropertyChanged += Model_Changed;
        PreviewKeyDown += View_PreviewKeyDown;
        IsVisibleChanged += (_, _) => { if (!IsVisible) ViewModel.CloseOverlays(); };
    }
    private void View_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ViewModel.IsCompact = e.NewSize.Width < 1050;
        var stacked = e.NewSize.Width < 900;
        DetailColumns.ColumnDefinitions[1].Width = new GridLength(stacked ? 0 : 16);
        DetailColumns.ColumnDefinitions[2].Width = stacked ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(AcquisitionColumn, stacked ? 0 : 2);
        Grid.SetRow(AcquisitionColumn, stacked ? 1 : 0);
        AcquisitionColumn.Margin = stacked ? new Thickness(0, 16, 0, 0) : new Thickness(0);
    }
    private void Model_Changed(object? sender, PropertyChangedEventArgs e)
    {
        var overlayOpen = ViewModel.IsPreferencesOpen || ViewModel.IsHistoryOpen;
        LibraryPage.IsEnabled = !overlayOpen;
        DetailsPage.IsEnabled = !overlayOpen;
        if (overlayOpen && !_overlayWasOpen)
        {
            _overlayFocus = Keyboard.FocusedElement;
            if (ViewModel.IsHistoryOpen) CloseHistoryButton.Focus();
            else ApplyStartingButton.Focus();
        }
        else if (!overlayOpen && _overlayWasOpen)
        {
            if (_overlayFocus != null) Keyboard.Focus(_overlayFocus);
            _overlayFocus = null;
        }
        _overlayWasOpen = overlayOpen;
        if (ViewModel.IsDetailsOpen && !_detailsWereOpen)
        {
            DetailsScroll.ScrollToTop();
            DetailsPage.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        }
        else if (!ViewModel.IsDetailsOpen && _detailsWereOpen)
            GamesList.Focus();
        _detailsWereOpen = ViewModel.IsDetailsOpen;
    }
    private void List_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && GamesList.SelectedItem is GameLibraryItem game)
        {
            ViewModel.DetailsCommand.Execute(game);
            e.Handled = true;
        }
    }
    private void View_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        if (ViewModel.IsHistoryOpen) ViewModel.CloseHistoryCommand.Execute(null);
        else if (ViewModel.IsPreferencesOpen) ViewModel.ClosePreferencesCommand.Execute(null);
        else if (ViewModel.IsDetailsOpen) ViewModel.BackCommand.Execute(null);
        else return;
        e.Handled = true;
    }
}
