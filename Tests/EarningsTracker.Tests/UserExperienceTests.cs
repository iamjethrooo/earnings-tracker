using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EarningsTracker.Enums;
using EarningsTracker.Models;
using EarningsTracker.Utilities;
using EarningsTracker.ViewModels;
using EarningsTracker.Views;
using EarningsTracker.Views.Modals;

namespace EarningsTracker.Tests;

[Collection("Wpf")]
[Trait("Category", "Regression")]
public class UserExperienceTests(WpfTestHost host)
{
    [Fact]
    public Task PreviewShowsIdentitySourcePrecisionAndBeforeValuesWithoutWriting() => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        var day = new DateTime(2026, 11, 21, 3, 13, 0);
        var game = new Game { Name = "Review game", RatePerHour = 2, MinutesCap = 3000 };
        using (var db = database.CreateContext())
        {
            db.Games.Add(game);
            db.Sessions.Add(new Session { Game = game, GameName = game.Name, Date = day, RecordedDate = day,
                Duration = TimeSpan.FromMinutes(24), Status = SessionStatus.Pending, Earnings = 0.6123m });
            db.Payouts.Add(new Payout { Type = "Payment", Status = "Paid", Date = day, Amount = -1, SourceId = "SYNTHETIC-REVIEW" });
            db.SaveChanges();
        }
        var imported = new Session { GameId = game.Id, GameName = game.Name, Date = day, RecordedDate = day,
            Duration = TimeSpan.FromMinutes(24), Status = SessionStatus.Verified, Earnings = 0.7123m };
        var added = new Session { GameId = game.Id, GameName = game.Name, Date = day.AddDays(1), RecordedDate = day.AddDays(1),
            Duration = TimeSpan.FromMinutes(30), Status = SessionStatus.Verified, Earnings = 1.2345m };
        var result = new SessionImporter(database.Options).PreviewImport([imported, added]);
        var summary = new ImportResultModal();
        summary.ShowPreview(result, [game]);
        var rows = ((ListBox)summary.FindName("ReviewRows")).Items.Cast<SessionImportReviewItem>().ToList();
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, row => row.Details.Contains("$1.2345") && row.Details.Contains($"game #{game.Id}"));
        Assert.Contains(rows, row => row.Change == "Update" && row.Before.Contains("$0.6123") && row.Details.Contains("Verified"));
        var picker = (ComboBox)summary.FindName("NewSessionsPayout");
        picker.SelectedIndex = picker.Items.Count - 1;
        Assert.Equal(((SessionPayoutOption)picker.SelectedItem).PayoutId, added.AssignedPayoutId);
        Assert.Contains(((ListBox)summary.FindName("ReviewRows")).Items.Cast<SessionImportReviewItem>(),
            row => row.Change == "New" && row.Assignment == ((SessionPayoutOption)picker.SelectedItem).Label);
        using var read = database.CreateContext();
        Assert.Equal(0.6123m, read.Sessions.Single().Earnings);
        Assert.Empty(read.SessionChanges);
        await Layout(summary, 700, 700);
        Capture(summary, "import-review");
    });

    [Fact]
    public Task ParseErrorReturnsToTheProblemLineAndKeepsInput() => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        using (var db = database.CreateContext())
        {
            db.Games.Add(new Game { Name = "Review game", RatePerHour = 1.5m, MinutesCap = 3000 });
            db.SaveChanges();
        }
        const string input = "Review game\nReview game\nReview game · Nov 21, 2026 3:13 AM · 24m · File deleted\n+$0.61\nUnknown status";
        var view = new ImportSessionsView(database.Options);
        var box = (TextBox)view.FindName("ImportTextBox");
        box.Text = input;
        await Layout(view, 700, 700);
        Descendants<Button>(view).Single(button => Equals(button.Content, "Preview import")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await view.OperationCompletion;
        var summary = (ImportResultModal)view.FindName("ImportResultModal");
        Assert.Equal("Import failed", ((TextBlock)summary.FindName("ResultTitle")).Text);
        ((Button)summary.FindName("ResultBackButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(input, box.Text);
        Assert.True(box.SelectionLength > 0, ((TextBlock)summary.FindName("ResultMessage")).Text);
        using var read = database.CreateContext();
        Assert.Empty(read.Sessions);
        view.Dispose();
    });

    [Theory]
    [InlineData(740, 560)]
    [InlineData(1212, 800)]
    public Task FirstRunActionsAndBackupAreVisibleWithoutIntegrations(double width, double height) => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        var dashboard = new DashboardView(database.Options);
        await dashboard.LoadDashboardData();
        await Layout(dashboard, width, height);
        Assert.Equal(Visibility.Visible, ((Border)dashboard.FindName("GettingStarted")).Visibility);
        Assert.Equal(Visibility.Collapsed, ((Grid)dashboard.FindName("AnalysisGrid")).Visibility);
        var sessions = 0; var payouts = 0;
        dashboard.ImportSessionsRequested += (_, _) => sessions++;
        dashboard.ImportPayoutsRequested += (_, _) => payouts++;
        foreach (var button in Descendants<Button>((Border)dashboard.FindName("GettingStarted")))
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, sessions); Assert.Equal(1, payouts);
        Capture(dashboard, $"dashboard-start-{width}");

        var settings = new SettingsView(database.Options);
        await Layout(settings, width, height);
        var backup = (Button)settings.FindName("BackupButton");
        var bounds = backup.TransformToAncestor(settings).TransformBounds(new Rect(backup.RenderSize));
        Assert.InRange(bounds.Bottom, 0, height);
        Assert.False(((Expander)settings.FindName("PriceRefreshSetup")).IsExpanded);
        Assert.False(((Expander)settings.FindName("SteamSetup")).IsExpanded);
        Capture(settings, $"settings-{width}");
    });

    private static async Task Layout(FrameworkElement view, double width, double height)
    {
        view.Width = width; view.Height = height;
        view.Measure(new Size(width, height)); view.Arrange(new Rect(0, 0, width, height)); view.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); view.UpdateLayout();
    }

    private static void Capture(FrameworkElement view, string name)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "UserExperienceVisuals");
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(directory, name + ".png")); encoder.Save(file);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
