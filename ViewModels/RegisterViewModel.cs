using System.ComponentModel.DataAnnotations;

namespace EmekliRehberi.ViewModels
{
    public class RegisterViewModel
    {
        [Required(ErrorMessage = "Ad alanı zorunludur.")]
        public string FirstName { get; set; } = "";

        [Required(ErrorMessage = "Soyad alanı zorunludur.")]
        public string LastName { get; set; } = "";

        [Required(ErrorMessage = "T.C. kimlik numarası zorunludur.")]
        [StringLength(11, MinimumLength = 11, ErrorMessage = "T.C. kimlik numarası 11 haneli olmalıdır.")]
        public string TcKimlikNo { get; set; } = "";

        [Required(ErrorMessage = "E-posta alanı zorunludur.")]
        [EmailAddress(ErrorMessage = "Geçerli bir e-posta adresi giriniz.")]
        public string Email { get; set; } = "";

        [Required(ErrorMessage = "Şifre alanı zorunludur.")]
        [MinLength(6, ErrorMessage = "Şifre en az 6 karakter olmalıdır.")]
        [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).+$",
            ErrorMessage = "Şifre en az 1 büyük harf, 1 küçük harf ve 1 sayı içermelidir.")]
        public string Password { get; set; } = "";

        [Required(ErrorMessage = "Şifre tekrarı zorunludur.")]
        [Compare("Password", ErrorMessage = "Şifreler eşleşmiyor.")]
        public string ConfirmPassword { get; set; } = "";

        // Rol formdan alınmaz; controller tarafından her zaman "Vatandas" olarak atanır.
        public string Role { get; set; } = "Vatandas";
    }
}