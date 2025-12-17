using BankCheckOCR.Models;
using BankCheckOCR.Services;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace BankCheckOCR.Controllers
{
    public class HomeController : Controller
    {
        private readonly IVisionService _visionService;
        private readonly ILogger<HomeController> _logger;

        public HomeController(IVisionService visionService, ILogger<HomeController> logger)
        {
            _visionService = visionService;
            _logger = logger;
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Analyze(IFormFile checkImage)
        {
            if (checkImage == null || checkImage.Length == 0)
            {
                ViewBag.Error = "Please select an image.";
                return View("Index");
            }

            try
            {
                using (var stream = checkImage.OpenReadStream())
                {
                    var result = await _visionService.AnalyzeCheckAsync(stream);
                    return View("Result", result);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing request.");
                ViewBag.Error = "An error occurred while processing the image.";
                return View("Index");
            }
        }
    }
}
