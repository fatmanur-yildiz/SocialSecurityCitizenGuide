using System;
using System.Linq;
using EmekliRehberi.Models;

namespace EmekliRehberi.Services
{
    /// <summary>
    /// İlk sigorta başlangıç tarihini, birden fazla olası kaynak arasından
    /// öncelik sırasına göre belirler. Hiçbir kullanıcıya özel sabit değer
    /// içermez — tamamen elindeki belge/giriş verisine göre çalışır.
    ///
    /// Öncelik sırası:
    ///   1) Kullanıcının manuel girdiği tarih (varsa, her zaman en öncelikli)
    ///   2) Hizmet Dökümü (PremiumRecord) içindeki en eski "Giriş Tarihi" / en eski "Dönem"
    ///      — bu ikisinden HANGİSİ daha eskiyse o kullanılır.
    ///   3) Sosyal Güvenlik Kayıt Belgesi'ndeki "İlk Tescil Tarihi"
    ///
    /// 2. ve 3. kaynak birlikte mevcutsa ve Hizmet Dökümü'nden çıkan tarih
    /// Kayıt Belgesi'nden DAHA ESKİYSE, Hizmet Dökümü tarihi kullanılır
    /// (çünkü Kayıt Belgesi genelde sadece HALEN AKTİF olan tescilleri
    /// listeler; geçmişte biten kısa süreli ilk işler bu belgede hiç
    /// görünmeyebilir). İki kaynak arasında fark varsa kısa bir not üretilir.
    /// </summary>
    public static class InsuranceDateResolver
    {
        public class ResolvedInsuranceDate
        {
            /// <summary>Değerlendirmede kullanılacak, sonuçta seçilen tarih.</summary>
            public DateTime? Date { get; set; }

            /// <summary>Seçilen tarihin kaynağı (kullanıcıya gösterilecek kısa metin).</summary>
            public string? Source { get; set; }

            /// <summary>Sosyal Güvenlik Kayıt Belgesi'ndeki tescil tarihi (varsa), kıyaslama/bilgi amaçlı.</summary>
            public DateTime? RegistrationDocumentDate { get; set; }

            /// <summary>Hizmet Dökümü'nden elde edilen tarih (varsa), kıyaslama/bilgi amaçlı.</summary>
            public DateTime? ServiceRecordDate { get; set; }

            /// <summary>İki belge arasında fark olup olmadığı.</summary>
            public bool HasConflict { get; set; }

            /// <summary>Fark varsa kullanıcıya gösterilecek kısa not.</summary>
            public string? Note { get; set; }
        }

        public const string SourceManual = "Kullanıcı girişi";
        public const string SourceServiceEntryDate = "Hizmet Dökümü Giriş Tarihi sütunu";
        public const string SourceServicePeriod = "Hizmet Dökümü en eski prim dönemi";
        public const string SourceRegistrationDocument = "Sosyal Güvenlik Kayıt Belgesi";

        public static ResolvedInsuranceDate Resolve(
            DateTime? manualDate,
            PremiumDocument? premiumDocument,
            SocialSecurityRecordDocument? socialSecurityDocument)
        {
            var result = new ResolvedInsuranceDate
            {
                RegistrationDocumentDate = socialSecurityDocument?.FirstRegistrationDate
            };

            // 1) Kullanıcı manuel tarih girdiyse her zaman en öncelikli.
            if (manualDate.HasValue)
            {
                result.Date = manualDate;
                result.Source = SourceManual;
                return result;
            }

            // 2) Hizmet Dökümü'nden en eski "Giriş Tarihi" ve en eski "Dönem"i ayrı ayrı bul.
            DateTime? earliestEntryDate = null;
            DateTime? earliestPeriodDate = null;

            if (premiumDocument?.Records != null && premiumDocument.Records.Count > 0)
            {
                var realRecords = premiumDocument.Records.Where(r => !r.IsYearTotal).ToList();

                earliestEntryDate = realRecords
                    .Where(r => r.EntryDate.HasValue)
                    .Select(r => r.EntryDate)
                    .OrderBy(d => d)
                    .FirstOrDefault();

                earliestPeriodDate = realRecords
                    .Where(r => !string.IsNullOrWhiteSpace(r.Period))
                    .Select(r => ParsePeriodToDate(r.Period))
                    .Where(d => d.HasValue)
                    .OrderBy(d => d)
                    .FirstOrDefault();
            }

            // EntryDate ile Period arasında "hangisi daha eski" kıyaslaması YAPILMAZ.
            // EntryDate varsa (Hizmet Dökümü'ndeki gerçek "Giriş Tarihi" sütunundan
            // okunduğu için) her zaman önceliklidir. Period (örn. "2020/01" → 01.01.2020)
            // yalnızca hiçbir kayıtta EntryDate bulunamadığında devreye giren bir
            // yedek/fallback'tir — asla EntryDate'in önüne geçmez.
            DateTime? serviceDate;
            string? serviceSource;

            if (earliestEntryDate.HasValue)
            {
                serviceDate = earliestEntryDate;
                serviceSource = SourceServiceEntryDate;
            }
            else if (earliestPeriodDate.HasValue)
            {
                serviceDate = earliestPeriodDate;
                serviceSource = SourceServicePeriod;
            }
            else
            {
                serviceDate = null;
                serviceSource = null;
            }

            result.ServiceRecordDate = serviceDate;

            var registrationDate = socialSecurityDocument?.FirstRegistrationDate;

            // 3) Hizmet Dökümü ve Kayıt Belgesi'ni kıyasla.
            if (serviceDate.HasValue && registrationDate.HasValue)
            {
                if (serviceDate.Value < registrationDate.Value)
                {
                    result.Date = serviceDate;
                    result.Source = serviceSource;
                }
                else
                {
                    result.Date = registrationDate;
                    result.Source = SourceRegistrationDocument;
                }

                if (serviceDate.Value.Date != registrationDate.Value.Date)
                {
                    result.HasConflict = true;
                    result.Note = "Belgelerde farklı başlangıç tarihleri görüldü. İlk sigorta tarihi ayrıca kontrol edilmelidir.";
                }
            }
            else if (serviceDate.HasValue)
            {
                result.Date = serviceDate;
                result.Source = serviceSource;
            }
            else if (registrationDate.HasValue)
            {
                result.Date = registrationDate;
                result.Source = SourceRegistrationDocument;
            }

            return result;
        }

        /// <summary>
        /// "2020/01" → 01.01.2020. Sadece yıl içeren ("2020") değerler için
        /// (örn. yıllık toplam satırları) 01.01.yyyy döndürür; bu satırlar
        /// normalde IsYearTotal=true olduğu için çağıran taraf zaten eler,
        /// burada sadece ek güvenlik amaçlı bırakılmıştır.
        /// </summary>
        private static DateTime? ParsePeriodToDate(string? period)
        {
            if (string.IsNullOrWhiteSpace(period)) return null;

            var parts = period.Trim().Split('/');

            if (parts.Length == 2 &&
                int.TryParse(parts[0], out int y) &&
                int.TryParse(parts[1], out int m) &&
                y > 1980 && y < 2100 && m >= 1 && m <= 12)
            {
                return new DateTime(y, m, 1);
            }

            if (parts.Length == 1 &&
                int.TryParse(parts[0], out int yOnly) &&
                yOnly > 1980 && yOnly < 2100)
            {
                return new DateTime(yOnly, 1, 1);
            }

            return null;
        }
    }
}
