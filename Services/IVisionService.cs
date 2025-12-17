using BankCheckOCR.Models;

namespace BankCheckOCR.Services
{
    public interface IVisionService
    {
        Task<CheckResult> AnalyzeCheckAsync(Stream imageStream);
    }
}
