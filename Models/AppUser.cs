using System.ComponentModel.DataAnnotations;

namespace EmekliRehberi.Models
{
    public class AppUser
    {
        public int Id { get; set; }

        [Required]
        public string FirstName { get; set; } = "";

        [Required]
        public string LastName { get; set; } = "";

        [Required]
        public string TcKimlikNo { get; set; } = "";

        [Required]
        public string Email { get; set; } = "";

        [Required]
        public string PasswordHash { get; set; } = "";

        [Required]
        public string Role { get; set; } = "Vatandas";
    }
}