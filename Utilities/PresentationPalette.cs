using System.Windows.Media;

namespace EarningsTracker.Utilities;

// Value types can be used by both XAML resources and presentation work on background threads.
public static class PresentationPalette
{
    public static Color SuccessText { get; } = Color.FromRgb(28, 218, 137);
    public static Color SuccessBackground { get; } = Color.FromRgb(11, 48, 32);
    public static Color ErrorText { get; } = Color.FromRgb(234, 67, 53);
    public static Color ErrorBackground { get; } = Color.FromRgb(58, 18, 19);
    public static Color WarningText { get; } = Color.FromRgb(243, 168, 24);
    public static Color WarningBackground { get; } = Color.FromRgb(74, 59, 11);
    public static Color NeutralText { get; } = Color.FromRgb(168, 168, 173);
    public static Color NeutralBackground { get; } = Color.FromRgb(37, 39, 46);
    public static Color PaymentText { get; } = Color.FromRgb(0, 201, 232);
    public static Color PaymentBackground { get; } = Color.FromRgb(6, 37, 42);
}
