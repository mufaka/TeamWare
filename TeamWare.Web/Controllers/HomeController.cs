using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using TeamWare.Web.Models;

namespace TeamWare.Web.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            var requestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
            _logger.LogError("Unhandled error occurred. RequestId: {RequestId}", requestId);
            return View(new ErrorViewModel { RequestId = requestId });
        }

        [Route("/Home/StatusCode/{code:int}")]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult HttpStatusCode(int code)
        {
            _logger.LogWarning("HTTP {StatusCode} error for request {RequestId}", code, HttpContext.TraceIdentifier);
            return View("Error", new ErrorViewModel
            {
                RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            });
        }
    }
}
