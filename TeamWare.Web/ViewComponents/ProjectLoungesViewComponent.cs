using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Services;

namespace TeamWare.Web.ViewComponents;

public class ProjectLoungesViewComponent : ViewComponent
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILoungeService _loungeService;

    public ProjectLoungesViewComponent(ApplicationDbContext dbContext, ILoungeService loungeService)
    {
        _dbContext = dbContext;
        _loungeService = loungeService;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        if (UserClaimsPrincipal?.Identity?.IsAuthenticated != true)
        {
            return View(ProjectLoungesViewModel.Empty);
        }

        var userId = UserClaimsPrincipal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return View(ProjectLoungesViewModel.Empty);
        }

        var projects = await _dbContext.ProjectMembers
            .Where(pm => pm.UserId == userId && pm.Project.Status == Models.ProjectStatus.Active)
            .OrderBy(pm => pm.Project.Name)
            .Select(pm => new { pm.ProjectId, pm.Project.Name })
            .ToListAsync();

        var unreadResult = await _loungeService.GetUnreadCounts(userId);
        var unreadCounts = unreadResult.Succeeded ? unreadResult.Data! : new List<RoomUnreadCount>();
        var generalUnreadCount = unreadCounts
            .Where(u => u.ProjectId == null)
            .Select(u => u.Count)
            .FirstOrDefault();

        var items = projects.Select(p => new ProjectLoungeItem
        {
            ProjectId = p.ProjectId,
            ProjectName = p.Name,
            UnreadCount = unreadCounts
                .Where(u => u.ProjectId == p.ProjectId)
                .Select(u => u.Count)
                .FirstOrDefault()
        }).ToList();

        return View(new ProjectLoungesViewModel
        {
            GeneralUnreadCount = generalUnreadCount,
            ProjectLounges = items
        });
    }
}

public class ProjectLoungesViewModel
{
    public static ProjectLoungesViewModel Empty { get; } = new();

    public int GeneralUnreadCount { get; set; }
    public List<ProjectLoungeItem> ProjectLounges { get; set; } = [];
    public int TotalUnreadCount => GeneralUnreadCount + ProjectLounges.Sum(item => item.UnreadCount);
}

public class ProjectLoungeItem
{
    public int ProjectId { get; set; }
    public string ProjectName { get; set; } = string.Empty;
    public int UnreadCount { get; set; }
}
