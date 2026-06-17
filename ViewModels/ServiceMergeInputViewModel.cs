using System.ComponentModel.DataAnnotations;

namespace EmekliRehberi.ViewModels
{
    public class ServiceMergeInputViewModel
    {
        [Display(Name = "Hedef Sigorta Statüsü")]
        public string? TargetStatus { get; set; } // "4A" | "4B" | "4C" | "Bilmiyorum" | null
    }
}