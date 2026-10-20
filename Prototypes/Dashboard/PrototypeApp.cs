using System.IO;
using System.Windows;

namespace EarningsTracker.Prototypes;

// Deliberately independent of App, MainWindow, EF, update hooks and application services.
public sealed class PrototypeApp : Application
{
    [STAThread]
    public static int Main(string[] args)
    {
        var app = new PrototypeApp();
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/EarningsTracker;component/Resources/Styles.xaml", UriKind.Relative)
        });
        if (args.Length == 2 && args[0] is "--render" or "--render-sessions" or "--render-games")
        {
            try
            {
                if (args[0] == "--render-games") Games.GameLibraryVerification.Run(Path.GetFullPath(args[1]));
                else if (args[0] == "--render-sessions") Sessions.SessionExplorerVerification.Run(Path.GetFullPath(args[1]));
                else PrototypeVerification.Run(Path.GetFullPath(args[1]));
                return 0;
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(Path.GetFullPath(args[1]), "failure.txt"), ex.ToString());
                return 1;
            }
        }
        if (args.Length != 0 && !(args.Length == 1 && args[0] is "--sessions" or "--games"))
            return 2;
        var window = new PrototypeWindow();
        if (args.Length == 1) window.ViewModel.Page = args[0] == "--games" ? Enums.NavigationPage.Games : Enums.NavigationPage.Sessions;
        return app.Run(window);
    }
}
