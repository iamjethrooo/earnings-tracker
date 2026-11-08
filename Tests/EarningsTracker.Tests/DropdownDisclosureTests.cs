using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Markup;
using EarningsTracker.Controls;

namespace EarningsTracker.Tests;

[Collection("Wpf")]
[Trait("Category", "Regression")]
public class DropdownDisclosureTests(WpfTestHost host)
{
    private sealed record Choice(string Label);
    private sealed class Selection : INotifyPropertyChanged
    {
        private Choice? _value;
        public Choice? Value
        {
            get => _value;
            set { _value = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value))); }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    [Fact]
    public Task SearchDoesNotChangeBoundSelectionOrAnotherSelectorAndCommitsOriginalIdentity() => host.RunAsync(async () =>
    {
        var choices = new ObservableCollection<Choice>([new("Hades · rate 1"), new("Hades · rate 2"), new("Stardew Valley")]);
        var selection = new Selection { Value = choices[0] };
        var combo = Searchable(choices);
        combo.SetBinding(Selector.SelectedItemProperty, new Binding(nameof(Selection.Value)) { Source = selection, Mode = BindingMode.TwoWay });
        var other = Searchable(choices);
        other.SelectedItem = choices[2];
        var panel = new StackPanel(); panel.Children.Add(combo); panel.Children.Add(other);
        var window = WindowFor(panel);
        var selectionEvents = 0;
        combo.SelectionChanged += (_, _) => selectionEvents++;
        try
        {
            window.Show();
            await Idle();
            selectionEvents = 0;
            combo.IsDropDownOpen = true;
            await Idle();
            var search = Part<TextBox>(combo, "PART_SearchBox");
            var results = Part<ListBox>(combo, "PART_SearchResults");
            search.Text = "RATE 2";
            await Idle();
            Assert.Same(choices[1], Assert.Single(results.Items.Cast<Choice>()));
            Assert.Same(choices[0], combo.SelectedItem);
            Assert.Same(choices[0], selection.Value);
            Assert.Equal(3, other.Items.Count);
            Assert.Same(choices[2], other.SelectedItem);
            Assert.Equal(0, selectionEvents);
            Press(search, Key.Enter);
            await Idle();
            Assert.False(combo.IsDropDownOpen);
            Assert.Same(choices[1], selection.Value);
            Assert.Equal(1, selectionEvents);
            Assert.True(BindingOperations.IsDataBound(combo, Selector.SelectedItemProperty));
            Assert.Same(combo, Keyboard.FocusedElement);

            combo.IsDropDownOpen = true;
            await Idle();
            search.Text = "does not exist";
            await Idle();
            Assert.Empty(results.Items);
            Assert.Equal(Visibility.Visible, Part<TextBlock>(combo, "SearchEmpty").Visibility);
            Press(search, Key.Enter);
            Assert.True(combo.IsDropDownOpen);
            Assert.Same(choices[1], selection.Value);
            Press(search, Key.Escape);
            Assert.False(combo.IsDropDownOpen);
            Assert.Same(choices[1], selection.Value);

            combo.IsDropDownOpen = true;
            await Idle();
            Assert.Equal("", search.Text);
            Assert.Equal(3, results.Items.Count);
            choices.Add(new Choice("New game"));
            await Idle();
            Assert.Equal(4, results.Items.Count);
            search.Text = "New";
            await Idle();
            choices.RemoveAt(3);
            await Idle();
            Assert.Empty(results.Items);
            Assert.Same(choices[1], combo.SelectedItem);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task SearchKeyboardNavigationAndTabDoNotAccidentallyCommit() => host.RunAsync(async () =>
    {
        var choices = new ObservableCollection<Choice>([new("First"), new("Second"), new("Third")]);
        var combo = Searchable(choices); combo.SelectedItem = choices[0];
        var after = new Button { Content = "Next" };
        var panel = new StackPanel(); panel.Children.Add(combo); panel.Children.Add(after);
        var window = WindowFor(panel);
        try
        {
            window.Show(); await Idle(); combo.IsDropDownOpen = true; await Idle();
            var search = Part<TextBox>(combo, "PART_SearchBox");
            var results = Part<ListBox>(combo, "PART_SearchResults");
            Press(search, Key.Down);
            Assert.Same(choices[1], results.SelectedItem);
            Assert.Same(choices[0], combo.SelectedItem);
            Press(search, Key.Tab);
            await Idle();
            Assert.False(combo.IsDropDownOpen);
            Assert.Same(choices[0], combo.SelectedItem);
            Assert.Same(after, Keyboard.FocusedElement);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(320, 96)]
    [InlineData(740, 96)]
    [InlineData(740, 144)]
    public Task SharedChoicesAndDisclosureRenderAndRetainNativeAutomation(int width, int dpi) => host.RunAsync(async () =>
    {
        var sort = new ComboBox { ItemsSource = new[] { "Newest first", "Oldest first", "Highest earnings" }, SelectedIndex = 0 };
        var combo = Searchable(new ObservableCollection<Choice>([new("Hades · rate 1"), new("Hades · rate 2"), new("Stardew Valley")]));
        combo.SelectedIndex = 0;
        var expander = new Expander { Header = "How calculated", Content = new TextBlock { Text = "Stored verified earnings retain their original values.", TextWrapping = TextWrapping.Wrap } };
        var panel = new StackPanel();
        panel.Children.Add(sort); panel.Children.Add(combo); panel.Children.Add(expander);
        var surface = new Border { Padding = new Thickness(20), Child = panel };
        var window = WindowFor(surface); window.Width = width;
        try
        {
            window.Show(); await Idle();
            var provider = (IExpandCollapseProvider)new ExpanderAutomationPeer(expander).GetPattern(PatternInterface.ExpandCollapse)!;
            var header = Part<ToggleButton>(expander, "DisclosureToggle");
            provider.Expand(); await Idle();
            Assert.True(header.IsChecked);
            Assert.Equal(Visibility.Visible, Part<ContentPresenter>(expander, "DisclosureContent").Visibility);
            Assert.Same(Application.Current.FindResource("AccentTextColor"), header.Foreground);
            var headerText = Descendants<TextBlock>(header).Single(text => text.Text == "How calculated");
            Assert.Same(header.Foreground, headerText.Foreground);
            Assert.True(header.ActualWidth <= expander.ActualWidth);
            var toggle = Part<ToggleButton>(sort, "ToggleButton");
            var arrow = Assert.IsType<System.Windows.Shapes.Path>(toggle.Template.FindName("Arrow", toggle));
            var bounds = arrow.TransformToAncestor(sort).TransformBounds(new Rect(arrow.RenderSize));
            Assert.True(bounds.Right <= sort.ActualWidth);
            Assert.True(bounds.Left > 0);
            Capture(surface, $"disclosures-{width}-{dpi}", dpi);
            provider.Collapse(); await Idle();
            Assert.Equal(Visibility.Collapsed, Part<ContentPresenter>(expander, "DisclosureContent").Visibility);
            header.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
            Assert.True(expander.IsExpanded);

            sort.IsDropDownOpen = true; await Idle();
            var nativePopup = Part<Popup>(sort, "PART_Popup");
            Assert.IsType<ComboBoxItem>(sort.ItemContainerGenerator.ContainerFromIndex(0));
            Capture((FrameworkElement)nativePopup.Child, $"text-choices-{width}-{dpi}", dpi);
            sort.IsDropDownOpen = false;
            combo.IsDropDownOpen = true; await Idle();
            Assert.True(Part<ListBox>(combo, "PART_SearchResults").ActualHeight > 0);
            Assert.True(Part<Popup>(combo, "PART_Popup").Child.RenderSize.Width <= width);
            Capture((FrameworkElement)Part<Popup>(combo, "PART_Popup").Child, $"search-choices-{width}-{dpi}", dpi);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task CustomTemplateSearchKeepsBoundedRecyclingAndMouseActivationInModal() => host.RunAsync(async () =>
    {
        var choices = new ObservableCollection<Choice>(Enumerable.Range(0, 1500).Select(index => new Choice($"Game {index:D4}")));
        var combo = Searchable(choices); combo.DisplayMemberPath = ""; combo.SelectedIndex = 0;
        DropdownSearch.SetMemberPath(combo, nameof(Choice.Label));
        combo.ItemTemplate = (DataTemplate)XamlReader.Parse("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <StackPanel><TextBlock Text="{Binding Label}"/><TextBlock Text="Recorded rate version"/></StackPanel>
            </DataTemplate>
            """);
        var modal = new Modal { Visibility = Visibility.Collapsed };
        var window = WindowFor(modal);
        try
        {
            window.Show(); modal.Open("Choose a game", combo); await Idle();
            combo.IsDropDownOpen = true; await Idle();
            var search = Part<TextBox>(combo, "PART_SearchBox");
            var results = Part<ListBox>(combo, "PART_SearchResults");
            Assert.True(Part<Popup>(combo, "PART_Popup").Child.RenderSize.Height <= combo.MaxDropDownHeight);
            Assert.True(Descendants<ListBoxItem>(results).Count() < 100);
            Assert.Same(combo.ItemTemplate, results.ItemTemplate);
            search.Text = "Game 1499"; await Idle();
            var match = Assert.Single(results.Items.Cast<Choice>());
            Assert.Same(choices[1499], match);
            var row = Assert.IsType<ListBoxItem>(results.ItemContainerGenerator.ContainerFromItem(match));
            row.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                { RoutedEvent = Mouse.PreviewMouseUpEvent });
            Assert.Same(choices[1499], combo.SelectedItem);
            Assert.False(combo.IsDropDownOpen);
            Assert.Equal(Visibility.Visible, modal.Visibility);

            combo.IsDropDownOpen = true; await Idle();
            search.Text = "no match";
            Press(search, Key.Escape);
            Assert.False(combo.IsDropDownOpen);
            Assert.Equal(Visibility.Visible, modal.Visibility);
            combo.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(combo), 0, Key.Escape)
                { RoutedEvent = Keyboard.KeyDownEvent });
            Assert.Equal(Visibility.Collapsed, modal.Visibility);
        }
        finally { window.Close(); }
    });

    private static ComboBox Searchable(ObservableCollection<Choice> choices) => new()
    {
        ItemsSource = choices, DisplayMemberPath = nameof(Choice.Label),
        Style = (Style)Application.Current.FindResource("SearchableComboBox")
    };
    private static T Part<T>(Control owner, string name) where T : class => Assert.IsType<T>(owner.Template.FindName(name, owner));
    private static Window WindowFor(UIElement content) => new()
    {
        Content = content, Width = 740, Height = 550, ShowInTaskbar = false, Left = -10000, Top = -10000,
        Background = (Brush)Application.Current.FindResource("DefaultBackgroundColor"),
        FontFamily = (FontFamily)Application.Current.FindResource("AppFont")
    };
    private static async Task Idle() => await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    private static void Press(UIElement target, Key key) => target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,
        PresentationSource.FromVisual(target), 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
    private static void Capture(FrameworkElement element, string name, int dpi)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "DropdownDisclosureVisuals");
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth * dpi / 96),
            (int)Math.Ceiling(element.ActualHeight * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
            context.DrawRectangle(new VisualBrush(element)
            {
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(0, 0, element.ActualWidth, element.ActualHeight)
            }, null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        bitmap.Render(drawing);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(directory, name + ".png")); encoder.Save(file);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
