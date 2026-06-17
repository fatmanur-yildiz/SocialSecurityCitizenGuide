using System.ComponentModel.DataAnnotations.Schema;

namespace EmekliRehberi.Models
{
    public class PremiumRecord
    {
        public int Id { get; set; }

        public int PremiumDocumentId { get; set; }

        public PremiumDocument? PremiumDocument { get; set; }

        public string? InsuranceBranch { get; set; }

        public string? InsuranceStatus { get; set; }

        public string? Period { get; set; }

        public string? DocumentType { get; set; }

        public int Days { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? PekAmount { get; set; }

        public DateTime? EntryDate { get; set; }

        public DateTime? ExitDate { get; set; }

        public string? MissingDayReason { get; set; }

        public string? ExitReason { get; set; }

        public bool IsYearTotal { get; set; } = false;
    }
}