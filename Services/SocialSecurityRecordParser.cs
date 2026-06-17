using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using EmekliRehberi.Models;

namespace EmekliRehberi.Services
{
    /// <summary>
    /// "Sosyal Güvenlik Kayıt Belgesi Sorgulama" PDF'ini okur.
    /// Belge düzenli metin akışı içerdiğinden regex yeterlidir.
    /// Temel sorun: "4/1-a Gün Sayısı: 821" satırındaki "4" rakamı
    /// yanlış yakalanmamalı; tam regex kullanılmalı.
    /// </summary>
    public class SocialSecurityRecordParser
    {
        public SocialSecurityRecordDocument Parse(string filePath)
        {
            var result = new SocialSecurityRecordDocument();

            try
            {
                var text = ExtractText(filePath);
                result.ExtractedText = text;

                // ── Gün sayıları ──────────────────────────────────────────────
                // "4/1-a Gün Sayısı: 821" → tam eşleşme, baştaki 4 yakalanmasın
                result.Days4A   = ExtractExact(text, @"4/1-a\s+G[üu]n\s+Say[ıi]s[ıi]\s*:\s*(\d+)");
                result.Days4B   = ExtractExact(text, @"4/1-b\s+G[üu]n\s+Say[ıi]s[ıi]\s*:\s*(\d+)");
                result.Days4C   = ExtractExact(text, @"4/1-c\s+G[üu]n\s+Say[ıi]s[ıi]\s*:\s*(\d+)");
                result.DaysGM20 = ExtractExact(text, @"GM20\s+G[üu]n\s+Say[ıi]s[ıi]\s*:\s*(\d+)");
                result.TotalDays = ExtractExact(text, @"Toplam\s+G[üu]n\s+Say[ıi]s[ıi]\s*:\s*(\d+)");

                // Tablo satırından fallback: "821  0  0  0  821" gibi 5 sayı yan yana
                if (!result.Days4A.HasValue || !result.TotalDays.HasValue)
                    TryParseTableRow(text, result);

                // ── Tescil varlığı ────────────────────────────────────────────
                result.Has4A = (result.Days4A ?? 0) > 0
                    || Regex.IsMatch(text, @"4/1-a.*?tescil.*?tespit", RegexOptions.IgnoreCase | RegexOptions.Singleline);

                result.Has4B = (result.Days4B ?? 0) > 0
                    && !Regex.IsMatch(text, @"4[bB]\s+tescil\s+kayd[ıi]\s+bulunmamaktad[ıi]r", RegexOptions.IgnoreCase);

                result.Has4C = (result.Days4C ?? 0) > 0
                    && !Regex.IsMatch(text, @"4[cC]\s+tescil\s+kayd[ıi]\s+bulunmamaktad[ıi]r", RegexOptions.IgnoreCase);

                // ── Tescil tarihleri ──────────────────────────────────────────
                // "01/03/2024 tarihinden itibaren tescili bulunduğu"
                var dateMatches = Regex.Matches(text,
                    @"(\d{2}/\d{2}/\d{4})\s+tarihinden\s+itibaren\s+tescili?\s+bulundu",
                    RegexOptions.IgnoreCase);

                var parsedDates = dateMatches
                    .Cast<Match>()
                    .Select(m =>
                    {
                        DateTime.TryParseExact(m.Groups[1].Value, "dd/MM/yyyy",
                            CultureInfo.InvariantCulture, DateTimeStyles.None, out var d);
                        return d;
                    })
                    .Where(d => d != default && d.Year > 1980)
                    .OrderBy(d => d)
                    .ToList();

                if (parsedDates.Any())
                {
                    result.FirstRegistrationDate = parsedDates.First();
                    result.LastRegistrationDate  = parsedDates.Last();
                }

                // Aktif mi? Son tescil tarihi son 12 ay içindeyse aktif say
                result.IsActive = result.LastRegistrationDate.HasValue
                    && result.LastRegistrationDate.Value >= DateTime.Now.AddMonths(-12);

                result.Status = "Analiz edildi";
            }
            catch (Exception ex)
            {
                result.ExtractedText = $"PDF okuma hatası: {ex.Message}";
                result.Status = "Okuma hatası";
            }

            return result;
        }

        // ─── Belirli bir regex pattern'den tam sayı çek ───────────────────────
        private int? ExtractExact(string text, string pattern)
        {
            var m = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
            if (m.Success && int.TryParse(m.Groups[1].Value.Trim(), out int val))
                return val;
            return null;
        }

        // ─── Tablo satırı fallback: ardışık 5 sayıyı yakala ─────────────────
        // Belgedeki "821  0  0  0  821" formatı için
        private void TryParseTableRow(string text, SocialSecurityRecordDocument result)
        {
            // 5 ardışık tam sayı — 4A, 4B, 4C, GM20, Toplam sırasıyla
            var tablePattern = new Regex(
                @"(\d+)\s{2,}(\d+)\s{2,}(\d+)\s{2,}(\d+)\s{2,}(\d+)",
                RegexOptions.Multiline);

            var m = tablePattern.Match(text);
            if (!m.Success) return;

            if (!result.Days4A.HasValue   && int.TryParse(m.Groups[1].Value, out int a)) result.Days4A   = a;
            if (!result.Days4B.HasValue   && int.TryParse(m.Groups[2].Value, out int b)) result.Days4B   = b;
            if (!result.Days4C.HasValue   && int.TryParse(m.Groups[3].Value, out int c)) result.Days4C   = c;
            if (!result.DaysGM20.HasValue && int.TryParse(m.Groups[4].Value, out int g)) result.DaysGM20 = g;
            if (!result.TotalDays.HasValue && int.TryParse(m.Groups[5].Value, out int t)) result.TotalDays = t;
        }

        // ─── PDF'ten ham metin çek ────────────────────────────────────────────
        private string ExtractText(string filePath)
        {
            var sb = new StringBuilder();
            var options = new ParsingOptions { SkipMissingFonts = true, UseLenientParsing = true };

            using var doc = PdfDocument.Open(filePath, options);
            foreach (var page in doc.GetPages())
            {
                var words = page.GetWords().ToList();
                if (words.Any())
                {
                    var rows = words
                        .GroupBy(w => Math.Round(w.BoundingBox.Bottom, 0))
                        .OrderByDescending(g => g.Key)
                        .Select(g => string.Join(" ",
                            g.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text)));

                    foreach (var row in rows)
                        sb.AppendLine(row.Trim());
                }
                else if (!string.IsNullOrWhiteSpace(page.Text))
                {
                    sb.AppendLine(page.Text);
                }
            }

            return sb.ToString();
        }
    }
}
