using System;

public class PremiumParsedRecord
{
    public string Period { get; set; } = "";
    public int Days { get; set; }
    public decimal? PekAmount { get; set; }
    public bool IsYearTotal { get; set; } = false;
    public DateTime? EntryDate { get; set; }
    public DateTime? ExitDate { get; set; }
}
