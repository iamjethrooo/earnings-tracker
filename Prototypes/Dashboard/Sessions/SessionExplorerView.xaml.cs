using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace EarningsTracker.Prototypes.Sessions;

public partial class SessionExplorerView : UserControl
{
    public SessionExplorerViewModel ViewModel { get; } = new();
    private bool _drawer;
    private IInputElement? _importFocus;
    private bool _importWasOpen;
    private bool _detailsWereOpen;
    private string _lastFeedback = "";
    private bool _wasBusy;

    public SessionExplorerView()
    {
        InitializeComponent();
        DataContext = ViewModel;
        ViewModel.PropertyChanged += Model_Changed;
        ViewModel.ValidationFailed += RevealValidation;
        PreviewKeyDown += View_PreviewKeyDown;
    }
    private void RevealValidation(SessionEditField field)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (!ViewModel.IsEditing || ViewModel.ValidationField != field || ViewModel.Validation.Length == 0) return;
            (Control Editor, FrameworkElement Group) target = field switch
            {
                SessionEditField.Duration => (MinutesEditor, DurationField),
                SessionEditField.Earnings => (EarningsEditor, EarningsField),
                SessionEditField.Verification => (StatusEditor, VerificationField),
                SessionEditField.Reason => (ReasonEditor, ReasonField),
                _ => throw new InvalidOperationException("No editing field for validation.")
            };
            DetailsScroll.UpdateLayout();
            target.Editor.Focus();
            // Reveal the field and its adjacent message, including repeated failed saves from a scrolled drawer.
            target.Group.BringIntoView();
        }));
    }
    private void Model_Changed(object? sender, PropertyChangedEventArgs e)
    {
        UpdatePanel();
        if (_drawer && ViewModel.ShowDetails && ViewModel.HasFeedback && (ViewModel.Feedback != _lastFeedback || _wasBusy && !ViewModel.IsBusy))
            RevealPanelFeedback();
        _lastFeedback = ViewModel.Feedback;
        _wasBusy = ViewModel.IsBusy;
        if (_drawer && ViewModel.ShowDetails && !_detailsWereOpen)
            DetailsPanel.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        _detailsWereOpen = ViewModel.ShowDetails;
        if (ViewModel.IsImportOpen && !_importWasOpen)
        {
            _importWasOpen = true;
            _importFocus = Keyboard.FocusedElement;
            CloseImportButton.Focus();
        }
        else if (!ViewModel.IsImportOpen && _importWasOpen)
        {
            _importWasOpen = false;
            if (_importFocus != null) Keyboard.Focus(_importFocus);
            _importFocus = null;
        }
    }
    private void View_SizeChanged(object sender, SizeChangedEventArgs e) => UpdatePanel();
    private void UpdatePanel()
    {
        if (DetailsColumn == null) return;
        var wasDrawer = _drawer;
        var feedbackWasVisible = PanelFeedback.Visibility == Visibility.Visible;
        _drawer = ActualWidth < 1150;
        DetailsColumn.Width = new GridLength(!_drawer && ViewModel.ShowDetails ? 336 : 0);
        DetailsGap.Width = new GridLength(!_drawer && ViewModel.ShowDetails ? 16 : 0);
        var host = _drawer ? DrawerLayer : Body;
        if (DetailsPanel.Parent is Grid previousHost && previousHost != host)
        {
            previousHost.Children.Remove(DetailsPanel);
            host.Children.Add(DetailsPanel);
        }
        Grid.SetColumn(DetailsPanel, _drawer ? 0 : 2);
        Grid.SetColumnSpan(DetailsPanel, 1);
        DetailsPanel.Width = _drawer ? Math.Min(360, Math.Max(300, ActualWidth - 32)) : double.NaN;
        DetailsPanel.HorizontalAlignment = _drawer ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
        DrawerLayer.Visibility = _drawer && ViewModel.ShowDetails ? Visibility.Visible : Visibility.Collapsed;
        TableHost.IsEnabled = !_drawer || !ViewModel.ShowDetails;
        PageFeedback.Visibility = ViewModel.HasFeedback && !(_drawer && ViewModel.ShowDetails) ? Visibility.Visible : Visibility.Collapsed;
        PanelFeedback.Visibility = ViewModel.HasFeedback && _drawer && ViewModel.ShowDetails ? Visibility.Visible : Visibility.Collapsed;
        SessionTable.IsTabStop = !_drawer || !ViewModel.ShowDetails;
        KeyboardNavigation.SetTabNavigation(SessionTable, _drawer && ViewModel.ShowDetails ? KeyboardNavigationMode.None : KeyboardNavigationMode.Continue);
        KeyboardNavigation.SetTabNavigation(DetailsPanel, _drawer ? KeyboardNavigationMode.Cycle : KeyboardNavigationMode.Continue);
        if (_drawer && ViewModel.ShowDetails)
        {
            if (ViewModel.HasFeedback && (!wasDrawer || !feedbackWasVisible)) RevealPanelFeedback();
            else if (!wasDrawer && ViewModel.Validation.Length > 0) RevealValidation(ViewModel.ValidationField);
        }
    }
    private void RevealPanelFeedback() => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
    {
        if (!_drawer || !ViewModel.ShowDetails || !ViewModel.HasFeedback) return;
        DetailsScroll.UpdateLayout();
        PanelFeedback.BringIntoView();
    }));
    private void Body_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        GameColumn.Width = Math.Max(170, SessionTable.ActualWidth - 652 - 44);
    }
    private void Table_SizeChanged(object sender, SizeChangedEventArgs e) => Body_SizeChanged(sender, e);
    private void Scrim_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, DrawerLayer)) return;
        ViewModel.CloseDetailsCommand.Execute(null);
        e.Handled = true;
    }
    private void GameLink_Click(object sender, RoutedEventArgs e) => e.Handled = true;
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try { await ViewModel.SaveAsync(); }
        catch (Exception error) { ViewModel.ReportError(error); }
    }
    private void View_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        if (ViewModel.IsImportOpen) ViewModel.CloseImportCommand.Execute(null);
        else if (ViewModel.IsEditing) ViewModel.CancelCommand.Execute(null);
        else ViewModel.CloseDetailsCommand.Execute(null);
        e.Handled = true;
    }
}
