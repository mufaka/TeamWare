using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Security.Claims;
using TeamWare.Web.Models;
using TeamWare.Web.Services;
using TeamWare.Web.ViewModels;

namespace TeamWare.Web.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly ITaskService? _taskService;

        public HomeController(ILogger<HomeController> logger, ITaskService? taskService = null)
        {
            _logger = logger;
            _taskService = taskService;
        }

        public IActionResult Index()
        {
            return View();
        }

        [Authorize]
        public async Task<IActionResult> WhatsNext()
        {
            if (_taskService == null)
            {
                return View(new WhatsNextViewModel());
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var result = await _taskService.GetWhatsNext(userId);

            var viewModel = new WhatsNextViewModel
            {
                Tasks = result.Succeeded
                    ? result.Data!.Select(t => new WhatsNextItemViewModel
                    {
                        Id = t.Id,
                        Title = t.Title,
                        ProjectId = t.ProjectId,
                        ProjectName = t.Project.Name,
                        Priority = t.Priority,
                        Status = t.Status,
                        DueDate = t.DueDate
                    }).ToList()
                    : new()
            };

            return View(viewModel);
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
