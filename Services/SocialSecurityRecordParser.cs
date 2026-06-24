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
    ///
    /// Belge yapısı:
    ///   Başlık satırı: "4/1-a Gün Sayısı: 4/1-b Gün Sayısı: 4/1-c Gün Sayısı: GM20 Gün Sayısı: Toplam Gün Sayısı:"
    ///   Değer satırı : "821 0 0 0 821"               (küçük gün sayısı)
    ///                  "2.086 0 0 0 2.086"            (1000+ gün — Türkçe binlik nokta ile)
    ///
    /// GEÇMİŞTE GÖRÜLEN SORUN (gerçek test verisiyle doğrulandı):
    /// PdfPig'in kelime ayıklayıcısı bitişik bir rakam dizisini ("2086") bazı
    /// fontlarda/PDF'lerde TEK kelime olarak değil, "208" + "6" gibi İKİ AYRI
    /// kelime/token olarak döndürebiliyor. Önceki sürüm her sayı parçasını
    /// "en yakın TEK nokta" (başlık kelimesinin merkezi) mantığıyla en yakın
    /// sütuna atıyordu. Bu, parçanın gerçekte hangi sütuna ait olduğundan
    /// bağımsız olarak en yakın anchor'a kayabiliyordu — sonuç: "2086" parçası
    /// "208" (4A'ya) ve "6" (4B'ye) diye YANLIŞ İKİ SÜTUNA bölünüyordu.
    ///
    /// ÇÖZÜM — ARALIK (INTERVAL) TABANLI SÜTUN EŞLEME:
    /// "En yakın nokta" yerine, başlık satırındaki her etiketin SOL X
    /// konumundan başlayan, kendinden sonraki etiketin başına kadar süren
    /// 5 ayrık (kesişmeyen) X aralığı kuruluyor:
    ///
    ///   [-∞, 4B_start)      → 4A sütunu
    ///   [4B_start, 4C_start) → 4B sütunu
    ///   [4C_start, GM20_start) → 4C sütunu
    ///   [GM20_start, Toplam_start) → GM20 sütunu
    ///   [Toplam_start, +∞)  → Toplam sütunu
    ///
    /// Değer satırındaki HER sayısal parça (PdfPig onu nasıl bölmüş olursa
    /// olsun) merkez X'ine göre bu aralıklardan birine düşer. Aynı aralığa
    /// düşen tüm parçalar, soldan sağa sırayla string olarak birleştirilir
    /// (örn. "208" + "6" → "2086"). Bu yöntem PdfPig'in kelime bölme
    /// sezgisinden tamamen bağımsızdır — bir sayı 1, 2 ya da 3 parçaya
    /// bölünmüş olsa da aynı aralığa düştükleri için doğru birleşirler.
    /// Türkçe binlik ayraç (".") da aynı aralığa düştüğü için otomatik
    /// doğru sıraya birleşir, sonradan temizlenir.
    /// </summary>
    public class SocialSecurityRecordParser
    {
        private const double RowTolerance = 4.0;

        // Değer satırında olabilecek her token: sadece rakam ve/veya nokta
        private static readonly Regex NumericTokenRegex = new Regex(
            @"^[0-9.]+$",
            RegexOptions.Compiled);

        private static readonly Regex RegistrationDateRegex = new Regex(
            @"(\d{2}/\d{2}/\d{4})\s+tarihinden\s+itibaren\s+tescili?\s+bulundu",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Yedek (fallback) metin taramasında kullanılan sayı regex'i (binlik ayraç destekli)
        private static readonly Regex NumberInTextRegex = new Regex(
            @"\d{1,3}(?:\.\d{3})*",
            RegexOptions.Compiled);

        public SocialSecurityRecordDocument Parse(string filePath)
        {
            var result = new SocialSecurityRecordDocument();
            var debugLog = new List<string>();

            try
            {
                var rows = ExtractRows(filePath);
                string rawDump = BuildDebugText(rows);
                string flatText = string.Join(" ", rows.Select(r => string.Join(" ", r.Select(w => w.Text))));

                debugLog.Add("=== HAM ÇIKARILMIŞ SATIRLAR ===");
                debugLog.Add(rawDump.TrimEnd());

                bool readOk = ParseDayValuesByColumns(rows, result, debugLog);

                if (!readOk)
                {
                    debugLog.Add("Sütun bazlı yöntem başarısız oldu, metin tabanlı yedek yöntem deneniyor...");
                    readOk = ParseDayValuesFallback(flatText, result, debugLog);
                }

                ParseRegistrationInfo(flatText, result);
                ApplyTextualCrossCheck(flatText, result);

                debugLog.Add("=== SONUÇ ===");
                debugLog.Add($"Days4A={Fmt(result.Days4A)}, Days4B={Fmt(result.Days4B)}, " +
                             $"Days4C={Fmt(result.Days4C)}, DaysGM20={Fmt(result.DaysGM20)}, " +
                             $"TotalDays={Fmt(result.TotalDays)}");
                debugLog.Add($"Has4A={result.Has4A}, Has4B={result.Has4B}, Has4C={result.Has4C}");

                result.Status = readOk
                    ? "Analiz edildi"
                    : "Kısmi okuma — gün sayıları tespit edilemedi, belgeyi kontrol edin";

                result.ExtractedText = rawDump + Environment.NewLine +
                                        "=== PARSER DEBUG ===" + Environment.NewLine +
                                        string.Join(Environment.NewLine, debugLog.Skip(2));
            }
            catch (Exception ex)
            {
                result.ExtractedText = $"PDF okuma hatası: {ex.Message}";
                result.Status = "Okuma hatası";
            }

            return result;
        }

        private static string Fmt(int? v) => v.HasValue ? v.Value.ToString() : "null";

        // ── Koordinat tabanlı satır çıkarma (PremiumPdfParser ile aynı mantık) ──
        private List<List<WordInfo>> ExtractRows(string filePath)
        {
            var options = new ParsingOptions { SkipMissingFonts = true, UseLenientParsing = true };
            var allRows = new List<List<WordInfo>>();

            using var doc = PdfDocument.Open(filePath, options);

            foreach (var page in doc.GetPages())
            {
                var words = page.GetWords()
                    .Select(w => new WordInfo(w.Text, w.BoundingBox.Left, w.BoundingBox.Right,
                                               w.BoundingBox.Bottom, w.BoundingBox.Top))
                    .Where(w => !string.IsNullOrWhiteSpace(w.Text))
                    .ToList();

                if (words.Count == 0)
                {
                    if (!string.IsNullOrWhiteSpace(page.Text))
                    {
                        int li = 0;
                        foreach (var line in page.Text.Split('\n'))
                        {
                            if (string.IsNullOrWhiteSpace(line)) continue;
                            allRows.Add(new List<WordInfo>
                            {
                                new WordInfo(line.Trim(), 0, 500, li * -12, li * -12 + 10)
                            });
                            li++;
                        }
                    }
                    continue;
                }

                allRows.AddRange(GroupIntoRows(words));
            }

            return allRows;
        }

        private List<List<WordInfo>> GroupIntoRows(List<WordInfo> words)
        {
            var sorted = words.OrderByDescending(w => w.Bottom).ToList();
            var rows = new List<List<WordInfo>>();

            foreach (var word in sorted)
            {
                bool placed = false;

                foreach (var row in rows)
                {
                    var rep = row[0];
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

        // ── Ana yöntem: ARALIK (interval) tabanlı sütun eşleme ──────────────────
        private bool ParseDayValuesByColumns(List<List<WordInfo>> rows, SocialSecurityRecordDocument result, List<string> debugLog)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];

                var w4a = row.FirstOrDefault(w => Regex.IsMatch(w.Text, @"4[/\\]1[\s\-]?a", RegexOptions.IgnoreCase));
                var w4b = row.FirstOrDefault(w => Regex.IsMatch(w.Text, @"4[/\\]1[\s\-]?b", RegexOptions.IgnoreCase));
                var w4c = row.FirstOrDefault(w => Regex.IsMatch(w.Text, @"4[/\\]1[\s\-]?c", RegexOptions.IgnoreCase));

                var wGm = row.FirstOrDefault(w => Regex.IsMatch(w.Text, @"GM\s*-?\s*20", RegexOptions.IgnoreCase));
                if (wGm == null)
                {
                    // Bazı belgelerde "GM" ve "20" iki ayrı kelime olabilir
                    for (int k = 0; k < row.Count - 1; k++)
                    {
                        if (string.Equals(row[k].Text, "GM", StringComparison.OrdinalIgnoreCase) &&
                            row[k + 1].Text.TrimStart('-').StartsWith("20"))
                        {
                            wGm = row[k];
                            break;
                        }
                    }
                }

                var wToplam = row.FirstOrDefault(w =>
                    string.Equals(w.Text.TrimEnd(':'), "Toplam", StringComparison.OrdinalIgnoreCase));

                bool hasGunKelimesi = row.Any(w =>
                    w.Text.IndexOf("Gün", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    w.Text.IndexOf("Gun", StringComparison.OrdinalIgnoreCase) >= 0);

                if (w4a == null || w4b == null || w4c == null || wGm == null || wToplam == null || !hasGunKelimesi)
                    continue;

                debugLog.Add("Bulunan başlık satırı: " + RowText(row));

                // ── 5 sütunluk AYRIK X aralığını kur (etiketin SOL X'inden başlar) ──
                var labelStarts = new List<(string Key, double Start)>
                {
                    ("4A", w4a.Left),
                    ("4B", w4b.Left),
                    ("4C", w4c.Left),
                    ("GM20", wGm.Left),
                    ("Toplam", wToplam.Left)
                }.OrderBy(t => t.Start).ToList();

                var zones = new List<(string Key, double Start, double End)>();
                for (int z = 0; z < labelStarts.Count; z++)
                {
                    double start = (z == 0) ? double.MinValue : labelStarts[z].Start;
                    double end = (z + 1 < labelStarts.Count) ? labelStarts[z + 1].Start : double.MaxValue;
                    zones.Add((labelStarts[z].Key, start, end));
                }

                // ── Değer satırını ara: başlığın altındaki ilk birkaç satır ──
                for (int j = i + 1; j < Math.Min(i + 4, rows.Count); j++)
                {
                    var valueRow = rows[j];

                    var numericTokens = valueRow
                        .Where(w => NumericTokenRegex.IsMatch(w.Text))
                        .OrderBy(w => w.Left)
                        .ToList();

                    if (numericTokens.Count == 0) continue;

                    var zoneText = zones.ToDictionary(z => z.Key, z => new StringBuilder());

                    foreach (var tok in numericTokens)
                    {
                        var zone = zones.FirstOrDefault(z => tok.CenterX >= z.Start && tok.CenterX < z.End);
                        string zoneKey = zone.Key ?? zones
                            .OrderBy(z => Math.Min(Math.Abs(tok.CenterX - z.Start), Math.Abs(tok.CenterX - z.End)))
                            .First().Key;

                        zoneText[zoneKey].Append(tok.Text);
                    }

                    int zonesWithData = zoneText.Values.Count(sb => sb.Length > 0);
                    if (zonesWithData < 3) continue; // bu satır gerçek değer satırı değilmiş, sonraki satıra bak

                    debugLog.Add("Bulunan değer satırı: " + RowText(valueRow));
                    foreach (var z in zones)
                        debugLog.Add($"  Sütuna atanan token → [{z.Key}] = \"{zoneText[z.Key]}\"");

                    int? d4a = zoneText["4A"].Length > 0 ? ParseTurkishInt(zoneText["4A"].ToString()) : (int?)null;
                    int? d4b = zoneText["4B"].Length > 0 ? ParseTurkishInt(zoneText["4B"].ToString()) : (int?)null;
                    int? d4c = zoneText["4C"].Length > 0 ? ParseTurkishInt(zoneText["4C"].ToString()) : (int?)null;
                    int? dgm = zoneText["GM20"].Length > 0 ? ParseTurkishInt(zoneText["GM20"].ToString()) : (int?)null;
                    int? dTot = zoneText["Toplam"].Length > 0 ? ParseTurkishInt(zoneText["Toplam"].ToString()) : (int?)null;

                    // 4A ve Toplam okunamadıysa bu satır geçersiz say, sonraki satıra bak
                    if (d4a == null || dTot == null) continue;

                    result.Days4A = d4a;
                    result.Days4B = d4b ?? 0;
                    result.Days4C = d4c ?? 0;
                    result.DaysGM20 = dgm ?? 0;
                    result.TotalDays = dTot;

                    SetTescilFlags(result);

                    // ── Tutarlılık kontrolü (sadece bilgi amaçlı, sonucu bloke etmez) ──
                    int sum = (result.Days4A ?? 0) + (result.Days4B ?? 0) + (result.Days4C ?? 0) + (result.DaysGM20 ?? 0);
                    if (result.TotalDays.HasValue && result.TotalDays.Value != sum)
                    {
                        debugLog.Add($"  UYARI — tutarsızlık: Toplam={result.TotalDays}, " +
                                     $"sütunlar toplamı (4A+4B+4C+GM20)={sum}. Belge/okuma kontrol edilmeli.");
                    }
                    else
                    {
                        debugLog.Add("  Tutarlılık kontrolü: OK (Toplam = 4A+4B+4C+GM20 toplamı)");
                    }

                    return true;
                }

                debugLog.Add("Başlık bulundu ama altında geçerli bir değer satırı bulunamadı.");
            }

            return false;
        }

        // ── Yedek yöntem: düz metin üzerinde regex (sütun eşleme tamamen başarısız olursa) ──
        private bool ParseDayValuesFallback(string flatText, SocialSecurityRecordDocument result, List<string> debugLog)
        {
            var headerPattern = new Regex(
                @"4[/\\]1[\s\-]?a\s+G[üu]n\s+Say[ıi]s[ıi]\s*:.*?Toplam\s+G[üu]n\s+Say[ıi]s[ıi]\s*:",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            var headerMatch = headerPattern.Match(flatText);
            if (!headerMatch.Success)
            {
                debugLog.Add("Yedek yöntem: başlık deseni de bulunamadı.");
                return false;
            }

            string afterHeader = flatText.Substring(headerMatch.Index + headerMatch.Length);

            var numberMatches = NumberInTextRegex.Matches(afterHeader)
                .Cast<Match>()
                .Take(5)
                .ToList();

            if (numberMatches.Count < 5)
            {
                debugLog.Add($"Yedek yöntem: başlık bulundu ama sonrasında yalnızca {numberMatches.Count} sayı bulundu (5 gerekli).");
                return false;
            }

            result.Days4A = ParseTurkishInt(numberMatches[0].Value);
            result.Days4B = ParseTurkishInt(numberMatches[1].Value);
            result.Days4C = ParseTurkishInt(numberMatches[2].Value);
            result.DaysGM20 = ParseTurkishInt(numberMatches[3].Value);
            result.TotalDays = ParseTurkishInt(numberMatches[4].Value);

            debugLog.Add("Yedek yöntem ile okundu: " + string.Join(", ", numberMatches.Select(m => m.Value)));

            SetTescilFlags(result);
            return true;
        }

        // ── Sayısal okuma başarısız olduğunda metinden tamamlayıcı tespit ───
        // NOT: Var olan sayısal değerleri EZMEZ, sadece null kalanları tamamlar.
        private void ApplyTextualCrossCheck(string text, SocialSecurityRecordDocument result)
        {
            if (result.Days4A == null &&
                Regex.IsMatch(text, @"4[/\\]1-?a.*tescili?\s+bulundu", RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                result.Has4A = true;
            }

            if (result.Days4B == null &&
                Regex.IsMatch(text, @"4b\s+tescil\s+kayd[ıi]\s+bulunmamaktad[ıi]r", RegexOptions.IgnoreCase))
            {
                result.Days4B = 0;
                result.Has4B = false;
            }

            if (result.Days4C == null &&
                Regex.IsMatch(text, @"4c\s+tescil\s+kayd[ıi]\s+bulunmamaktad[ıi]r", RegexOptions.IgnoreCase))
            {
                result.Days4C = 0;
                result.Has4C = false;
            }
        }

        private void SetTescilFlags(SocialSecurityRecordDocument result)
        {
            result.Has4A = (result.Days4A ?? 0) > 0;
            result.Has4B = (result.Days4B ?? 0) > 0;
            result.Has4C = (result.Days4C ?? 0) > 0;
        }

        // ── Tescil tarihlerini çöz ───────────────────────────────────────────
        private void ParseRegistrationInfo(string flatText, SocialSecurityRecordDocument result)
        {
            var matches = RegistrationDateRegex.Matches(flatText);
            var dates = matches
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

            if (dates.Any())
            {
                result.FirstRegistrationDate = dates.First();
                result.LastRegistrationDate = dates.Last();
            }

            result.IsActive = result.LastRegistrationDate.HasValue
                && result.LastRegistrationDate.Value >= DateTime.Now.AddMonths(-12);
        }

        // ── Debug metin (satır satır, görsel sıraya göre) ───────────────────
        private string BuildDebugText(List<List<WordInfo>> rows)
        {
            var sb = new StringBuilder();
            foreach (var row in rows)
            {
                var lineText = string.Join(" ", row.Select(w => w.Text));
                if (!string.IsNullOrWhiteSpace(lineText))
                    sb.AppendLine(lineText.Trim());
            }
            return sb.ToString();
        }

        private string RowText(List<WordInfo> row) => string.Join(" ", row.Select(w => w.Text));

        // ── Türkçe binlik ayraçlı sayıyı (2.086 → 2086) int'e çevir ─────────
        private int ParseTurkishInt(string s)
        {
            var cleaned = s.Replace(".", "").Trim();
            return int.TryParse(cleaned, out int v) ? v : 0;
        }

        // ── İç veri sınıfı (PremiumPdfParser'daki WordInfo ile aynı mantık) ──
        private class WordInfo
        {
            public string Text { get; }
            public double Left { get; }
            public double Right { get; }
            public double Bottom { get; }
            public double Top { get; }
            public double CenterX => (Left + Right) / 2.0;

            public WordInfo(string text, double left, double right, double bottom, double top)
            {
                Text = text;
                Left = left;
                Right = right;
                Bottom = bottom;
                Top = top;
            }
        }
    }
}