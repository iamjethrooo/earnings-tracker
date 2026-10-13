using System.Text.RegularExpressions;

namespace nerve_earnings_tracker.Utilities
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
