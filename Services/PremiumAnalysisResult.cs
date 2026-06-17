using System.Collections.Generic;

namespace EmekliRehberi.Services
{
    public class PremiumAnalysisResult
    {
        public string ExtractedText { get; set; } = "";

        public int? TotalPremiumDays { get; set; }

        public string? LastPeriod { get; set; }

        public bool HasMissingDays { get; set; }

        public List<PremiumParsedRecord> Records { get; set; } = new();
    }
}