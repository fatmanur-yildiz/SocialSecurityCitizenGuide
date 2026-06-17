using System.ComponentModel.DataAnnotations;

namespace EmekliRehberi.ViewModels
{
    public class PremiumUploadViewModel
    {
        [Required(ErrorMessage = "Prim bilgileri için SGK Tescil ve Hizmet Dökümü PDF dosyası seçilmelidir.")]
        public IFormFile? PdfFile { get; set; }
    }
}