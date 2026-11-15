using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using EarningsTracker.Models;

namespace EarningsTracker.Utilities
{
    internal sealed class PayoutParseResult
    {
        public List<Payout> Payouts { get; } = [];
        public List<Session> ForceVerifiedSessions { get; } = [];
        public List<string> FailedRows { get; } = [];
        public List<string> SkippedRows { get; } = [];
    }

    internal static class PayoutTextParser
    {
        public static PayoutParseResult ParseDetailed(string importText)
        {
            var result = new PayoutParseResult();
            var lines = importText.ReplaceLineEndings("\n").Split('\n');
            Dictionary<string, int>? header = null;
            var invalidHeader = false;
            for (var i = 0; i < lines.Length; i++)
            {
                var raw = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(raw))
                    continue;
                string[] cols;
                var isTableRow = raw.Contains('|');
                if (isTableRow)
                {
                    cols = raw.Trim('|').Split('|').Select(CleanCell).ToArray();
                }
                else if (!TryParsePlainTextRow(raw, out cols))
                {
                    result.FailedRows.Add($"Line {i + 1}: could not read the transaction row.");
                    continue;
                }

                if (cols.Length > 0 && cols.All(c => c.Trim().Length == 0 || c.Trim().All(ch => ch is '-' or ':' or '|')))
                    continue;
                var possibleHeader = cols.Select(NormalizeHeader).ToArray();
                if (possibleHeader.Contains("posted") && possibleHeader.Contains("type") && possibleHeader.Contains("amount"))
                {
                    var columns = possibleHeader.Select((name, index) => (name, index))
                        .Where(item => item.name.Length > 0).ToList();
                    invalidHeader = columns.Select(item => item.name).Distinct().Count() != columns.Count;
                    if (invalidHeader)
                        result.FailedRows.Add($"Line {i + 1}: duplicate column names. Correct the header before importing its rows.");
                    else
                        header = columns.ToDictionary(item => item.name, item => item.index, StringComparer.OrdinalIgnoreCase);
                    continue;
                }
                if (isTableRow && invalidHeader)
                {
                    result.FailedRows.Add($"Line {i + 1}: could not read the transaction under the invalid table header.");
                    continue;
                }
                if (cols.Length < 6)
                {
                    result.FailedRows.Add($"Line {i + 1}: expected at least six table columns.");
                    continue;
                }
                var rowHeader = isTableRow ? header : null;
                int Index(string name, int fallback) => rowHeader != null && rowHeader.TryGetValue(name, out var index) ? index : fallback;
                var postedIndex = Index("posted", 0);
                var recordedIndex = Index("recorded", 1);
                var typeIndex = Index("type", 2);
                var descriptionIndex = Index("description", 3);
                var statusIndex = Index("status", 4);
                var amountIndex = Index("amount", 5);
                var sourceIdIndex = rowHeader == null
                    ? (isTableRow && cols.Length == 7 ? 6 : -1)
                    : new[] { "sourceid", "transactionid", "referenceid", "id" }
                        .Select(name => Index(name, -1)).FirstOrDefault(index => index >= 0, -1);
                if (new[] { postedIndex, recordedIndex, typeIndex, descriptionIndex, statusIndex, amountIndex }.Any(index => index >= cols.Length))
                {
                    result.FailedRows.Add($"Line {i + 1}: row does not contain all required transaction columns.");
                    continue;
                }
                if (!DateTime.TryParse(cols[postedIndex], CultureInfo.InvariantCulture, DateTimeStyles.None, out var postedDate))
                {
                    result.FailedRows.Add($"Line {i + 1}: invalid posted date '{cols[postedIndex]}'.");
                    continue;
                }

                var type = cols[typeIndex].Trim();
                var description = cols[descriptionIndex].Trim();
                var status = cols[statusIndex].Trim();
                var amountMatch = Regex.Match(cols[amountIndex], @"^\s*(?<sign>[+-]?)\s*\$\s*(?<amount>(?:[0-9]+|[0-9]{1,3}(?:,[0-9]{3})+)(?:\.[0-9]+)?)\s*$");
                if (!amountMatch.Success)
                {
                    result.FailedRows.Add($"Line {i + 1}: invalid USD amount '{cols[amountIndex]}'.");
                    continue;
                }
                var number = amountMatch.Groups["amount"].Value.Replace(",", "");
                var parts = number.Split('.');
                var whole = parts[0].TrimStart('0');
                var fraction = parts.Length == 2 ? parts[1].TrimEnd('0') : "";
                if (fraction.Length > 2)
                {
                    result.FailedRows.Add($"Line {i + 1}: USD amount must be non-zero and have no more than two decimal places.");
                    continue;
                }
                var exactNumber = (whole.Length == 0 ? "0" : whole) + (fraction.Length == 0 ? "" : "." + fraction);
                // TryParse can round a value whose significant digits exceed decimal precision.
                if (!decimal.TryParse(exactNumber, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
                    || amount.ToString("0.##", CultureInfo.InvariantCulture) != exactNumber)
                {
                    result.FailedRows.Add($"Line {i + 1}: USD amount cannot be represented exactly.");
                    continue;
                }
                if (amountMatch.Groups["sign"].Value == "-") amount = -amount;
                if (amount == 0m)
                {
                    result.FailedRows.Add($"Line {i + 1}: USD amount must be non-zero and have no more than two decimal places.");
                    continue;
                }
                if (type.Equals("Payment", StringComparison.OrdinalIgnoreCase) || type.Equals("Bonus", StringComparison.OrdinalIgnoreCase) || type.Equals("Earnings correction", StringComparison.OrdinalIgnoreCase))
                {
                    if (!IsKnownStatus(status))
                    {
                        result.FailedRows.Add($"Line {i + 1}: unsupported status '{status}'.");
                        continue;
                    }
                    var sourceId = sourceIdIndex >= 0 && sourceIdIndex < cols.Length && !IsPlaceholder(cols[sourceIdIndex]) ? cols[sourceIdIndex] : null;
                    result.Payouts.Add(new Payout
                    {
                        Date = postedDate,
                        Type = type,
                        Description = description,
                        Status = status,
                        Amount = amount,
                        SourceId = sourceId,
                        SourceRow = raw,
                        SourceValuesJson = JsonSerializer.Serialize(new { PostedDate = cols[postedIndex], RecordedDate = cols[recordedIndex], Type = type, Description = description, Status = status, Amount = cols[amountIndex], SourceId = sourceId })
                    });
                    continue;
                }
                if (type.Equals("Earning", StringComparison.OrdinalIgnoreCase) && description.Contains("force-verified", StringComparison.OrdinalIgnoreCase))
                {
                    if (amount < 0m)
                    {
                        result.FailedRows.Add($"Line {i + 1}: session earnings cannot be negative. Import a correction as an earnings adjustment.");
                        continue;
                    }
                    if (!DateTime.TryParse(cols[recordedIndex], CultureInfo.InvariantCulture, DateTimeStyles.None, out var recordedDate))
                    {
                        result.SkippedRows.Add($"Line {i + 1}: force-verified earning has no recorded date, so it cannot be safely matched to a session.");
                        continue;
                    }
                    var match = Regex.Match(description, @"^(?<game>.+?)\s+[—–-]\s+(?<minutes>\d+)\s+min(?:ute)?s? recording", RegexOptions.IgnoreCase);
                    if (!match.Success || !int.TryParse(match.Groups["minutes"].Value, NumberStyles.None,
                        CultureInfo.InvariantCulture, out var minutes))
                    {
                        result.FailedRows.Add($"Line {i + 1}: could not extract game and duration from force-verified earning.");
                        continue;
                    }
                    if (!status.Equals("Verified", StringComparison.OrdinalIgnoreCase))
                    {
                        result.SkippedRows.Add($"Line {i + 1}: force-verified earning status '{status}' is not Verified.");
                        continue;
                    }
                    result.ForceVerifiedSessions.Add(new Session
                    {
                        GameName = match.Groups["game"].Value.Trim(),
                        Date = recordedDate,
                        RecordedDate = recordedDate,
                        PostedDate = postedDate,
                        Duration = TimeSpan.FromMinutes(minutes),
                        Earnings = amount,
                        Status = Enums.SessionStatus.Verified,
                        IsForceVerified = true,
                        ImportSourceText = raw
                    });
                }
                else
                {
                    result.SkippedRows.Add($"Line {i + 1}: transaction type '{type}' is not imported by this workflow.");
                }
            }
            return result;
        }

        private static bool TryParsePlainTextRow(string raw, out string[] columns)
        {
            columns = [];
            var dateAndTransaction = Regex.Match(raw,
                @"^(?<date>\d{1,2}/\d{1,2}/\d{4})\s*[—–-]\s*(?<transaction>.+)$");
            if (!dateAndTransaction.Success)
                return false;

            var transaction = dateAndTransaction.Groups["transaction"].Value.Trim();
            string? type = null;
            foreach (var candidate in new[] { "Earnings correction", "Payment", "Bonus", "Earning" })
            {
                if (transaction.StartsWith(candidate, StringComparison.OrdinalIgnoreCase) &&
                    transaction.Length > candidate.Length &&
                    char.IsWhiteSpace(transaction[candidate.Length]))
                {
                    type = candidate;
                    transaction = transaction[(candidate.Length + 1)..].TrimStart();
                    break;
                }
            }

            if (type == null)
                return false;

            var details = Regex.Match(transaction,
                @"^(?<description>.+?)\s+(?<status>Paid|Processing|Failed|Verified|Pending)\s+(?<amount>[+-]?\s*\$\s*[0-9][0-9,]*(?:\.[0-9]+)?)\s*$",
                RegexOptions.IgnoreCase);
            if (!details.Success)
                return false;

            columns =
            [
                dateAndTransaction.Groups["date"].Value,
                "—",
                type,
                details.Groups["description"].Value.Trim(),
                details.Groups["status"].Value,
                details.Groups["amount"].Value
            ];
            return true;
        }

        private static string CleanCell(string value) => System.Net.WebUtility.HtmlDecode(value.Replace("**", "").Trim());
        private static string NormalizeHeader(string value) => Regex.Replace(CleanCell(value).Trim().ToLowerInvariant(), @"[^a-z]", "") switch
        {
            "date" or "posteddate" => "posted",
            "recordeddate" => "recorded",
            "transactiontype" => "type",
            "transactiondescription" => "description",
            "transactionstatus" => "status",
            "usdamount" => "amount",
            "sourceidentifier" => "sourceid",
            "transactionreferenceid" => "referenceid",
            var name => name
        };
        private static bool IsPlaceholder(string value) => string.IsNullOrWhiteSpace(value) || value.Trim() is "—" or "–" or "-";
        private static bool IsKnownStatus(string status) => new[] { "Paid", "Processing", "Failed", "Verified", "Pending" }.Contains(status, StringComparer.OrdinalIgnoreCase);
    }
}
