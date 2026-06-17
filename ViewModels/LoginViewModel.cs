using System.ComponentModel.DataAnnotations;

namespace EmekliRehberi.ViewModels
{
    public class LoginViewModel
    {
        [Required(ErrorMessage = "T.C. kimlik numarası veya e-posta alanı zorunludur.")]
        public string TcKimlikNoOrEmail { get; set; } = "";

        [Required(ErrorMessage = "Şifre alanı zorunludur.")]
        public string Password { get; set; } = "";
    }
}