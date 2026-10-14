using EarningsTracker.Exceptions;
using EarningsTracker.Models;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;

namespace EarningsTracker.Utilities
{
    public static class SessionTextParser
    {
        // Nerve's pasted dates use month/day order; Windows locale must not change recording identity.
        private static readonly CultureInfo SourceDateCulture = CultureInfo.GetCultureInfo("en-US");
        public static TimeSpan ParseDuration(string input)
        {
            input = input.Trim();
            // The source gives only an upper bound here; retain the existing zero-duration representation.
            if (Regex.IsMatch(input, @"\A<\s*1\s*m\z"))
                return TimeSpan.Zero;

            var match = Regex.Match(input,
                @"\A(?:(?<hours>[0-9]+)\s*h\s*)?(?:(?<minutes>[0-9]+)\s*m\s*)?(?:(?<seconds>[0-9]+)\s*s\s*)?\z");
            if (!match.Success || input.Length == 0)
                throw new FormatException($"Invalid duration '{input}'. Use a value such as 1h 2m 3s.");

            try
            {
                int hours = match.Groups["hours"].Success ? int.Parse(match.Groups["hours"].Value, CultureInfo.InvariantCulture) : 0;
                int minutes = match.Groups["minutes"].Success ? int.Parse(match.Groups["minutes"].Value, CultureInfo.InvariantCulture) : 0;
                int seconds = match.Groups["seconds"].Success ? int.Parse(match.Groups["seconds"].Value, CultureInfo.InvariantCulture) : 0;
                return new TimeSpan(hours, minutes, seconds);
            }
            catch (Exception ex) when (ex is OverflowException or ArgumentOutOfRangeException)
            {
                throw new FormatException($"Invalid duration '{input}': the value is too large.", ex);
            }
        }

        private static bool LooksLikeGameDetailsFormat(string[] lines)
        {
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                if (line.Contains((char)183) && DateTime.TryParse(line.Split((char)183)[0].Trim(), SourceDateCulture, DateTimeStyles.None, out _)) // · character
                {
                    return true;
                }

                if (line.Contains("Nerve"))
                {
                    return true;
                }
                if (line.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase) && !line.Contains((char)183))
                    return true;
            }
            return false;
        }

        private static bool LooksLikeRecordingsFormat(string[] lines)
        {
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var parts = line.Split((char)183);
                if (parts.Length > 1 && DateTime.TryParse(parts[1].Trim(), SourceDateCulture, DateTimeStyles.None, out _)) // · character
                {
                    return true;
                }
            }
            return false;
        }

        private static List<Session> ParseGameDetails(string[] lines, List<Game> games, Game game)
        {
            List<Session> sessions = [];
            Session? currentSession = null;
            var selectedGame = ResolveGame(game.Name, games, game)!;
            var recordingLine = 0;
            var hasEarnings = false;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                if (line.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase) && !line.Contains((char)183))
                {
                    EnsureRecordingComplete(currentSession, recordingLine);
                    Match match = Regex.Match(line, @"(\d{17})");
                    if (!match.Success || !DateTime.TryParseExact(match.Value, "yyyyMMddHHmmssfff",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                        throw new InvalidSessionFormatException($"Line {i + 1}: the recording filename has no valid recording timestamp.");

                    var durationIndex = NextContentLine(lines, i + 1);
                    if (durationIndex == lines.Length)
                        throw new InvalidSessionFormatException($"Line {i + 1}: the recording is missing its duration.");

                    var durationText = lines[durationIndex].Trim();
                    if (durationText.Contains((char)183))
                    {
                        var parts = durationText.Split((char)183);
                        if (parts.Length != 2)
                            throw new InvalidSessionFormatException($"Line {durationIndex + 1}: expected recorded date · duration.");
                        var displayedDate = ReadRecordedDate(parts[0].Trim(), durationIndex + 1);
                        if (displayedDate.Date != date.Date || (displayedDate.TimeOfDay != TimeSpan.Zero
                            && (displayedDate.Hour != date.Hour || displayedDate.Minute != date.Minute)))
                            throw new InvalidSessionFormatException($"Line {durationIndex + 1}: the filename and displayed recording dates disagree. Correct the recording metadata before importing.");
                        durationText = parts[1].Trim();
                    }
                    // A filename contains seconds/milliseconds that the displayed date may omit.
                    currentSession = new Session
                    {
                        Date = date,
                        RecordedDate = date,
                        Duration = ReadDuration(durationText, durationIndex + 1),
                        GameName = selectedGame.Name,
                        GameId = selectedGame.Id
                    };
                    recordingLine = i + 1;
                    hasEarnings = false;
                    i = durationIndex;
                    continue;
                }
                else if (line.Contains((char)183)) // · character
                {
                    EnsureRecordingComplete(currentSession, recordingLine);
                    var parts = line.Split((char)183);
                    if (parts.Length != 2)
                        throw new InvalidSessionFormatException($"Line {i + 1}: expected recorded date · duration.");
                    var date = ReadRecordedDate(parts[0].Trim(), i + 1);
                    currentSession = new Session
                    {
                        Date = date,
                        RecordedDate = date,
                        Duration = ReadDuration(parts[1].Trim(), i + 1),
                        GameName = selectedGame.Name,
                        GameId = selectedGame.Id
                    };
                    recordingLine = i + 1;
                    hasEarnings = false;
                    continue;
                }

                if (currentSession == null)
                {
                    continue;
                }
                if (line.Contains('$'))
                {
                    ParseEarnings(currentSession, line);
                    hasEarnings = true;
                }
                // Parse status
                if (TryParseStatus(line, out var status))
                {
                    CompleteRecording(currentSession, status, line, hasEarnings, i + 1);
                    currentSession.ImportSourceText = string.Join("\n", lines[(recordingLine - 1)..(i + 1)]);
                    sessions.Add(currentSession);
                    currentSession = null; // Reset currentSession for the next entry
                }
            }
            EnsureRecordingComplete(currentSession, recordingLine);
            if (sessions.Count == 0)
                throw new InvalidSessionFormatException("No complete recordings were found in the provided text.");
            return sessions;
        }

        private static List<Session> ParseRecordings(string[] lines, List<Game> games, Game? selectedGame)
        {
            List<Session> sessions = [];
            Session? currentSession = null;
            var recordingLine = 0;
            var hasEarnings = false;

            var sourceStart = 0;
            var durationText = string.Empty;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                // A game title is metadata, even when it contains a status word such as "Verified".
                var nextLine = NextContentLine(lines, i + 1);
                if (nextLine < lines.Length && lines[nextLine].Contains((char)183)
                    && games.Any(game => game.Name.Equals(line, StringComparison.OrdinalIgnoreCase)))
                    continue;

                if (line.Contains((char)183)) // · character
                {
                    EnsureRecordingComplete(currentSession, recordingLine);
                    var parts = line.Split((char)183);
                    // Newer Recordings exports append local file availability to the header.
                    // It is metadata, not the upload/verification status that follows the row.
                    if (parts.Length != 3 && !(parts.Length == 4
                        && parts[3].Trim().Equals("File deleted", StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidSessionFormatException($"Line {i + 1}: expected filename · recorded date · duration.");
                    var gameIndex = i - 1;
                    while (gameIndex >= 0 && string.IsNullOrWhiteSpace(lines[gameIndex]))
                        gameIndex--;
                    if (gameIndex < 0)
                        throw new InvalidSessionFormatException($"Line {i + 1}: the recording is missing its game name.");
                    var gameName = lines[gameIndex].Trim();
                    var game = ResolveGame(gameName, games, selectedGame, allowAmbiguous: true);
                    sourceStart = gameIndex;
                    var date = ReadRecordedDate(parts[1].Trim(), i + 1);
                    durationText = parts[2].Trim();
                    currentSession = new Session
                    {
                        Date = date,
                        RecordedDate = date,
                        Duration = ReadDuration(durationText, i + 1),
                        GameName = game?.Name ?? gameName,
                        GameId = game?.Id
                    };
                    recordingLine = i + 1;
                    hasEarnings = false;
                    continue;
                }

                if (currentSession == null)
                {
                    continue;
                }
                if (line.Contains('$'))
                {
                    ParseEarnings(currentSession, line);
                    hasEarnings = true;
                }

                // Parse status. Preserve an explicit status following a below-minimum notice.
                if (line == "Below minimum time" && nextLine < lines.Length
                    && TryParseStatus(lines[nextLine].Trim(), out _))
                    continue;
                if (TryParseStatus(line, out var status))
                {
                    CompleteRecording(currentSession, status, line, hasEarnings, i + 1);
                    if (!currentSession.GameId.HasValue)
                        MatchRecordingEarnings(currentSession, games, durationText, hasEarnings);
                    currentSession.ImportSourceText = string.Join("\n", lines[sourceStart..(i + 1)]);
                    sessions.Add(currentSession);
                    currentSession = null; // Reset currentSession for the next entry
                }
            }
            EnsureRecordingComplete(currentSession, recordingLine);
            if (sessions.Count == 0)
                throw new InvalidSessionFormatException("No complete recordings were found in the provided text.");
            return sessions;
        }

        private static int NextContentLine(string[] lines, int index)
        {
            while (index < lines.Length && string.IsNullOrWhiteSpace(lines[index]))
                index++;
            return index;
        }

        private static DateTime ReadRecordedDate(string input, int line)
        {
            if (!DateTime.TryParse(input, SourceDateCulture, DateTimeStyles.None, out var date))
                throw new InvalidSessionFormatException($"Line {line}: invalid recorded date '{input}'.");
            return date;
        }

        private static TimeSpan ReadDuration(string input, int line)
        {
            try { return ParseDuration(input); }
            catch (FormatException ex) { throw new InvalidSessionFormatException($"Line {line}: {ex.Message}"); }
        }

        private static void EnsureRecordingComplete(Session? session, int line)
        {
            if (session != null)
                throw new InvalidSessionFormatException($"Line {line}: the recording for '{session.GameName}' is missing a recognized status. No sessions were imported.");
        }

        private static void CompleteRecording(Session session, Enums.SessionStatus status, string line, bool hasEarnings, int lineNumber)
        {
            if (status == Enums.SessionStatus.Verified && !hasEarnings)
                throw new InvalidSessionFormatException($"Line {lineNumber}: verified recording for '{session.GameName}' is missing its earnings value. No sessions were imported.");
            session.Status = status;
            session.IsForceVerified = line.Contains("force-verified", StringComparison.OrdinalIgnoreCase);
            SessionService.ValidateImportedSession(session);
        }

        private static void ParseEarnings(Session session, string line)
        {
            var currencyIndex = line.IndexOf('$');
            if (currencyIndex < 0)
                return;

            var amountText = line[(currencyIndex + 1)..].Trim();
            var prefix = line[..currencyIndex].TrimEnd();
            var errorMessage = $"Invalid USD amount for '{session.GameName}': '{line}'. Use a value such as $1.55.";
            if (prefix.EndsWith('('))
                throw new FormatException(errorMessage);
            // Nerve can put the sign before the currency symbol. Keep it when reading the amount.
            if (prefix.EndsWith('-') || prefix.EndsWith('+'))
                amountText = prefix[^1] + amountText;

            var match = Regex.Match(amountText,
                @"\A(?<sign>[+-]?)\s*(?<amount>(?:[0-9]+|[0-9]{1,3}(?:,[0-9]{3})+)(?:\.[0-9]+)?)\z");
            if (!match.Success)
                throw new FormatException(errorMessage);

            var numberText = match.Groups["amount"].Value.Replace(",", "");
            if (!decimal.TryParse(numberText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var earnings))
                throw new FormatException(errorMessage);

            // Decimal parsing can round values beyond its precision. Reject those rather than change earnings.
            var exactNumberText = numberText;
            if (exactNumberText.Contains('.'))
                exactNumberText = exactNumberText.TrimEnd('0').TrimEnd('.');
            var parsedNumberText = earnings.ToString("0.############################", CultureInfo.InvariantCulture);
            if (exactNumberText.TrimStart('0') != parsedNumberText.TrimStart('0'))
                throw new FormatException(errorMessage);

            if (match.Groups["sign"].Value == "-")
                earnings = -earnings;
            SessionService.ValidateEarnings(earnings);
            session.Earnings = earnings;
        }

        private static bool TryParseStatus(string line, out Enums.SessionStatus status)
        {
            if (line.Contains("Uploading") || line.Contains("Verifying") || line.Contains("In review"))
            {
                status = Enums.SessionStatus.Pending;
                return true;
            }

            if (line.Contains("Verified") || line.Contains("Paid"))
            {
                status = Enums.SessionStatus.Verified;
                return true;
            }

            if (line.Contains("Rejected"))
            {
                status = Enums.SessionStatus.Rejected;
                return true;
            }

            if (line.Contains("Local file deleted") || line.Contains("File missing") || line.Contains("Below minimum time"))
            {
                status = Enums.SessionStatus.NotUploaded;
                return true;
            }

            status = default;
            return false;
        }

        private static void MatchRecordingEarnings(Session session, List<Game> games, string durationText, bool hasEarnings)
        {
            if (!hasEarnings || session.Status != Enums.SessionStatus.Verified
                || session.IsForceVerified || session.Earnings <= 0m || session.Duration <= TimeSpan.Zero)
                return;
            var candidates = games.Where(game => game.Name.Equals(session.GameName, StringComparison.OrdinalIgnoreCase)).ToList();
            // A minute-only source can be truncated or rounded. Seconds explicitly supplied are exact.
            var uncertainty = durationText.Contains('s') ? 0m : durationText.Contains('m') ? 60m : 3600m;
            var seconds = session.Duration.Ticks / (decimal)TimeSpan.TicksPerSecond;
            var minimumSeconds = Math.Max(0m, seconds - uncertainty);
            var maximumSeconds = seconds + uncertainty;
            var scale = Math.Max(2, (decimal.GetBits(session.Earnings)[3] >> 16) & 0xff);
            var rounding = 0.5m;
            for (int i = 0; i < scale; i++) rounding /= 10m;
            var minimumAmount = session.Earnings - rounding;
            var maximumAmount = session.Earnings > decimal.MaxValue - rounding
                ? decimal.MaxValue : session.Earnings + rounding;
            Game? match = null;
            foreach (var candidate in candidates)
            {
                // Missing rate evidence cannot eliminate a candidate safely.
                if (candidate.RatePerHour <= 0m) return;
                decimal minimum, maximum;
                try
                {
                    minimum = candidate.RatePerHour * minimumSeconds / 3600m;
                    maximum = candidate.RatePerHour * maximumSeconds / 3600m;
                }
                catch (OverflowException) { return; }
                var agrees = minimum <= maximumAmount && maximum >= minimumAmount;
                // Nerve pays the whole recording even when it crosses a cap. Compare the full
                // duration; never clip the amount to remaining cap minutes or guess a closest rate.
                if (!agrees) continue;
                if (match != null) return;
                match = candidate;
            }
            if (match == null) return; // Changed historical rates/adjustments are not a closest-match rule.
            session.GameId = match.Id;
        }

        private static Game? ResolveGame(string gameName, List<Game> games, Game? selectedGame, bool allowAmbiguous = false)
        {
            if (selectedGame != null && gameName.Equals(selectedGame.Name, StringComparison.OrdinalIgnoreCase))
            {
                var selected = games.FirstOrDefault(game => game.Id == selectedGame.Id);
                return selected ?? throw new GameNotFoundException(gameName);
            }
            var matchingGames = games.Where(game => game.Name.Equals(gameName, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matchingGames.Count == 0)
                throw new GameNotFoundException(gameName);
            if (matchingGames.Count == 1)
                return matchingGames[0];
            if (allowAmbiguous)
                return null;
            throw new InvalidSessionFormatException($"Multiple games are named '{gameName}'. Select the correct game in Games and use Add Session to import these recordings. No sessions were imported.");
        }


        public static List<Session> Parse(string importText, List<Game> games, Game? game)
        {
            string[] lines = importText.Split(
                ["\r\n", "\r", "\n"],
                StringSplitOptions.None
            );
            if (LooksLikeRecordingsFormat(lines))
            {
                return ParseRecordings(lines, games, game);
            }

            if (LooksLikeGameDetailsFormat(lines) && game is not null)
            {
                return ParseGameDetails(lines, games, game);
            }

            throw new InvalidSessionFormatException();
        }
    }
}
