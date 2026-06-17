using System;
using System.Collections.Generic;

namespace EmekliRehberi.Services
{
    public enum DataSufficiencyLevel
    {
        Eksik,
        Sinirli,
        Yeterli
    }

    public enum ServiceMergeCommentType
    {
        Info,
        Warning,
        Success
    }

    public class ServiceMergeComment
    {
        public string Title { get; set; } = "";
        public string Text { get; set; } = "";
        public ServiceMergeCommentType Type { get; set; } = ServiceMergeCommentType.Info;
    }

    public class ServiceMergeAnalysisItem
    {
        public string Question { get; set; } = "";
        public string Answer { get; set; } = "";
        public ServiceMergeCommentType Type { get; set; } = ServiceMergeCommentType.Info;
    }

    public class ServiceMergeResult
    {
        public bool HasPremiumDocument { get; set; }
        public bool HasSocialSecurityDocument { get; set; }

        public DataSufficiencyLevel DataSufficiency { get; set; } = DataSufficiencyLevel.Eksik;
        public string DataSufficiencyLabel { get; set; } = "";

        public int? TotalPremiumDays { get; set; }
        public int? TotalDaysFromSocialSecurity { get; set; }
        public int EffectiveTotalDays { get; set; }

        public int? Days4A { get; set; }
        public int? Days4B { get; set; }
        public int? Days4C { get; set; }
        public int? DaysGM20 { get; set; }

        public List<string> ActiveBranches { get; set; } = new();
        public bool IsMixedService { get; set; }
        public string? DominantBranch { get; set; }
        public double? DominantBranchSharePercent { get; set; }

        public string? TargetStatus { get; set; }
        public bool? TargetMatchesDominant { get; set; }

        public DateTime? FirstInsuranceDateUsed { get; set; }
        public bool? IsPost2008Insured { get; set; }

        public string YourSituationSummary { get; set; } = "";

        public List<ServiceMergeAnalysisItem> AnalysisItems { get; set; } = new();
        public List<ServiceMergeComment> AdditionalNotes { get; set; } = new();
        public List<string> MissingDataNotices { get; set; } = new();
    }
}