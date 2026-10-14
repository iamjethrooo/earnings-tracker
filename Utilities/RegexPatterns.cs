using System.Text.RegularExpressions;

namespace EarningsTracker.Utilities
{
    public static partial class RegexPatterns
    {
        [GeneratedRegex(@"(\d+)\s*h")]
        public static partial Regex HourRegex();

        [GeneratedRegex(@"(\d+)\s*m")]
        public static partial Regex MinuteRegex();

        [GeneratedRegex(@"(\d+)\s*s")]
        public static partial Regex SecondRegex();
    }
}
