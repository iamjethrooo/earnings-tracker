using System.Windows;
using Microsoft.EntityFrameworkCore;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using EarningsTracker.Models;
using EarningsTracker.Utilities;
using EarningsTracker.Views;
using EarningsTracker.Views.Modals;

namespace EarningsTracker.Tests
{
    [Collection("Wpf")]
    [Trait("Category", "Regression")]
    public class ReconciliationDisplayTests(WpfTestHost host)
    {
        [Theory]
        [InlineData("NeedsReview", false, "Verified", "Needs reconciliation", true, 0)]
        [InlineData("Unreconciled", false, "Verified", "Needs reconciliation", true, 0)]
        [InlineData("Unexpected", false, "Verified", "Needs reconciliation", true, 0)]
        [InlineData("Separate", true, "Verified", "Needs reconciliation", true, 0)]
        [InlineData("IncludedInSessions", false, "Verified", "Needs reconciliation", true, 0)]
        [InlineData("IncludedInSessions", true, "Verified", "Included in sessions", false, 0)]
        [InlineData("Separate", false, "Verified", "Counted separately", false, -2)]
        [InlineData("Separate", false, "Processing", "Counted separately", false, -2)]
        [InlineData("Separate", false, "Paid", "Counted separately", false, -2)]
        [InlineData("Separate", false, "Pending", "Separate · excluded", false, 0)]
        [InlineData("Separate", false, "Failed", "Separate · excluded", false, 0)]
        public Task RowExplainsReconciliationWithoutChangingSourceStatusOrSignedAmount(
            string state, bool included, string status, string label, bool needsReview, int earned) => host.RunAsync(() =>
        {
            var payout = new Payout { Date = new DateTime(2026, 6, 1), Type = "Earnings correction", Status = status,
                Amount = -2m, ReconciliationState = state, IsAdjustmentIncludedInSessionEarnings = included };
            var item = PayoutHistoryItem.FromPayout(payout);

            Assert.Equal(label, item.ReconciliationText);
            Assert.NotEmpty(item.ReconciliationExplanation);
            Assert.Equal(needsReview, item.NeedsReconciliation);
            Assert.Equal(status, item.Status);
            Assert.Equal(-2m, item.Amount);
            Assert.Equal(earned, PayoutAccounting.Calculate([], [payout]).TotalEarned);
            Assert.Equal(state, payout.ReconciliationState);
            Assert.Equal(included, payout.IsAdjustmentIncludedInSessionEarnings);
            return Task.CompletedTask;
        });

        [Theory]
        [InlineData("Bonus")]
        [InlineData("bonus")]
        [InlineData("Earnings correction")]
        public Task PendingReconciliationNoticeMatchesVisibleActiveRowsAndRefreshes(string type) => host.RunAsync(async () =>
        {
            using var database = new TestDatabase();
            using (var db = database.CreateContext())
            {
                db.Payouts.AddRange(
                    new Payout { Date = DateTime.Today, Type = type, Description = "Review me", Status = "Verified", Amount = 20m, ReconciliationState = "NeedsReview" },
                    new Payout { Date = DateTime.Today, Type = type, Description = "Hidden", Status = "Verified", Amount = 99m, ReconciliationState = "NeedsReview", IsDeleted = true },
                    new Payout { Date = DateTime.Today, Type = "Payment", Status = "Paid", Amount = -10m, ReconciliationState = "NeedsReview" });
                db.SaveChanges();
            }
            var view = new PayoutsView(database.Options);
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            try
            {
                await view.RefreshCompletion;
                var notice = Assert.IsType<TextBlock>(view.FindName("ReconciliationNotice"));
                Assert.Equal(Visibility.Visible, notice.Visibility);
                Assert.Contains("1 adjustment", notice.Text);
                Assert.Contains("excluded from Total Earned", notice.Text);
                Assert.Single(view.History, item => item.NeedsReconciliation);
                Assert.Equal("$0.00", Assert.IsType<TextBlock>(view.FindName("TotalEarnedValue")).Text);

                using var read = database.CreateContext();
                var payout = read.Payouts.Single(payout => payout.Type == type);
                payout.ReconciliationState = "Separate";
                PayoutService.Update(payout.Id, payout, "Reviewed", database.Options);
                await view.RefreshCompletion;
                await System.Windows.Threading.Dispatcher.Yield();

                Assert.Equal(Visibility.Collapsed, notice.Visibility);
                Assert.Equal("$20.00", Assert.IsType<TextBlock>(view.FindName("TotalEarnedValue")).Text);
                Assert.Equal("Counted separately", view.History.Single(item => item.Payout?.Id == payout.Id).ReconciliationText);
                using var auditRead = database.CreateContext();
                Assert.Single(auditRead.PayoutChanges);
                Assert.Equal(99m, auditRead.Payouts.IgnoreQueryFilters().Single(p => p.Description == "Hidden").Amount);
            }
            finally { view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); }
        });

        [Fact]
        public Task PaymentsDoNotReceiveAnAdjustmentReconciliationBadge() => host.RunAsync(() =>
        {
            var item = PayoutHistoryItem.FromPayout(new Payout { Type = "Payment", Status = "Processing", Amount = 10m });
            Assert.Equal(string.Empty, item.ReconciliationText);
            Assert.False(item.NeedsReconciliation);
            return Task.CompletedTask;
        });

        [Theory]
        [InlineData("Separate", true, false, false)]
        [InlineData("IncludedInSessions", false, false, false)]
        [InlineData("Separate", false, false, true)]
        [InlineData("IncludedInSessions", true, true, false)]
        public Task EditorRequiresExplicitResolutionForInconsistentReconciliation(
            string state, bool flag, bool includedSelected, bool separateSelected) => host.RunAsync(() =>
        {
            var view = new PayoutEditView(new Payout { Type = "Bonus", Status = "Verified", Amount = 20m,
                ReconciliationState = state, IsAdjustmentIncludedInSessionEarnings = flag });
            Assert.Equal(includedSelected, Assert.IsType<RadioButton>(view.FindName("IncludedRadio")).IsChecked == true);
            Assert.Equal(separateSelected, Assert.IsType<RadioButton>(view.FindName("SeparateRadio")).IsChecked == true);
            return Task.CompletedTask;
        });

        [Fact]
        public Task DescriptionTemplateShowsTheBadgeAndReadableExplanationWithoutAddingColumns() => host.RunAsync(async () =>
        {
            using var database = new TestDatabase();
            var view = new PayoutsView(database.Options);
            var list = Assert.IsType<ListView>(view.FindName("PayoutList"));
            var grid = Assert.IsType<GridView>(list.View);
            Assert.Equal(6, grid.Columns.Count);
            Assert.False(grid.AllowsColumnReorder);
        Assert.Equal(ScrollBarVisibility.Auto, ScrollViewer.GetHorizontalScrollBarVisibility(list));
            var column = Assert.Single(grid.Columns, column => Equals(column.Header, "DESCRIPTION"));
            var content = Assert.IsType<StackPanel>(column.CellTemplate.LoadContent());
            var item = PayoutHistoryItem.FromPayout(new Payout { Type = "Bonus", Status = "Verified", Amount = 20m,
                Description = "Original description", ReconciliationState = "NeedsReview" });
            content.DataContext = item;
            var badge = Assert.IsType<Border>(content.Children[1]);
            var tooltip = Assert.IsType<ToolTip>(badge.ToolTip);
            tooltip.PlacementTarget = badge;
            await Dispatcher.Yield();

            Assert.Equal("Original description", Assert.IsType<TextBlock>(content.Children[0]).Text);
            Assert.Equal(TextWrapping.Wrap, Assert.IsType<TextBlock>(content.Children[0]).TextWrapping);
            Assert.Equal("Needs reconciliation", Assert.IsType<TextBlock>(badge.Child).Text);
            Assert.Equal(item.ReconciliationExplanation, Assert.IsType<TextBlock>(tooltip.Content).Text);
            Assert.NotEqual(Colors.White, Assert.IsType<SolidColorBrush>(tooltip.Background).Color);
            Assert.NotEqual(Assert.IsType<SolidColorBrush>(tooltip.Background).Color,
                Assert.IsType<SolidColorBrush>(Assert.IsType<TextBlock>(tooltip.Content).Foreground).Color);

            content.DataContext = PayoutHistoryItem.FromPayout(new Payout { Type = "Payment", Status = "Paid", Amount = -20m });
            await Dispatcher.Yield();
            Assert.Equal(Visibility.Collapsed, badge.Visibility);
        });
    }
}
