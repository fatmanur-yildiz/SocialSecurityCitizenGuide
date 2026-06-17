using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace EmekliRehberi.Services
{
    public class PremiumPdfParser
    {
        private const double RowTolerance       = 4.0;
        private const double ColTolerance       = 18.0;
        private const double HeaderSearchWindow = 60.0;

        private static readonly string[] HeaderKeywords =
            { "Dönem", "Gün", "PEK", "Belge", "Ünite", "Sgrt", "Sicil" };

        private static readonly Regex PeriodRegex = new Regex(
            @"\b(?<year>20\d{2})\s*/\s*(?<month>0[1-9]|1[0-2])\b",
            RegexOptions.Compiled);

        // Her iki para formatını da yakala:
        // Türk:    20.002,50
        // İngiliz: 20,002.50
        // Sade:    1079,10 veya 1079.10
        private static readonly Regex MoneyRegex = new Regex(
            @"\b\d{1,3}(?:[.,]\d{3})*[.,]\d{2}\b",
            RegexOptions.Compiled);

        private static readonly Regex OfficialTotalRegex = new Regex(
            @"Toplam\s*4\s*[Aa]\s*Uzun\s*Vade\s*P[ÖO]GS\s*[:\-]?\s*(?<days>\d{3,5})",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // ─── Public API ──────────────────────────────────────────────────────────

        public PremiumAnalysisResult AnalyzePdf(string filePath)
        {
            try
            {
                var allPageData = ExtractAllPageData(filePath);
                var debugLines  = BuildDebugText(allPageData);

                var officialTotal = FindOfficialTotal(allPageData);
                var records       = ParseRecordsCoordinateBased(allPageData);

                int? totalDays = officialTotal
                    ?? SumFromYearTotals(records)
                    ?? SumFromMonthlyRecords(records);

                string? lastPeriod = records
                    .Where(r => !r.IsYearTotal && PeriodRegex.IsMatch(r.Period))
                    .OrderByDescending(r => r.Period)
                    .FirstOrDefault()?.Period;

                bool hasMissing = records
                    .Any(r => !r.IsYearTotal && r.Days > 0 && r.Days < 30);

                return new PremiumAnalysisResult
                {
                    ExtractedText    = debugLines,
                    Records          = records,
                    TotalPremiumDays = totalDays,
                    LastPeriod       = lastPeriod,
                    HasMissingDays   = hasMissing
                };
            }
            catch (Exception ex)
            {
                return new PremiumAnalysisResult
                {
                    ExtractedText = $"PDF okuma hatası: {ex.Message}",
                    Records       = new List<PremiumParsedRecord>()
                };
            }
        }

        public string ExtractText(string filePath)
        {
            var allPageData = ExtractAllPageData(filePath);
            return BuildDebugText(allPageData);
        }

        // ─── Sayfa verisi çıkarma ─────────────────────────────────────────────────

        private List<List<List<WordInfo>>> ExtractAllPageData(string filePath)
        {
            var options  = new ParsingOptions { SkipMissingFonts = true, UseLenientParsing = true };
            var allPages = new List<List<List<WordInfo>>>();

            using var doc = PdfDocument.Open(filePath, options);

            foreach (var page in doc.GetPages())
            {
                var wordInfos = page.GetWords()
                    .Select(w => new WordInfo(w.Text, w.BoundingBox.Left,
                                             w.BoundingBox.Bottom, w.BoundingBox.Top,
                                             w.BoundingBox.Right))
                    .ToList();

                if (!wordInfos.Any())
                {
                    var fallback = page.Text?
                        .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select((t, i) => new List<WordInfo>
                            { new WordInfo(t.Trim(), 0, i * -12, i * -12 + 10, 500) })
                        .ToList() ?? new List<List<WordInfo>>();

                    allPages.Add(fallback);
                    continue;
                }

                var rows = GroupIntoRows(wordInfos);
                allPages.Add(rows);
            }

            return allPages;
        }

        private List<List<WordInfo>> GroupIntoRows(List<WordInfo> words)
        {
            var sorted = words.OrderByDescending(w => w.Bottom).ToList();
            var rows   = new List<List<WordInfo>>();

            foreach (var word in sorted)
            {
                bool placed = false;

                foreach (var row in rows)
                {
                    var rep    = row[0];
                    double mid = (word.Bottom + word.Top) / 2.0;

                    if (mid >= rep.Bottom - RowTolerance && mid <= rep.Top + RowTolerance)
                    {
                        row.Add(word);
                        placed = true;
                        break;
                    }
                }

                if (!placed)
                    rows.Add(new List<WordInfo> { word });
            }

            foreach (var row in rows)
                row.Sort((a, b) => a.Left.CompareTo(b.Left));

            return rows;
        }

        // ─── Koordinat bazlı kayıt çözümleme ────────────────────────────────────

        private List<PremiumParsedRecord> ParseRecordsCoordinateBased(
            List<List<List<WordInfo>>> allPageData)
        {
            var records = new List<PremiumParsedRecord>();

            foreach (var pageRows in allPageData)
            {
                var colMap = DetectColumnMap(pageRows);

                if (colMap == null)
                {
                    records.AddRange(FallbackRegexParse(pageRows));
                    continue;
                }

                ParseDataRows(pageRows, colMap, records);
            }

            var merged = MergeRecords(records);
            return merged.OrderBy(r => r.IsYearTotal ? 1 : 0).ThenBy(r => r.Period).ToList();
        }

        // ─── Kolon haritası ───────────────────────────────────────────────────────

        private class ColumnMap
        {
            public double  HeaderY     { get; set; }
            public double? DönemX      { get; set; }
            public double? GünX        { get; set; }
            public double? PekX        { get; set; }
            public double? BelgeTürüX  { get; set; }
            public double? BelgeKdX    { get; set; }
        }

        private ColumnMap? DetectColumnMap(List<List<WordInfo>> pageRows)
        {
            foreach (var row in pageRows)
            {
                var rowText = string.Join(" ", row.Select(w => w.Text));
                int hits = HeaderKeywords.Count(k =>
                    rowText.Contains(k, StringComparison.OrdinalIgnoreCase));

                if (hits < 3) continue;

                bool hasDönem = row.Any(w => w.Text.Contains("Dönem", StringComparison.OrdinalIgnoreCase));
                bool hasGün   = row.Any(w => w.Text.Contains("Gün",   StringComparison.OrdinalIgnoreCase)
                                          && !w.Text.Contains("Neden", StringComparison.OrdinalIgnoreCase));

                if (!hasDönem || !hasGün) continue;

                var map = new ColumnMap { HeaderY = row[0].Bottom };

                map.DönemX = FindColumnX(row, "Dönem");

                map.GünX = row
                    .Where(w => w.Text.Equals("Gün", StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault()?.CenterX
                    ?? row.Where(w => w.Text.Contains("Gün", StringComparison.OrdinalIgnoreCase)
                                   && !w.Text.Contains("Neden", StringComparison.OrdinalIgnoreCase))
                           .FirstOrDefault()?.CenterX;

                map.PekX = FindColumnX(row, "PEK");

                map.BelgeTürüX = FindColumnX(row, "Belge");

                var belgeKd = row.FirstOrDefault(w =>
                    w.Text.Contains("Kd",   StringComparison.OrdinalIgnoreCase)
                 || w.Text.Contains("Bsmk", StringComparison.OrdinalIgnoreCase));
                map.BelgeKdX = belgeKd?.CenterX;

                return map;
            }

            return null;
        }

        private double? FindColumnX(List<WordInfo> row, string keyword)
        {
            return row.FirstOrDefault(w =>
                w.Text.Contains(keyword, StringComparison.OrdinalIgnoreCase))?.CenterX;
        }

        // ─── Veri satırlarını çözme ──────────────────────────────────────────────

        private void ParseDataRows(
            List<List<WordInfo>> pageRows,
            ColumnMap colMap,
            List<PremiumParsedRecord> records)
        {
            var dataRows = pageRows
                .Where(r => r[0].Bottom < colMap.HeaderY - RowTolerance)
                .OrderByDescending(r => r[0].Bottom)
                .ToList();

            foreach (var row in dataRows)
            {
                var rowText = string.Join(" ", row.Select(w => w.Text)).Trim();

                if (rowText.Contains("TOPLAM", StringComparison.OrdinalIgnoreCase)
                 || rowText.Contains("Toplam", StringComparison.OrdinalIgnoreCase))
                {
                    var yearTotalRecord = TryParseYearTotalRow(row, colMap);
                    if (yearTotalRecord != null)
                        records.Add(yearTotalRecord);
                    continue;
                }

                string? period = ExtractPeriodFromRow(row, colMap);
                if (period == null) continue;

                int days       = ExtractDayFromRow(row, colMap);
                decimal? pek   = ExtractPekFromRow(row, colMap);

                if (days < 0 || days > 30) days = 0;

                records.Add(new PremiumParsedRecord
                {
                    Period      = period,
                    Days        = days,
                    PekAmount   = pek,
                    IsYearTotal = false
                });
            }
        }

        // ─── Kolon değeri çekme ───────────────────────────────────────────────────

        private string? ExtractPeriodFromRow(List<WordInfo> row, ColumnMap colMap)
        {
            if (colMap.DönemX.HasValue)
            {
                var candidates = row
                    .Where(w => Math.Abs(w.CenterX - colMap.DönemX.Value) < ColTolerance * 2)
                    .OrderBy(w => w.Left)
                    .ToList();

                foreach (var c in candidates)
                {
                    var m = PeriodRegex.Match(c.Text);
                    if (m.Success)
                        return $"{m.Groups["year"].Value}/{m.Groups["month"].Value}";
                }

                var segment = string.Join("", candidates.Select(w => w.Text));
                var sm = PeriodRegex.Match(segment);
                if (sm.Success)
                    return $"{sm.Groups["year"].Value}/{sm.Groups["month"].Value}";
            }

            var rowText = string.Join(" ", row.Select(w => w.Text));
            var fm = PeriodRegex.Match(rowText);
            return fm.Success
                ? $"{fm.Groups["year"].Value}/{fm.Groups["month"].Value}"
                : null;
        }

        private int ExtractDayFromRow(List<WordInfo> row, ColumnMap colMap)
        {
            if (!colMap.GünX.HasValue) return 0;

            var candidate = row
                .Where(w => Math.Abs(w.CenterX - colMap.GünX.Value) < ColTolerance)
                .OrderBy(w => Math.Abs(w.CenterX - colMap.GünX.Value))
                .FirstOrDefault();

            if (candidate != null && Regex.IsMatch(candidate.Text, @"\d{2}\.\d{2}\.\d{4}"))
                candidate = null;

            if (candidate != null && int.TryParse(candidate.Text.Trim(), out int d) && d >= 0 && d <= 30)
                return d;

            candidate = row
                .Where(w => Math.Abs(w.CenterX - colMap.GünX.Value) < ColTolerance * 2)
                .Where(w => !Regex.IsMatch(w.Text, @"\d{2}\.\d{2}\.\d{4}"))
                .Where(w => int.TryParse(w.Text.Trim(), out _))
                .OrderBy(w => Math.Abs(w.CenterX - colMap.GünX.Value))
                .FirstOrDefault();

            if (candidate != null && int.TryParse(candidate.Text.Trim(), out int d2) && d2 >= 0 && d2 <= 30)
                return d2;

            return 0;
        }

        private decimal? ExtractPekFromRow(List<WordInfo> row, ColumnMap colMap)
        {
            // PEK kolonunu bul – kolon biliniyorsa yakın kelimelerde ara, yoksa tüm satırda
            IEnumerable<WordInfo> candidates;

            if (colMap.PekX.HasValue)
            {
                candidates = row
                    .Where(w => Math.Abs(w.CenterX - colMap.PekX.Value) < ColTolerance * 3)
                    .OrderBy(w => Math.Abs(w.CenterX - colMap.PekX.Value));
            }
            else
            {
                candidates = row;
            }

            foreach (var c in candidates)
            {
                // Önce kelimenin kendisinde para formatı ara
                var m = MoneyRegex.Match(c.Text);
                if (m.Success)
                {
                    var parsed = ParseMoney(m.Value);
                    if (parsed.HasValue && parsed.Value > 0)
                        return parsed;
                }
            }

            // Bulunamadıysa tüm satırda son çare olarak ara
            var rowText = string.Join(" ", row.Select(w => w.Text));
            var allMatches = MoneyRegex.Matches(rowText);

            // Satırda birden fazla para değeri olabilir (PEK ve yıl toplamı gibi)
            // Dönemden SONRA gelen ilk para değerini al
            var periodMatch = PeriodRegex.Match(rowText);
            int searchFrom  = periodMatch.Success ? periodMatch.Index + periodMatch.Length : 0;

            foreach (Match match in allMatches)
            {
                if (match.Index >= searchFrom)
                {
                    var parsed = ParseMoney(match.Value);
                    if (parsed.HasValue && parsed.Value > 100) // Gün sayısı gibi küçük değerleri atla
                        return parsed;
                }
            }

            return null;
        }

        // ─── Yıl toplam satırı ────────────────────────────────────────────────────

        private PremiumParsedRecord? TryParseYearTotalRow(List<WordInfo> row, ColumnMap colMap)
        {
            var rowText = string.Join(" ", row.Select(w => w.Text));

            string? yearStr = null;
            foreach (var w in row)
            {
                if (Regex.IsMatch(w.Text, @"^20\d{2}$"))
                {
                    yearStr = w.Text;
                    break;
                }
            }

            if (yearStr == null)
            {
                var ym = Regex.Match(rowText, @"\b(20\d{2})\b");
                if (ym.Success) yearStr = ym.Groups[1].Value;
            }

            if (yearStr == null) return null;

            int days = 0;

            if (colMap.GünX.HasValue)
            {
                var candidate = row
                    .Where(w => Math.Abs(w.CenterX - colMap.GünX.Value) < ColTolerance * 2)
                    .Where(w => !Regex.IsMatch(w.Text, @"\d{2}\.\d{2}\.\d{4}"))
                    .OrderBy(w => Math.Abs(w.CenterX - colMap.GünX.Value))
                    .FirstOrDefault();

                if (candidate != null && int.TryParse(candidate.Text.Trim(), out int cd))
                    days = cd;
            }

            if (days == 0)
            {
                var numbers = row
                    .Select(w => w.Text.Trim())
                    .Where(t => Regex.IsMatch(t, @"^\d+$"))
                    .Select(t => int.TryParse(t, out int n) ? n : -1)
                    .Where(n => n > 0 && n <= 366)
                    .OrderByDescending(n => n)
                    .ToList();

                if (numbers.Any()) days = numbers.First();
            }

            if (days <= 0) return null;

            decimal? pek = ExtractPekFromRow(row, colMap);

            return new PremiumParsedRecord
            {
                Period      = yearStr,
                Days        = days,
                PekAmount   = pek,
                IsYearTotal = true
            };
        }

        // ─── Kayıt birleştirme ───────────────────────────────────────────────────

        private List<PremiumParsedRecord> MergeRecords(List<PremiumParsedRecord> records)
        {
            var yearTotals = records.Where(r => r.IsYearTotal).ToList();

            var merged = records
                .Where(r => !r.IsYearTotal)
                .GroupBy(r => r.Period)
                .Select(g =>
                {
                    var validDays = g.Select(r => r.Days).Where(d => d > 0).ToList();
                    int total     = validDays.Any() ? Math.Min(30, validDays.Sum()) : 0;
                    decimal? pek  = g.Any(r => r.PekAmount.HasValue)
                        ? g.Where(r => r.PekAmount.HasValue).Sum(r => r.PekAmount!.Value)
                        : null;

                    return new PremiumParsedRecord
                    {
                        Period      = g.Key,
                        Days        = total,
                        PekAmount   = pek,
                        IsYearTotal = false
                    };
                })
                .ToList();

            merged.AddRange(yearTotals);
            return merged;
        }

        // ─── Resmi toplam ────────────────────────────────────────────────────────

        private int? FindOfficialTotal(List<List<List<WordInfo>>> allPageData)
        {
            foreach (var pageRows in allPageData)
            {
                foreach (var row in pageRows)
                {
                    var text = string.Join(" ", row.Select(w => w.Text));
                    var m    = OfficialTotalRegex.Match(text);
                    if (m.Success && int.TryParse(m.Groups["days"].Value, out int days) && days > 0)
                        return days;
                }

                var fullText = string.Join(" ", pageRows.SelectMany(r => r.Select(w => w.Text)));
                var fm       = OfficialTotalRegex.Match(fullText);
                if (fm.Success && int.TryParse(fm.Groups["days"].Value, out int fd) && fd > 0)
                    return fd;
            }

            return null;
        }

        // ─── Fallback regex ───────────────────────────────────────────────────────

        private List<PremiumParsedRecord> FallbackRegexParse(List<List<WordInfo>> pageRows)
        {
            var result = new List<PremiumParsedRecord>();

            foreach (var row in pageRows)
            {
                var text = string.Join(" ", row.Select(w => w.Text));

                if (text.Contains("TOPLAM", StringComparison.OrdinalIgnoreCase)
                 || text.Contains("Toplam", StringComparison.OrdinalIgnoreCase))
                {
                    var yearM = Regex.Match(text, @"\b(20\d{2})\b");
                    var dayMs = Regex.Matches(text, @"\b(\d{1,3})\b")
                        .Cast<Match>()
                        .Select(m => int.TryParse(m.Groups[1].Value, out int n) ? n : -1)
                        .Where(n => n > 10 && n <= 366)
                        .OrderByDescending(n => n)
                        .ToList();

                    if (yearM.Success && dayMs.Any())
                    {
                        result.Add(new PremiumParsedRecord
                        {
                            Period      = yearM.Groups[1].Value,
                            Days        = dayMs.First(),
                            IsYearTotal = true
                        });
                    }
                    continue;
                }

                var pm = PeriodRegex.Match(text);
                if (!pm.Success) continue;

                string period = $"{pm.Groups["year"].Value}/{pm.Groups["month"].Value}";

                var dayMatch = Regex.Match(text,
                    @"(Asıl|Asil|ASIL|Ek|EK|İptal|IPTAL)\s+(?<d>\d{1,2})\b",
                    RegexOptions.IgnoreCase);

                int days = 0;
                if (dayMatch.Success && int.TryParse(dayMatch.Groups["d"].Value, out int fd) && fd <= 30)
                    days = fd;

                var moneyM = MoneyRegex.Match(text);
                decimal? pek = moneyM.Success ? ParseMoney(moneyM.Value) : null;

                result.Add(new PremiumParsedRecord
                {
                    Period      = period,
                    Days        = days,
                    PekAmount   = pek,
                    IsYearTotal = false
                });
            }

            return result;
        }

        // ─── Toplam yardımcıları ──────────────────────────────────────────────────

        private int? SumFromYearTotals(List<PremiumParsedRecord> records)
        {
            var yearTotals = records.Where(r => r.IsYearTotal).ToList();
            if (!yearTotals.Any()) return null;
            int sum = yearTotals.Sum(r => r.Days);
            return sum > 0 ? sum : null;
        }

        private int? SumFromMonthlyRecords(List<PremiumParsedRecord> records)
        {
            var monthly = records.Where(r => !r.IsYearTotal).ToList();
            if (!monthly.Any()) return null;
            int sum = monthly.Sum(r => r.Days);
            return sum > 0 ? sum : null;
        }

        // ─── Debug metin ──────────────────────────────────────────────────────────

        private string BuildDebugText(List<List<List<WordInfo>>> allPageData)
        {
            var sb = new StringBuilder();

            for (int pi = 0; pi < allPageData.Count; pi++)
            {
                sb.AppendLine($"=== Sayfa {pi + 1} ===");
                foreach (var row in allPageData[pi].OrderByDescending(r => r[0].Bottom))
                {
                    var lineText = string.Join(" ", row.Select(w => w.Text));
                    if (!string.IsNullOrWhiteSpace(lineText))
                        sb.AppendLine(lineText.Trim());
                }
            }

            return sb.ToString();
        }

        // ─── Para birimi çevirici ────────────────────────────────────────────────

        private decimal? ParseMoney(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            string normalized;

            if (raw.Contains('.') && raw.Contains(','))
            {
                int dotIndex   = raw.LastIndexOf('.');
                int commaIndex = raw.LastIndexOf(',');

                // Son işaret ondalık ayracı — hangisi daha sonda?
                if (dotIndex > commaIndex)
                {
                    // İngiliz: 20,002.50 → binlik virgül, ondalık nokta
                    normalized = raw.Replace(",", "");
                }
                else
                {
                    // Türk: 20.002,50 → binlik nokta, ondalık virgül
                    normalized = raw.Replace(".", "").Replace(",", ".");
                }
            }
            else if (raw.Contains(','))
            {
                // Sadece virgül: 1,079.10 veya 1079,10
                // Virgülden sonra tam 2 rakam varsa ondalık, değilse binlik
                var parts = raw.Split(',');
                if (parts.Length == 2 && parts[1].Length == 2)
                    normalized = raw.Replace(",", ".");  // ondalık virgül
                else
                    normalized = raw.Replace(",", "");   // binlik virgül
            }
            else
            {
                normalized = raw;
            }

            return decimal.TryParse(normalized, NumberStyles.Any,
                                    CultureInfo.InvariantCulture, out var result)
                ? result
                : null;
        }

        // ─── İç veri sınıfı ──────────────────────────────────────────────────────

        private class WordInfo
        {
            public string Text   { get; }
            public double Left   { get; }
            public double Bottom { get; }
            public double Top    { get; }
            public double Right  { get; }
            public double CenterX => (Left + Right) / 2.0;
            public double CenterY => (Bottom + Top) / 2.0;

            public WordInfo(string text, double left, double bottom, double top, double right)
            {
                Text   = text;
                Left   = left;
                Bottom = bottom;
                Top    = top;
                Right  = right;
            }
        }
    }
}
