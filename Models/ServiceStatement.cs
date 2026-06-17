namespace EmekliRehberi.Models
{
    public class ServiceStatement
    {
        public int Id { get; set; }

        public int AppUserId { get; set; }

        public AppUser? AppUser { get; set; }

        public string? UploadedFileName { get; set; }

        public string? OriginalFileName { get; set; }

        public string Status { get; set; } = "Yüklendi";

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}