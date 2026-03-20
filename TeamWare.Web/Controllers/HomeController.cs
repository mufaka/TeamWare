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
        private readonly IInboxService? _inboxService;
        private readonly IProjectService? _projectService;
        private readonly IReviewService? _reviewService;
        private readonly INotificationService? _notificationService;
        private readonly IProgressService? _progressService;

        public HomeController(
            ILogger<HomeController> logger,
            ITaskService? taskService = null,
            IInboxService? inboxService = null,
            IProjectService? projectService = null,
            IReviewService? reviewService = null,
            INotificationService? notificationService = null,
            IProgressService? progressService = null)
        {
            _logger = logger;
            _taskService = taskService;
            _inboxService = inboxService;
            _projectService = projectService;
            _reviewService = reviewService;
            _notificationService = notificationService;
            _progressService = progressService;
        }

        public IActionResult Index()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return View("Dashboard");
            }

            return View();
        }

        [Authorize]
        public async Task<IActionResult> DashboardInbox()
        {
            int count = 0;

            if (_inboxService != null)
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
                var result = await _inboxService.GetUnprocessedCount(userId);
                if (result.Succeeded)
                {
                    count = result.Data;
                }
            }

            return PartialView("_DashboardInbox", count);
        }

        [Authorize]
        public async Task<IActionResult> DashboardWhatsNext()
        {
            var items = new List<WhatsNextItemViewModel>();

            if (_taskService != null)
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
                var result = await _taskService.GetWhatsNext(userId, 5);
                if (result.Succeeded)
                {
                    items = result.Data!.Select(t => new WhatsNextItemViewModel
                    {
                        Id = t.Id,
                        Title = t.Title,
                        ProjectId = t.ProjectId,
                        ProjectName = t.Project.Name,
                        Priority = t.Priority,
                        Status = t.Status,
                        DueDate = t.DueDate
                    }).ToList();
                }
            }

            return PartialView("_DashboardWhatsNext", items);
        }

        [Authorize]
        public async Task<IActionResult> DashboardProjects()
        {
            var projects = new List<DashboardProjectViewModel>();

            if (_projectService != null && _progressService != null)
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
                var result = await _projectService.GetProjectsForUser(userId);
                if (result.Succeeded)
                {
                    foreach (var project in result.Data!)
                    {
                        var stats = await _progressService.GetProjectStatistics(project.Id);
                        projects.Add(new DashboardProjectViewModel
                        {
                            Id = project.Id,
                            Name = project.Name,
                            TotalTasks = stats.TotalTasks,
                            CompletedTasks = stats.TaskCountDone
                        });
                    }
                }
            }

            return PartialView("_DashboardProjects", projects);
        }

        [Authorize]
        public async Task<IActionResult> DashboardDeadlines()
        {
            var deadlines = new List<DashboardDeadlineViewModel>();

            if (_projectService != null && _progressService != null)
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
                var projectsResult = await _projectService.GetProjectsForUser(userId);
                if (projectsResult.Succeeded)
                {
                    foreach (var project in projectsResult.Data!)
                    {
                        var upcoming = await _progressService.GetUpcomingDeadlines(project.Id, 14);
                        deadlines.AddRange(upcoming.Select(t => new DashboardDeadlineViewModel
                        {
                            TaskId = t.Id,
                            TaskTitle = t.Title,
                            ProjectName = project.Name,
                            DueDate = t.DueDate!.Value,
                            Status = t.Status
                        }));
                    }
                }
            }

            deadlines = deadlines.OrderBy(d => d.DueDate).Take(10).ToList();
            return PartialView("_DashboardDeadlines", deadlines);
        }

        [Authorize]
        public async Task<IActionResult> DashboardReview()
        {
            DateTime? lastReviewDate = null;
            bool isReviewDue = true;

            if (_reviewService != null)
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
                lastReviewDate = await _reviewService.GetLastReviewDate(userId);
                isReviewDue = await _reviewService.IsReviewDue(userId);
            }

            ViewBag.LastReviewDate = lastReviewDate;
            ViewBag.IsReviewDue = isReviewDue;
            return PartialView("_DashboardReview");
        }

        [Authorize]
        public async Task<IActionResult> DashboardNotifications()
        {
            var notifications = new List<DashboardNotificationViewModel>();

            if (_notificationService != null)
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
                var unread = await _notificationService.GetUnreadForUser(userId);
                notifications = unread.Take(5).Select(n => new DashboardNotificationViewModel
                {
                    Id = n.Id,
                    Message = n.Message,
                    Type = n.Type,
                    CreatedAt = n.CreatedAt
                }).ToList();
            }

            return PartialView("_DashboardNotifications", notifications);
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
