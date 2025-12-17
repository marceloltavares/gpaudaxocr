namespace BankCheckOCR.Models
{
    public class CheckResult
    {
        public string? BankCode { get; set; }
        public string? Amount { get; set; }
        public string? Date { get; set; }
        public string? CMC7 { get; set; }
        public string? RawText { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }

        public string?  IssuerDocument { get; set; }
        public string? ImageBase64 { get; set; }
    }
}
