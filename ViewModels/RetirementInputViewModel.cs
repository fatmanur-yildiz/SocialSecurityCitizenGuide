using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace EmekliRehberi.ViewModels
{
    public class RetirementInputViewModel
    {
        // ── Sosyal Güvenlik Kayıt Belgesi yükleme ───────────────
        public IFormFile? SocialSecurityPdf { get; set; }

        // ── Kullanıcıdan alınan manuel bilgiler ─────────────────

        [Display(Name = "Doğum Tarihi")]
        [DataType(DataType.Date)]
        public DateTime? BirthDate { get; set; }

        [Display(Name = "Cinsiyet")]
        public string? Gender { get; set; }  // "Erkek" | "Kadın"

        [Display(Name = "İlk Sigorta Başlangıç Tarihi")]
        [DataType(DataType.Date)]
        public DateTime? FirstInsuranceDate { get; set; }

        [Display(Name = "Hedef Sigorta Statüsü")]
        public string? TargetStatus { get; set; }  // "4A" | "4B" | "4C" | "Bilmiyorum"

        [Display(Name = "Askerlik Borçlanması")]
        public string? MilitaryDebt { get; set; }  // "Evet" | "Hayır" | "Bilmiyorum"

        [Display(Name = "Doğum Borçlanması")]
        public string? MaternityDebt { get; set; }

        [Display(Name = "Yurt Dışı Borçlanma")]
        public string? AbroadDebt { get; set; }

        [Display(Name = "Daha Önce Emeklilik Başvurusu Yaptı mı?")]
        public bool PreviousApplication { get; set; } = false;

        // ── e-Devlet sonucu (opsiyonel) ─────────────────────────
        [Display(Name = "Tahmini Emeklilik Tarihi (e-Devlet)")]
        [DataType(DataType.Date)]
        public DateTime? EstimatedRetirementDate { get; set; }

        [Display(Name = "Gereken Prim Günü")]
        public int? RequiredPremiumDays { get; set; }

        [Display(Name = "Gereken Yaş")]
        public int? RequiredAge { get; set; }

        [Display(Name = "Gereken Sigortalılık Süresi (Yıl)")]
        public int? RequiredInsuranceYears { get; set; }
    }
}
