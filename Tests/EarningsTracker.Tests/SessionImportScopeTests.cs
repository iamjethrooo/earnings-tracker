using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EarningsTracker.Models;
using EarningsTracker.Views;
using EarningsTracker.Views.Modals;

namespace EarningsTracker.Tests;

[Collection("Wpf")]
[Trait("Category", "Regression")]
public class SessionImportScopeTests(WpfTestHost host)
{
    [Fact]
    public Task GameDetailsImportIsCollapsedAfterOpeningDetails() => host.RunAsync(() =>
    {
        using var database = new TestDatabase();
        var game = new Game { Name = "Example" };
        using (var db = database.CreateContext()) { db.Games.Add(game); db.SaveChanges(); }
        var view = new GamesView(database.Options);
        view.OpenGameDetails(game);
        view.Measure(new Size(960, 768));
        view.Arrange(new Rect(0, 0, 960, 768));
        view.UpdateLayout();
        Assert.Equal(Visibility.Visible, ((Grid)view.FindName("GameDetailsView")).Visibility);
        var button = (Button)view.FindName("AddSessionButton");
        Assert.Equal(Visibility.Collapsed, button.Visibility);
        Assert.False(button.IsVisible);
        Assert.Equal(new Size(0, 0), button.DesiredSize);
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData("Example\nvideo.mkv · 6/5/2026 12:00 PM · 1m\n+$1.00\nVerified", true)]
    [InlineData("recording-20260605120000123.mkv\n6/5/2026 · 1m\n+$1.00\nVerified", false)]
    public Task AccessibleImporterPreviewsOnlyRecordings(string text, bool supported) => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        using (var db = database.CreateContext())
        {
            db.Games.Add(new Game { Name = "Example", RatePerHour = 1m, MinutesCap = 60m });
            db.SaveChanges();
        }
        using var view = new ImportSessionsView(database.Options);
        view.Measure(new Size(700, 450));
        view.Arrange(new Rect(0, 0, 700, 450));
        view.UpdateLayout();
        Assert.Empty(Descendants<ComboBox>(view));
        Assert.Empty(Descendants<RadioButton>(view));
        ((TextBox)view.FindName("ImportTextBox")).Text = text;
        Descendants<Button>(view).Single(button => button.Content as string == "Parse Import")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await view.OperationCompletion;
        var summary = (ImportResultModal)view.FindName("ImportResultModal");
        Assert.Equal(supported ? Visibility.Visible : Visibility.Collapsed,
            ((Grid)summary.FindName("PreviewPanel")).Visibility);
        if (supported)
            Assert.Equal("1 sessions found", ((TextBlock)summary.FindName("SessionsFoundText")).Text);
        else
            Assert.Equal("Import Failed", ((TextBlock)summary.FindName("ResultTitle")).Text);
        using var read = database.CreateContext();
        Assert.Empty(read.Sessions);
        Assert.Empty(read.SessionChanges);
    });

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
