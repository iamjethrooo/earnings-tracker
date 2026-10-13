using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Input;
using System.Windows.Automation.Peers;
using nerve_earnings_tracker.Views;
using nerve_earnings_tracker.Views.Modals;
using nerve_earnings_tracker.Models;
using nerve_earnings_tracker.ViewModels;
using nerve_earnings_tracker.Controls;
using System.Windows.Data;

namespace nerve_earnings_tracker.Tests;

[Collection("Wpf")]
public class SharedControlTests(WpfTestHost host)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task EscapeClosesDropdownBeforeDismissingModal(bool editable) => host.RunAsync(async () =>
    {
        var modal = new Modal { Visibility = Visibility.Collapsed };
        var combo = new ComboBox { IsEditable = editable, ItemsSource = new[] { "One", "Two" }, SelectedIndex = 0 };
        var window = new Window { Content = modal, Width = 1000, Height = 700,
            ShowInTaskbar = false, Left = -10000, Top = -10000 };
        try
        {
            window.Show();
            modal.Open("Test dropdown", combo);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            combo.IsDropDownOpen = true;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            combo.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(combo), 0, Key.Escape)
                { RoutedEvent = Keyboard.KeyDownEvent });
            Assert.False(combo.IsDropDownOpen);
            Assert.Equal(Visibility.Visible, modal.Visibility);
            combo.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(combo), 0, Key.Escape)
                { RoutedEvent = Keyboard.KeyDownEvent });
            Assert.Equal(Visibility.Collapsed, modal.Visibility);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task BookmarkAutomationNameIncludesGameAndCurrentAction() => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        using (var db = database.CreateContext())
        {
            db.Games.Add(new Game { Name = "Example" });
            db.SaveChanges();
        }
        var view = new GamesView(database.Options);
        var item = Assert.Single(((GamesViewModel)view.DataContext).Games);
        var list = (ListBox)view.FindName("GamesList");
        var card = (FrameworkElement)list.ItemTemplate.LoadContent();
        card.DataContext = item;
        card.Measure(new Size(1000, 400));
        card.Arrange(new Rect(0, 0, 1000, 400));
        card.UpdateLayout();
        var bookmark = Descendants<Button>(card).Single(b => ReferenceEquals(b.Command, item.ToggleBookmarkCommand));
        var peer = new ButtonAutomationPeer(bookmark);
        Assert.Equal("Bookmark Example", peer.GetName());
        item.ToggleBookmarkCommand.Execute(null);
        await Dispatcher.Yield();
        Assert.Equal("Remove bookmark for Example", peer.GetName());
    });

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    [Fact]
    public Task ModalContainsTabFocusAndRestoresInvokerAfterEscape() => host.RunAsync(async () =>
    {
        var invoker = new Button { Content = "Open" };
        var modal = new Modal { Visibility = Visibility.Collapsed };
        var disabledWhenOpen = new Style(typeof(Button));
        var trigger = new DataTrigger { Binding = new Binding(nameof(Modal.Visibility)) { Source = modal }, Value = Visibility.Visible };
        trigger.Setters.Add(new Setter(UIElement.IsEnabledProperty, false));
        disabledWhenOpen.Triggers.Add(trigger);
        invoker.Style = disabledWhenOpen;
        var grid = new Grid();
        grid.Children.Add(invoker);
        grid.Children.Add(modal);
        var window = new Window { Content = grid, Width = 1000, Height = 700, ShowInTaskbar = false,
            Left = -10000, Top = -10000 };
        try
        {
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Keyboard.Focus(invoker);
            Assert.Same(invoker, Keyboard.FocusedElement);
            var first = new TextBox();
            var last = new TextBox();
            var content = new StackPanel();
            content.Children.Add(first);
            content.Children.Add(last);
            modal.Open("Test dialog", content);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.False(invoker.IsEnabled);
            Assert.Same(first, Keyboard.FocusedElement);
            first.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            Assert.Same(last, Keyboard.FocusedElement);
            last.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            Assert.True(modal.IsKeyboardFocusWithin);
            ((UIElement)Keyboard.FocusedElement).MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            Assert.Same(first, Keyboard.FocusedElement);
            first.MoveFocus(new TraversalRequest(FocusNavigationDirection.Previous));
            Assert.True(modal.IsKeyboardFocusWithin);
            modal.SetHeaderVisible(false);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            first.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(first), 0, Key.Escape)
                { RoutedEvent = Keyboard.KeyDownEvent });
            Assert.Equal(Visibility.Visible, modal.Visibility); // Preserve completed-import Done behavior.
            modal.SetHeaderVisible(true);
            first.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(first), 0, Key.Escape)
                { RoutedEvent = Keyboard.KeyDownEvent });
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Null(modal.ModalContent);
            Assert.Equal(Visibility.Collapsed, modal.Visibility);
            Assert.True(invoker.IsEnabled);
            Assert.Same(invoker, Keyboard.FocusedElement);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task EditablePayoutFieldsExposeNamesToAutomation() => host.RunAsync(() =>
    {
        var view = new PayoutEditView();
        foreach (var name in new[] { "PayoutDate", "PayoutType", "Description", "Status", "Amount", "Reason" })
        {
            var field = (UIElement)view.FindName(name);
            var peer = UIElementAutomationPeer.CreatePeerForElement(field);
            Assert.False(string.IsNullOrWhiteSpace(peer.GetName()));
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task SessionRowEnterOpensGameButEmbeddedControlsDoNot() => host.RunAsync(() =>
    {
        var view = new SessionsView(); // No Loaded event: do not open the production database.
        var list = (ListBox)view.FindName("SessionsList");
        var game = new Game { Name = "Example" };
        var item = new SessionItemViewModel(new Session { Game = game });
        var button = new Button { Content = "Edit" };
        var row = new ListBoxItem { Content = button, DataContext = item };
        list.ItemsSource = new[] { row };
        view.Measure(new Size(1000, 700));
        view.Arrange(new Rect(0, 0, 1000, 700));
        view.UpdateLayout();
        var opened = 0;
        view.ViewGameRequested += (_, value) => { Assert.Same(game, value); opened++; };
        KeyEventArgs Enter() => new(Keyboard.PrimaryDevice, new FakePresentationSource(), 0, Key.Enter)
            { RoutedEvent = Keyboard.KeyDownEvent };
        row.RaiseEvent(Enter());
        Assert.Equal(1, opened);
        button.RaiseEvent(Enter());
        Assert.Equal(1, opened);
        item.BeginEdit();
        row.RaiseEvent(Enter());
        Assert.Equal(1, opened);
        return Task.CompletedTask;
    });

    private sealed class FakePresentationSource : PresentationSource
    {
        public override Visual RootVisual { get; set; } = null!;
        public override bool IsDisposed => false;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }

    [Fact]
    public Task PrimaryButtonTextMeetsContrastForNormalAndHoverFill() => host.RunAsync(() =>
    {
        var label = new TextBlock { Text = "Import" };
        var button = new Button { Style = (Style)Application.Current.FindResource("PrimaryButton"), Content = label };
        button.Measure(new Size(200, 100));
        button.Arrange(new Rect(0, 0, 200, 100));
        button.UpdateLayout();
        Assert.Equal(((SolidColorBrush)button.Foreground).Color, ((SolidColorBrush)label.Foreground).Color);
        button.Content = "Import";
        button.Measure(new Size(200, 100));
        button.Arrange(new Rect(0, 0, 200, 100));
        button.UpdateLayout();
        var generatedLabel = Assert.Single(Descendants<TextBlock>(button));
        Assert.Equal(((SolidColorBrush)button.Foreground).Color, ((SolidColorBrush)generatedLabel.Foreground).Color);
        foreach (var key in new[] { "AccentColor", "AccentHoverColor" })
        {
            var fill = ((SolidColorBrush)Application.Current.FindResource(key)).Color;
            var text = ((SolidColorBrush)button.Foreground).Color;
            Assert.True((Luminance(fill) + 0.05) / (Luminance(text) + 0.05) >= 4.5);
        }
        return Task.CompletedTask;
    });

    private static double Luminance(Color color)
    {
        static double Linear(byte value)
        {
            var channel = value / 255.0;
            return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
    }

    [Theory]
    [InlineData(Orientation.Vertical)]
    [InlineData(Orientation.Horizontal)]
    public Task ScrollbarUsesViewportAndSupportsBothDirections(Orientation orientation) => host.RunAsync(() =>
    {
        var bar = new ScrollBar { Style = (Style)Application.Current.FindResource(typeof(ScrollBar)),
            Orientation = orientation, Maximum = 300, ViewportSize = 100 };
        bar.ApplyTemplate();
        var track = Assert.IsType<Track>(bar.Template.FindName("PART_Track", bar));
        bar.Measure(new Size(400, 400));
        bar.Arrange(orientation == Orientation.Vertical ? new Rect(0, 0, 25, 400) : new Rect(0, 0, 400, 25));
        bar.UpdateLayout();
        var initialLength = orientation == Orientation.Vertical ? track.Thumb.ActualHeight : track.Thumb.ActualWidth;
        bar.ViewportSize = 300;
        bar.UpdateLayout();
        var enlargedLength = orientation == Orientation.Vertical ? track.Thumb.ActualHeight : track.Thumb.ActualWidth;
        Assert.True(enlargedLength > initialLength);
        Assert.Equal(300, track.ViewportSize);
        Assert.Equal(orientation, track.Orientation);
        Assert.Equal(Visibility.Visible, bar.Visibility);
        Assert.Equal(orientation == Orientation.Vertical, track.IsDirectionReversed);
        Assert.Same(orientation == Orientation.Vertical ? ScrollBar.PageUpCommand : ScrollBar.PageLeftCommand,
            track.DecreaseRepeatButton.Command);
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ComboBoxPopupTracksOpenAndCloseAndPreservesSelection(bool editable) => host.RunAsync(async () =>
    {
        var combo = new ComboBox { Style = (Style)Application.Current.FindResource(typeof(ComboBox)),
            IsEditable = editable, ItemsSource = new[] { "One", "Two" }, SelectedIndex = 1 };
        var window = new Window { Content = combo, Width = 240, Height = 100,
            ShowActivated = false, ShowInTaskbar = false, Left = -10000, Top = -10000 };
        try
        {
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            combo.ApplyTemplate();
            var popup = Assert.IsType<Popup>(combo.Template.FindName("PART_Popup", combo));
            combo.IsDropDownOpen = true;
            await Dispatcher.Yield();
            Assert.True(popup.IsOpen);
            combo.IsDropDownOpen = false;
            await Dispatcher.Yield();
            Assert.False(popup.IsOpen);
            Assert.Equal("Two", combo.SelectedItem);
            if (editable)
            {
                var input = Assert.IsType<TextBox>(combo.Template.FindName("PART_EditableTextBox", combo));
                Assert.Equal("Two", input.Text);
                Assert.Same(combo.Foreground, input.Foreground);
                input.Text = "Custom";
                Assert.Equal("Custom", combo.Text);
            }
        }
        finally { window.Close(); }
    });
}
