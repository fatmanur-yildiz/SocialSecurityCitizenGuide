using System;
using System.ComponentModel.DataAnnotations;

namespace EmekliRehberi.Models
{
    public class SocialSecurityRecordDocument
    {
        public int Id { get; set; }

        public int AppUserId { get; set; }
        public AppUser? AppUser { get; set; }

        public string OriginalFileName { get; set; } = "";
        public string UploadedFileName { get; set; } = "";
        public string Status { get; set; } = "Yüklendi";
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // 4A / 4B / 4C gün sayıları
        public int? Days4A { get; set; }
        public int? Days4B { get; set; }
        public int? Days4C { get; set; }
        public int? DaysGM20 { get; set; }
        public int? TotalDays { get; set; }

        // Tescil bilgileri
        public bool Has4A { get; set; } = false;
        public bool Has4B { get; set; } = false;
        public bool Has4C { get; set; } = false;

        public DateTime? FirstRegistrationDate { get; set; }
        public DateTime? LastRegistrationDate { get; set; }
        public bool IsActive { get; set; } = false;

        // Ham metin (debug/kontrol)
        public string? ExtractedText { get; set; }
    }
}
