using System.ComponentModel.DataAnnotations;

namespace EmekliRehberi.ViewModels
{
    public class ServiceStatementViewModel
    {
        [Required(ErrorMessage = "İlk işe giriş tarihi zorunludur.")]
        public DateTime FirstWorkDate { get; set; }

        [Required(ErrorMessage = "Toplam prim günü zorunludur.")]
        [Range(0, 20000, ErrorMessage = "Prim günü geçerli aralıkta olmalıdır.")]
        public int TotalPremiumDays { get; set; }

        [Required(ErrorMessage = "Sigorta statüsü zorunludur.")]
        public string InsuranceStatus { get; set; } = "4A";

        public string? LastPeriod { get; set; }

        public IFormFile? PdfFile { get; set; }
    }
}