namespace EmekliRehberi.Models
{
    public class PremiumDocument
    {
        public int Id { get; set; }

        public int AppUserId { get; set; }

        public AppUser? AppUser { get; set; }

        public string OriginalFileName { get; set; } = "";

        public string UploadedFileName { get; set; } = "";

        public string DocumentType { get; set; } = "SGK Tescil ve Hizmet Dökümü";

        public string Status { get; set; } = "Yüklendi";

        public int? TotalPremiumDays { get; set; }

        public string? LastPeriod { get; set; }

        public bool HasMissingDays { get; set; } = false;

        public string? ExtractedText { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public List<PremiumRecord> Records { get; set; } = new();
    }
}