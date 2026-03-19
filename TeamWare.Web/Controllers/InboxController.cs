using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TeamWare.Web.Services;
using TeamWare.Web.ViewModels;

namespace TeamWare.Web.Controllers;

[Authorize]
public class InboxController : Controller
{
    private readonly IInboxService _inboxService;
    private readonly IProjectService _projectService;
    private readonly ITaskService _taskService;

    public InboxController(
        IInboxService inboxService,
        IProjectService projectService,
        ITaskService taskService)
    {
        _inboxService = inboxService;
        _projectService = projectService;
        _taskService = taskService;
    }

    private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var userId = GetUserId();
        var result = await _inboxService.GetUnprocessedItems(userId);

        var viewModel = new InboxListViewModel
        {
            Items = result.Succeeded
                ? result.Data!.Select(i => new InboxItemViewModel
                {
                    Id = i.Id,
                    Title = i.Title,
                    Description = i.Description,
                    Status = i.Status,
                    CreatedAt = i.CreatedAt
                }).ToList()
                : new(),
            UnprocessedCount = result.Succeeded ? result.Data!.Count : 0
        };

        return View(viewModel);
    }

    [HttpGet]
    public IActionResult Add()
    {
        return View(new InboxAddViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(InboxAddViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var userId = GetUserId();
        var result = await _inboxService.AddItem(model.Title, model.Description, userId);

        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Errors.FirstOrDefault();
            return View(model);
        }

        TempData["SuccessMessage"] = "Item added to inbox.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> QuickAdd(string title)
    {
        var userId = GetUserId();
        var result = await _inboxService.AddItem(title, null, userId);

        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Errors.FirstOrDefault();
        }
        else
        {
            TempData["SuccessMessage"] = "Item added to inbox.";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Clarify(int id)
    {
        var userId = GetUserId();
        var itemsResult = await _inboxService.GetUnprocessedItems(userId);

        if (!itemsResult.Succeeded)
        {
            TempData["ErrorMessage"] = "Could not load inbox items.";
            return RedirectToAction(nameof(Index));
        }

        var item = itemsResult.Data!.FirstOrDefault(i => i.Id == id);
        if (item == null)
        {
            TempData["ErrorMessage"] = "Inbox item not found.";
            return RedirectToAction(nameof(Index));
        }

        var projectsResult = await _projectService.GetProjectsForUser(userId);

        var viewModel = new InboxClarifyViewModel
        {
            InboxItemId = item.Id,
            Title = item.Title,
            Description = item.Description,
            AvailableProjects = projectsResult.Succeeded
                ? projectsResult.Data!.Select(p => new ProjectOptionViewModel
                {
                    Id = p.Id,
                    Name = p.Name
                }).ToList()
                : new()
        };

        return View(viewModel);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Clarify(InboxClarifyViewModel model)
    {
        if (!ModelState.IsValid)
        {
            var userId2 = GetUserId();
            var projectsResult = await _projectService.GetProjectsForUser(userId2);
            model.AvailableProjects = projectsResult.Succeeded
                ? projectsResult.Data!.Select(p => new ProjectOptionViewModel
                {
                    Id = p.Id,
                    Name = p.Name
                }).ToList()
                : new();
            return View(model);
        }

        var userId = GetUserId();
        var result = await _inboxService.ConvertToTask(model.InboxItemId, model.ProjectId,
            model.Priority, model.DueDate, model.IsNextAction, model.IsSomedayMaybe, userId);

        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Errors.FirstOrDefault();
            var projectsResult = await _projectService.GetProjectsForUser(userId);
            model.AvailableProjects = projectsResult.Succeeded
                ? projectsResult.Data!.Select(p => new ProjectOptionViewModel
                {
                    Id = p.Id,
                    Name = p.Name
                }).ToList()
                : new();
            return View(model);
        }

        TempData["SuccessMessage"] = "Inbox item converted to task.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Dismiss(int id)
    {
        var userId = GetUserId();
        var result = await _inboxService.DismissItem(id, userId);

        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Errors.FirstOrDefault();
        }
        else
        {
            TempData["SuccessMessage"] = "Item dismissed.";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> SomedayMaybe()
    {
        var userId = GetUserId();
        var result = await _taskService.GetSomedayMaybe(userId);

        var viewModel = new SomedayMaybeViewModel
        {
            Tasks = result.Succeeded
                ? result.Data!.Select(t => new SomedayMaybeItemViewModel
                {
                    Id = t.Id,
                    Title = t.Title,
                    Description = t.Description,
                    ProjectId = t.ProjectId,
                    ProjectName = t.Project.Name,
                    Priority = t.Priority,
                    UpdatedAt = t.UpdatedAt
                }).ToList()
                : new()
        };

        return View(viewModel);
    }
}
