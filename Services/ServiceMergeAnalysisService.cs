using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EmekliRehberi.Models;

namespace EmekliRehberi.Services
{
    /// <summary>
    /// 4A/4B/4C hizmet birleştirme ön değerlendirmesini, mevcut PremiumDocument ve
    /// SocialSecurityRecordDocument verileri üzerinden anlık olarak hesaplar.
    /// DB'ye herhangi bir kayıt yapmaz, bağımlılığı yoktur (static).
    /// </summary>
    public static class ServiceMergeAnalysisService
    {
        private static readonly DateTime Post2008Threshold = new DateTime(2008, 10, 1);

        public static ServiceMergeResult Analyze(
            PremiumDocument? premium,
            SocialSecurityRecordDocument? socialSecurity,
            string? targetStatus)
        {
            var result = new ServiceMergeResult
            {
                HasPremiumDocument = premium != null,
                HasSocialSecurityDocument = socialSecurity != null,
                TargetStatus = (string.IsNullOrWhiteSpace(targetStatus) || targetStatus == "Bilmiyorum")
                    ? null
                    : targetStatus,
                TotalPremiumDays = premium?.TotalPremiumDays
            };

            if (socialSecurity != null)
            {
                result.Days4A = socialSecurity.Days4A;
                result.Days4B = socialSecurity.Days4B;
                result.Days4C = socialSecurity.Days4C;
                result.DaysGM20 = socialSecurity.DaysGM20;
                result.TotalDaysFromSocialSecurity = socialSecurity.TotalDays;
            }

            result.EffectiveTotalDays = result.TotalDaysFromSocialSecurity ?? result.TotalPremiumDays ?? 0;

            var branchDays = new Dictionary<string, int>();
            if ((result.Days4A ?? 0) > 0) branchDays["4A"] = result.Days4A!.Value;
            if ((result.Days4B ?? 0) > 0) branchDays["4B"] = result.Days4B!.Value;
            if ((result.Days4C ?? 0) > 0) branchDays["4C"] = result.Days4C!.Value;

            result.ActiveBranches = branchDays.Keys.ToList();
            result.IsMixedService = branchDays.Count >= 2;

            if (branchDays.Count > 0)
            {
                var dominant = branchDays.OrderByDescending(b => b.Value).First();
                result.DominantBranch = dominant.Key;

                var sum = branchDays.Values.Sum();
                result.DominantBranchSharePercent = sum > 0
                    ? Math.Round((double)dominant.Value / sum * 100, 1)
                    : null;
            }

            if (result.TargetStatus != null && result.DominantBranch != null)
            {
                result.TargetMatchesDominant = result.TargetStatus == result.DominantBranch;
            }

            result.FirstInsuranceDateUsed = DetermineFirstInsuranceDate(premium, socialSecurity);
            result.IsPost2008Insured = result.FirstInsuranceDateUsed.HasValue
                ? result.FirstInsuranceDateUsed.Value >= Post2008Threshold
                : (bool?)null;

            bool branchDataReadable = socialSecurity != null &&
                (result.Days4A.HasValue || result.Days4B.HasValue || result.Days4C.HasValue);

            if (socialSecurity == null)
            {
                result.DataSufficiency = DataSufficiencyLevel.Eksik;
                result.DataSufficiencyLabel = "Veri Eksik";
            }
            else if (!branchDataReadable)
            {
                result.DataSufficiency = DataSufficiencyLevel.Sinirli;
                result.DataSufficiencyLabel = "Statü Bilgisi Okunamadı";
            }
            else if (branchDays.Count <= 1)
            {
                result.DataSufficiency = DataSufficiencyLevel.Yeterli;
                result.DataSufficiencyLabel = "Farklı Statü Tespit Edilmedi";
            }
            else if (result.IsPost2008Insured == true)
            {
                result.DataSufficiency = DataSufficiencyLevel.Yeterli;
                result.DataSufficiencyLabel = "Statü Dağılımı Okunabildi";
            }
            else
            {
                result.DataSufficiency = DataSufficiencyLevel.Sinirli;
                result.DataSufficiencyLabel = "Son 2520 Gün Analizi İçin Dönemsel Statü Bilgisi Sınırlı";
            }

            result.YourSituationSummary = BuildYourSituationSummary(result, branchDays);
            result.AnalysisItems = BuildAnalysisItems(result, premium, socialSecurity, branchDays);
            result.AdditionalNotes = BuildAdditionalNotes(result, premium, socialSecurity);
            result.MissingDataNotices = BuildMissingNotices(premium, socialSecurity);

            return result;
        }

        private static DateTime? DetermineFirstInsuranceDate(PremiumDocument? premium, SocialSecurityRecordDocument? socialSecurity)
        {
            if (socialSecurity?.FirstRegistrationDate.HasValue == true)
                return socialSecurity.FirstRegistrationDate;

            if (premium?.Records != null)
            {
                var firstEntry = premium.Records
                    .Where(r => r.EntryDate.HasValue)
                    .OrderBy(r => r.EntryDate)
                    .FirstOrDefault();

                if (firstEntry?.EntryDate != null)
                    return firstEntry.EntryDate;

                var firstPeriod = premium.Records
                    .Where(r => !r.IsYearTotal && !string.IsNullOrWhiteSpace(r.Period))
                    .OrderBy(r => r.Period)
                    .FirstOrDefault();

                if (firstPeriod != null &&
                    DateTime.TryParseExact(firstPeriod.Period + "/01", "yyyy/MM/dd",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var pd))
                {
                    return pd;
                }
            }

            return null;
        }

        private static string BuildYourSituationSummary(ServiceMergeResult result, Dictionary<string, int> branchDays)
        {
            if (!result.HasSocialSecurityDocument)
            {
                return "Hizmet birleştirme açısından durumunuzu değerlendirebilmek için Sosyal Güvenlik Kayıt Belgesi'nin yüklenmesi gerekmektedir. Belge yüklendiğinde bu bölüm otomatik olarak güncellenecektir.";
            }

            if (branchDays.Count == 0)
            {
                return "Sosyal Güvenlik Kayıt Belgenizden 4A, 4B ve 4C gün bilgileri okunamadı. Bu nedenle durumunuz hakkında bir değerlendirme yapılamamaktadır; belgenizin güncel ve eksiksiz olduğundan emin olup gerekirse yeniden yüklemeniz önerilir.";
            }

            if (branchDays.Count == 1)
            {
                var only = branchDays.First();
                var others = new[] { "4A", "4B", "4C" }.Where(b => b != only.Key);
                return $"Mevcut belgelerinize göre {only.Key} hizmetiniz {only.Value} gün, {string.Join(" ve ", others)} hizmetiniz bulunmamaktadır. Bu nedenle mevcut belgelerle hizmet birleştirme açısından farklı statü riski görünmemektedir.";
            }

            var parts = branchDays.Select(b => $"{b.Key} hizmetiniz {b.Value} gün");
            var summary = $"Mevcut belgelerinize göre {string.Join(", ", parts)} olarak görünmektedir. Birden fazla statüde hizmetiniz bulunduğu için hizmet birleştirme açısından ayrıca değerlendirme yapılması önerilir.";

            if (result.DominantBranch != null)
                summary += $" Gün sayısı bakımından ağırlıklı statünüz {result.DominantBranch} olarak görünmektedir.";

            return summary;
        }

        private static List<ServiceMergeAnalysisItem> BuildAnalysisItems(
            ServiceMergeResult result,
            PremiumDocument? premium,
            SocialSecurityRecordDocument? socialSecurity,
            Dictionary<string, int> branchDays)
        {
            var items = new List<ServiceMergeAnalysisItem>();

            string differentStatusAnswer;
            ServiceMergeCommentType differentStatusType;

            if (socialSecurity == null)
            {
                differentStatusAnswer = "Bu sorunun cevaplanabilmesi için Sosyal Güvenlik Kayıt Belgesi'nin yüklenmesi gerekmektedir.";
                differentStatusType = ServiceMergeCommentType.Warning;
            }
            else if (branchDays.Count == 0)
            {
                differentStatusAnswer = "Statü bilgisi belgeden okunamadığı için bu soru şu an cevaplanamıyor.";
                differentStatusType = ServiceMergeCommentType.Warning;
            }
            else if (branchDays.Count == 1)
            {
                differentStatusAnswer = $"Hayır, kayıtlarınız tek statüde ({branchDays.Keys.First()}) görünmektedir.";
                differentStatusType = ServiceMergeCommentType.Success;
            }
            else
            {
                differentStatusAnswer = $"Evet, kayıtlarınızda {string.Join(" ve ", branchDays.Keys)} statülerinde hizmet görünmektedir.";
                differentStatusType = ServiceMergeCommentType.Warning;
            }

            items.Add(new ServiceMergeAnalysisItem
            {
                Question = "Farklı statüde hizmet var mı?",
                Answer = differentStatusAnswer,
                Type = differentStatusType
            });

            string dominantAnswer = result.DominantBranch != null
                ? result.DominantBranch + (result.DominantBranchSharePercent.HasValue
                    ? $" (statülü günlerin yaklaşık %{result.DominantBranchSharePercent.Value.ToString("0.0", CultureInfo.InvariantCulture)}'i)"
                    : "")
                : "Statü bilgisi okunamadığı için belirlenemedi.";

            items.Add(new ServiceMergeAnalysisItem
            {
                Question = "Ağırlıklı statünüz nedir?",
                Answer = dominantAnswer,
                Type = result.DominantBranch != null ? ServiceMergeCommentType.Info : ServiceMergeCommentType.Warning
            });

            string ruleAnswer;
            ServiceMergeCommentType ruleType;
            if (result.IsPost2008Insured == true)
            {
                ruleAnswer = "İlk sigorta başlangıcınız 01.10.2008 sonrası göründüğü için genel statü dağılımı üzerinden ön değerlendirme yapılmıştır.";
                ruleType = ServiceMergeCommentType.Info;
            }
            else if (result.IsPost2008Insured == false)
            {
                ruleAnswer = "İlk sigorta başlangıcınız 01.10.2008 öncesine ait görünmektedir. Bu durumda hangi statüden emeklilik değerlendirileceği konusunda son 2520 günlük (7 yıllık) dönemdeki ağırlıklı statü de ayrıca dikkate alınabilir.";
                ruleType = ServiceMergeCommentType.Warning;
            }
            else
            {
                ruleAnswer = "İlk sigorta başlangıç tarihiniz belirlenemediği için 2008 öncesi/sonrası kural farkı bu aşamada değerlendirilememektedir.";
                ruleType = ServiceMergeCommentType.Warning;
            }

            items.Add(new ServiceMergeAnalysisItem
            {
                Question = "2008 öncesi/sonrası hangi kural uygulanır?",
                Answer = ruleAnswer,
                Type = ruleType
            });

            items.Add(new ServiceMergeAnalysisItem
            {
                Question = "Son 2520 gün analizi yapılabildi mi?",
                Answer = "Hayır. Sistemde tarih bazlı (dönemsel) statü dökümü bulunmadığından son 2520 günlük dönem için ayrı bir hesap yapılamamaktadır. Bu bilginin gerekli olduğu durumlarda SGK'dan ayrıca teyit alınması önerilir.",
                Type = ServiceMergeCommentType.Info
            });

            var missing = BuildMissingNotices(premium, socialSecurity);
            items.Add(new ServiceMergeAnalysisItem
            {
                Question = "Eksik belge/bilgi var mı?",
                Answer = missing.Any()
                    ? string.Join(" ", missing)
                    : "Temel belgeler (SGK Tescil ve Hizmet Dökümü, Sosyal Güvenlik Kayıt Belgesi) yüklenmiş görünmektedir.",
                Type = missing.Any() ? ServiceMergeCommentType.Warning : ServiceMergeCommentType.Success
            });

            return items;
        }

        private static List<ServiceMergeComment> BuildAdditionalNotes(
            ServiceMergeResult result,
            PremiumDocument? premium,
            SocialSecurityRecordDocument? socialSecurity)
        {
            var notes = new List<ServiceMergeComment>();

            bool hasOnly4A = (result.Days4A ?? 0) > 0 && (result.Days4B ?? 0) == 0 && (result.Days4C ?? 0) == 0;
            bool hasNon4A = (result.Days4B ?? 0) > 0 || (result.Days4C ?? 0) > 0;

            if (result.HasSocialSecurityDocument && result.ActiveBranches.Count > 0)
            {
                if (hasOnly4A)
                {
                    notes.Add(new ServiceMergeComment
                    {
                        Title = "Statü Riski Değerlendirmesi",
                        Text = "Kayıtlarınıza göre hizmetleriniz 4A kapsamında görünmektedir. Farklı statüde hizmet görünmediği için hizmet birleştirme açısından özel bir statü riski tespit edilmemiştir.",
                        Type = ServiceMergeCommentType.Success
                    });
                }
                else if (hasNon4A)
                {
                    notes.Add(new ServiceMergeComment
                    {
                        Title = "Statü Riski Değerlendirmesi",
                        Text = "Farklı statülerde hizmetiniz bulunduğu için hizmet birleştirme değerlendirmesi önemlidir.",
                        Type = ServiceMergeCommentType.Warning
                    });
                }
            }

            if ((result.DaysGM20 ?? 0) > 0)
            {
                notes.Add(new ServiceMergeComment
                {
                    Title = "GM20 Kapsamında Hizmet",
                    Text = $"Kayıtlarınızda Geçici Madde 20 (GM20) kapsamında {result.DaysGM20} gün görünmektedir. Bu süreler özel sandık/banka sigortaları gibi farklı düzenlemelere tabi olabileceğinden ayrıca değerlendirilmesi önerilir.",
                    Type = ServiceMergeCommentType.Info
                });
            }

            if (premium?.HasMissingDays == true)
            {
                notes.Add(new ServiceMergeComment
                {
                    Title = "Eksik Gün Bilgisi",
                    Text = "Eksik günler toplam prim gününüzü etkileyebilir; detaylar Prim Bilgilerim sayfasında görülebilir.",
                    Type = ServiceMergeCommentType.Info
                });
            }

            if (premium?.TotalPremiumDays != null && socialSecurity?.TotalDays != null)
            {
                var diff = Math.Abs(premium.TotalPremiumDays.Value - socialSecurity.TotalDays.Value);
                if (diff > 30)
                {
                    notes.Add(new ServiceMergeComment
                    {
                        Title = "Belgeler Arası Gün Farkı",
                        Text = $"SGK Tescil ve Hizmet Dökümünüzdeki toplam gün ({premium.TotalPremiumDays} gün) ile Sosyal Güvenlik Kayıt Belgesi'ndeki toplam gün ({socialSecurity.TotalDays} gün) arasında {diff} günlük bir fark bulunmaktadır. Bu fark, belgelerin farklı tarihlerde alınmış olmasından veya henüz işlenmemiş bildirimlerden kaynaklanabilir.",
                        Type = ServiceMergeCommentType.Info
                    });
                }
            }

            if (result.TargetStatus != null && result.DominantBranch != null)
            {
                if (result.TargetMatchesDominant == true)
                {
                    notes.Add(new ServiceMergeComment
                    {
                        Title = "Hedef Statü Karşılaştırması",
                        Text = $"Hedeflediğinizi belirttiğiniz {result.TargetStatus} statüsü, mevcut kayıtlarınızdaki ağırlıklı statüyle uyumlu görünmektedir.",
                        Type = ServiceMergeCommentType.Success
                    });
                }
                else if (result.TargetMatchesDominant == false)
                {
                    notes.Add(new ServiceMergeComment
                    {
                        Title = "Hedef Statü Karşılaştırması",
                        Text = $"Hedeflediğinizi belirttiğiniz {result.TargetStatus} statüsünde, kayıtlarınızdaki ağırlıklı statü olan {result.DominantBranch}'a kıyasla daha az gününüz bulunmaktadır. Bu farkın emeklilik sürecinize etkisi ayrıca değerlendirilmelidir.",
                        Type = ServiceMergeCommentType.Warning
                    });
                }
            }

            return notes;
        }

        private static List<string> BuildMissingNotices(PremiumDocument? premium, SocialSecurityRecordDocument? socialSecurity)
        {
            var notices = new List<string>();

            if (socialSecurity == null)
                notices.Add("Sosyal Güvenlik Kayıt Belgesi yükleyin (Emeklilik Durumu sayfasından).");

            if (premium == null)
                notices.Add("SGK Tescil ve Hizmet Dökümü yükleyin (Prim Bilgilerim sayfasından).");

            return notices;
        }
    }
}