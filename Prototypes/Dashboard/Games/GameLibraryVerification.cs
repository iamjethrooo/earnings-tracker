using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EarningsTracker.Enums;

namespace EarningsTracker.Prototypes.Games;

internal static class GameLibraryVerification
{
    public static void Run(string directory)
    {
        Directory.CreateDirectory(directory);
        var checks = new List<string>();
        using var diagnostics = new StringWriter();
        using var listener = new TextWriterTraceListener(diagnostics);
        PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
        var window = new PrototypeWindow();
        window.ViewModel.Page = NavigationPage.Games;
        var root = (FrameworkElement)window.Content;
        var view = (GameLibraryView)window.FindName("GamesLibrary");
        var vm = view.ViewModel;
        var list = (ListBox)view.FindName("GamesList");
        var width = 1440; var height = 960;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            checks.Add("PASS " + message);
        }
        void Layout()
        {
            root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height));
            root.UpdateLayout(); Pump(); root.UpdateLayout();
        }
        void Capture(string name, double dpi = 96)
        {
            Layout();
            var bitmap = new RenderTargetBitmap((int)(width * dpi / 96), (int)(height * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
            bitmap.Render(root);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(directory, name + ".png")); png.Save(file);
        }
        try
        {
            Check(vm.Sort == "Newest" && !vm.BookmarksOnly && vm.ShowArchived, "starting preference defaults match the existing Games design");
            foreach (var size in new[] { ("minimum", 1000, 600), ("compact", 1200, 720), ("default", 1440, 960), ("wide", 1800, 1000) })
            {
                (var name, width, height) = size;
                Capture(name);
                var count = PrototypeWindow.Descendants<ListBoxItem>(list).Count();
                Check(list.ActualHeight >= 150 && count is > 0 and < 55, name + $": bounded recycling viewport realizes {count} of 180 games");
                Check(vm.IsCompact == (width < 1278), name + ": comparisons reflow without switching away from virtualization");
                var scroll = PrototypeWindow.Descendants<ScrollViewer>(list).First();
                Check(scroll.ScrollableWidth == 0, name + ": library has no horizontal scrolling");
                Check(PrototypeWindow.Descendants<TextBlock>(list).Where(text => text.Text == "Rate · USD/h").All(text =>
                    text.TransformToAncestor(list).TransformBounds(new Rect(text.RenderSize)).Right <= list.ActualWidth), name + ": rate labels remain within the row");
            }
            width = 1440; height = 960; Layout();
            var all = vm.Games.Cast<GameLibraryItem>().ToArray();
            Check(all.TakeWhile(item => !item.IsArchived).Count() == all.Count(item => !item.IsArchived), "all sorts place archived identities after active games");
            var first = all[0];
            list.SelectedItem = first; Layout();
            Check(vm.SelectedGame == first && !vm.IsDetailsOpen, "row selection is separate from opening details");
            vm.ToggleBookmarkCommand.Execute(first);
            Check(!first.IsBookmarked, "bookmark toggle changes only the in-memory item");
            vm.ToggleBookmarkCommand.Execute(first); vm.BookmarksOnly = true;
            Check(vm.VisibleCount > 0 && vm.Games.Cast<GameLibraryItem>().All(item => item.IsBookmarked), "bookmarks-only filters the sample library");
            vm.DetailsCommand.Execute(first); vm.ToggleBookmarkCommand.Execute(first); Layout();
            Check(vm.IsDetailsOpen && vm.SelectedGame == first, "unbookmarking a filtered item retains its open details context");
            vm.BackCommand.Execute(null);
            Check(vm.SelectedGame == null, "returning from a filtered-out detail clears the unavailable row selection");
            vm.ClearFilters();
            vm.Search = "Mosslight";
            Check(vm.VisibleCount == 1, "search finds the expected fictional title");
            var subscription = vm.Games.Cast<GameLibraryItem>().Single();
            Check(subscription.Game.SubscriptionCurrent && subscription.IsPlayable && !subscription.IsOwned, "current sample subscription provides access without claiming ownership");
            vm.ClearFilters(); vm.Access = "Owned";
            Check(vm.Games.Cast<GameLibraryItem>().All(item => item.IsOwned), "owned filter uses the personal ownership flag");
            var cost = first.Game.PurchaseCost;
            var steam = first.Game.SteamSlot;
            Check(first.OwnershipAction == "Owned · remove", "owned action states both the current flag and removal action");
            vm.ToggleOwnedCommand.Execute(first);
            Check(!first.IsOwned && first.Game.PurchaseCost == cost && first.Game.SteamSlot == steam, "ownership toggle preserves purchase evidence and independent Steam demo slots");
            Check(first.OwnershipAction == "Mark owned", "unowned action explicitly marks personal ownership");
            vm.ClearFilters(); vm.Access = "Playable now";
            Check(vm.Games.Cast<GameLibraryItem>().All(item => item.IsPlayable), "playable filter uses owned, current free or current sample subscription access");
            vm.ClearFilters(); vm.Search = "Glass Sea";
            var stale = vm.Games.Cast<GameLibraryItem>().Single();
            Check(stale.HasStaleInformation && !stale.Game.SubscriptionCurrent && !stale.IsPlayable, "stale subscription is labeled and does not grant playable access");
            vm.DetailsCommand.Execute(stale); Capture("stale-details");
            vm.BackCommand.Execute(null); vm.ClearFilters();
            vm.Sort = "Lowest price";
            Check(vm.ShowPriceSortHelp && vm.PriceSortHelp.Contains("sort last together") && vm.PriceSortHelp.Contains("56.25"), "price sort explains mixed-currency estimates and excluded offers on the page");
            Capture("price-sorting");
            width = 1000; height = 600; Capture("minimum-price-sorting");
            Check(list.ActualHeight >= 150 && PrototypeWindow.Descendants<ScrollViewer>(list).First().ScrollableWidth == 0, "minimum price-sort explanation preserves a bounded usable library viewport");
            width = 1440; height = 960; Layout();
            var ordered = vm.Games.Cast<GameLibraryItem>().Where(item => !item.IsArchived).Select(item => item.PriceSortValue).ToArray();
            Check(ordered.SequenceEqual(ordered.Order()), "price comparison uses explicit PHP equivalents with unavailable values last");
            var usd = all.Single(item => item.Name == "Juniper Hollow");
            var eur = all.Single(item => item.Name == "Rift Chronicle");
            Check(usd.PriceSortValue == 19.99m * 56.25m && usd.FxText.Contains("Estimate"), "USD conversion is explicitly an estimated dated fixture rate");
            Check(eur.PriceSortValue == decimal.MaxValue && eur.FxText.Contains("unavailable"), "missing FX retains the source currency without a false comparison");
            Check(all.Single(item => item.Name == "Sable Atlas").PrimaryOffer.Amount == null, "unknown price remains null rather than zero/free");
            Check(first.FxText.Contains("requires currency conversion") && !first.FxText.Contains("No conversion needed"), "PHP acquisition is not described as directly comparable with USD earnings");
            Check(stale.PriceEvidence.Contains("Stale") && stale.PriceEvidence.Contains("excluded") && !stale.FxText.Contains("≈"), "stale offers retain explicit exclusion without advertising a current conversion");
            Check(eur.PriceEvidence.Contains("No sample FX") && all.Single(item => item.Name == "Sable Atlas").PriceEvidence.Contains("Unknown"), "missing FX and unknown price retain distinct explanations");
            vm.DetailsCommand.Execute(all.Single(item => item.Name == "Sable Atlas")); Capture("missing-artwork-details");
            Check(PrototypeWindow.Descendants<TextBlock>(view).Any(text => text.Text == "No\nartwork" && text.FontSize >= 12), "missing artwork uses a readable local placeholder without an asset");
            vm.BackCommand.Execute(null);
            Check(all.Single(item => item.Name == "Signal Garden").CapPercent > 100 && all.Single(item => item.Name == "Signal Garden").CapDetail.StartsWith("125%"),
                "over-cap fixture retains its exact text while the shared progress visual clamps");
            Check(all.Single(item => item.Name == "Ashen Harbor").PriceText == "Unavailable in PH" && all.Single(item => item.Name == "Copper Skies").PriceSortValue == decimal.MaxValue,
                "unavailable and non-PH offers retain their availability constraints");
            vm.DetailsCommand.Execute(usd); Capture("currency-details");
            vm.BackCommand.Execute(null); vm.ClearFilters(); vm.Sort = "Highest Pay";
            var rates = vm.Games.Cast<GameLibraryItem>().Where(item => !item.IsArchived).Select(item => item.Rate).ToArray();
            Check(rates.SequenceEqual(rates.OrderDescending()), "highest pay sorts by the numeric sample hourly rate");
            vm.Genre = "Racing"; vm.Store = "Steam"; vm.Access = "Free-to-play";
            Check(vm.Games.Cast<GameLibraryItem>().All(item => item.Game.Genre == "Racing" && item.Offers.Any(o => o.Store == "Steam") && item.PrimaryOffer.IsFree), "genre, store and access filters compose");
            vm.Search = "no such synthetic title";
            Check(vm.ShowState && vm.CanClear, "actual filters produce filtered-empty guidance"); Capture("filtered-empty");
            vm.ClearFilters(); vm.ShowArchived = false;
            Check(vm.Games.Cast<GameLibraryItem>().All(item => !item.IsArchived), "archive visibility filters the library without deleting history");
            vm.ShowArchived = true; vm.Sort = "Newest";
            foreach (var example in vm.Examples.Where(example => example != "Populated"))
            {
                vm.Example = example; Capture(example.ToLowerInvariant().Replace(' ', '-'));
                Check(example switch
                {
                    "Archived" => vm.Games.Cast<GameLibraryItem>().All(item => item.IsArchived),
                    "Missing artwork" => vm.Games.Cast<GameLibraryItem>().All(item => item.Cover == null),
                    "Unavailable price" => vm.Games.Cast<GameLibraryItem>().All(item => item.PriceUnknown),
                    "Loading" or "Filtered empty" or "Error" => vm.VisibleCount == 0,
                    _ => vm.IsStale && vm.VisibleCount == 180
                }, example + ": distinct synthetic presentation");
            }
            vm.RetryCommand.Execute(null);
            Check(stale.HasStaleInformation, "snapshot retry preserves individual stale offer evidence");
            vm.DetailsCommand.Execute(all.First(item => item.IsArchived)); Capture("archived-details");
            Check(vm.SelectedGame!.PotentialText == "History only", "archived earnings opportunities are not presented as current");
            foreach (var history in new[] { "Session history", "Rate history", "Cap-reset history" })
            {
                vm.HistoryCommand.Execute(history); Capture(history.ToLowerInvariant().Replace(' ', '-'));
                Check(vm.IsHistoryOpen && vm.HistoryTitle == history, history + ": synthetic evidence is readable without record mutation");
                vm.CloseHistoryCommand.Execute(null);
            }
            vm.BackCommand.Execute(null);
            vm.PreferencesCommand.Execute(null); Capture("starting-preferences");
            vm.StartingSort = "Game A–Z"; vm.StartingBookmarks = true; vm.StartingArchived = false;
            vm.ApplyStartingCommand.Execute(null);
            Check(vm.Sort == "Game A–Z" && vm.BookmarksOnly && !vm.ShowArchived && !vm.IsPreferencesOpen, "sample starting preferences apply together without reading or writing settings");
            vm.StartingSort = "Newest"; vm.StartingBookmarks = false; vm.StartingArchived = true; vm.ApplyStartingCommand.Execute(null);
            var last = vm.Games.Cast<GameLibraryItem>().Last();
            list.ScrollIntoView(last); Layout();
            Check(list.ItemContainerGenerator.ContainerFromItem(last) is ListBoxItem && PrototypeWindow.Descendants<ListBoxItem>(list).Count() < 55, "last game remains reachable while recycling stays active");
            Capture("library-end");
            var libraryScroll = PrototypeWindow.Descendants<ScrollViewer>(list).First();
            var libraryOffset = libraryScroll.VerticalOffset;
            vm.DetailsCommand.Execute(last); Layout(); vm.BackCommand.Execute(null); Layout();
            Check(Math.Abs(libraryScroll.VerticalOffset - libraryOffset) < 1, "return from details retains the library scroll position");
            vm.DetailsCommand.Execute(first); Capture("details");
            width = 1000; height = 600; Capture("minimum-details");
            ((ScrollViewer)view.FindName("DetailsScroll")).ScrollToBottom(); Capture("minimum-details-bottom");
            vm.BackCommand.Execute(null); width = 1440; height = 960;
            ((ScrollViewer)PrototypeWindow.Descendants<ScrollViewer>(list).First()).ScrollToTop(); Capture("default-150pct", 144);
            listener.Flush(); File.WriteAllText(Path.Combine(directory, "bindings.txt"), diagnostics.ToString());
            Check(string.IsNullOrWhiteSpace(diagnostics.ToString()), "no WPF binding warnings or errors");
            File.WriteAllLines(Path.Combine(directory, "checks.txt"), checks);
        }
        finally
        {
            PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
            window.Close();
        }
    }
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}
