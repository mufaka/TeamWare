using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;
using TeamWare.Web.Services;
using TeamWare.Web.ViewModels;

namespace TeamWare.Web.Controllers;

[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    private readonly IAdminService _adminService;
    private readonly IAdminActivityLogService _activityLogService;
    private readonly IGlobalConfigurationService _configService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IPersonalAccessTokenService _patService;
    private readonly ApplicationDbContext _context;

    public AdminController(
        IAdminService adminService,
        IAdminActivityLogService activityLogService,
        IGlobalConfigurationService configService,
        UserManager<ApplicationUser> userManager,
        IPersonalAccessTokenService patService,
        ApplicationDbContext context)
    {
        _adminService = adminService;
        _activityLogService = activityLogService;
        _configService = configService;
        _userManager = userManager;
        _patService = patService;
        _context = context;
    }

    private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet]
    public async Task<IActionResult> Dashboard()
    {
        var result = await _adminService.GetSystemStatistics();
        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = "Unable to load dashboard statistics.";
            return RedirectToAction("Index", "Home");
        }

        var viewModel = new AdminDashboardViewModel
        {
            TotalUsers = result.Data!.TotalUsers,
            TotalProjects = result.Data.TotalProjects,
            TotalTasks = result.Data.TotalTasks
        };

        return View(viewModel);
    }

    [HttpGet]
    public async Task<IActionResult> Users(string? search, int page = 1)
    {
        var result = await _adminService.GetAllUsers(search, page, 20);
        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Errors.FirstOrDefault();
            return RedirectToAction(nameof(Dashboard));
        }

        var pagedResult = result.Data!;
        var userViewModels = new List<AdminUserViewModel>();

        foreach (var user in pagedResult.Items)
        {
            var tokensResult = await _patService.GetTokensForUserAsync(user.Id);
            var activePatCount = tokensResult.Succeeded ? tokensResult.Data!.Count : 0;

            userViewModels.Add(new AdminUserViewModel
            {
                Id = user.Id,
                Email = user.Email ?? string.Empty,
                DisplayName = user.DisplayName,
                IsLockedOut = user.LockoutEnd != null && user.LockoutEnd > DateTimeOffset.UtcNow,
                IsAdmin = await _userManager.IsInRoleAsync(user, "Admin"),
                ActivePatCount = activePatCount
            });
        }

        var viewModel = new AdminUserListViewModel
        {
            Users = userViewModels,
            SearchTerm = search,
            Page = pagedResult.Page,
            TotalPages = pagedResult.TotalPages,
            TotalCount = pagedResult.TotalCount
        };

        return View(viewModel);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LockUser(string id)
    {
        var result = await _adminService.LockUser(id, GetUserId());
        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Errors.FirstOrDefault();
        }
        else
        {
            TempData["SuccessMessage"] = "User account has been locked.";
        }

        return RedirectToAction(nameof(Users));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UnlockUser(string id)
    {
        var result = await _adminService.UnlockUser(id, GetUserId());
        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Errors.FirstOrDefault();
        }
        else
        {
            TempData["SuccessMessage"] = "User account has been unlocked.";
        }

        return RedirectToAction(nameof(Users));
    }

    [HttpGet]
    public IActionResult ResetPassword(string id, string email)
    {
        var viewModel = new AdminResetPasswordViewModel
        {
            Email = email
        };

        ViewData["UserId"] = id;
        return View(viewModel);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(string id, AdminResetPasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            ViewData["UserId"] = id;
            return View(model);
        }

        var user = await _userManager.FindByIdAsync(id);
        if (user == null)
        {
            TempData["ErrorMessage"] = "User not found.";
            return RedirectToAction(nameof(Users));
        }

        var result = await _adminService.ResetPassword(id, model.NewPassword, GetUserId());
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }
            ViewData["UserId"] = id;
            return View(model);
        }

        TempData["SuccessMessage"] = $"Password has been reset for {user.Email}.";
        return RedirectToAction(nameof(Users));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PromoteToAdmin(string id)
    {
        var result = await _adminService.PromoteToAdmin(id, GetUserId());
        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Errors.FirstOrDefault();
        }
        else
        {
            TempData["SuccessMessage"] = "User has been promoted to Admin.";
        }

        return RedirectToAction(nameof(Users));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DemoteToUser(string id)
    {
        var result = await _adminService.DemoteToUser(id, GetUserId());
        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Errors.FirstOrDefault();
        }
        else
        {
            TempData["SuccessMessage"] = "User has been demoted to User role.";
        }

        return RedirectToAction(nameof(Users));
    }

    [HttpGet]
    public async Task<IActionResult> ActivityLog(int page = 1)
    {
        var result = await _activityLogService.GetActivityLog(page, 20);
        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Errors.FirstOrDefault();
            return RedirectToAction(nameof(Dashboard));
        }

        var pagedResult = result.Data!;
        var viewModel = new AdminActivityLogViewModel
        {
            Entries = pagedResult.Items.Select(e => new AdminActivityLogEntryViewModel
            {
                Id = e.Id,
                AdminDisplayName = e.AdminUser?.DisplayName ?? "Unknown",
                Action = e.Action,
                TargetUserDisplayName = e.TargetUser?.DisplayName,
                TargetProjectName = e.TargetProject?.Name,
                Details = e.Details,
                CreatedAt = e.CreatedAt
            }).ToList(),
            Page = pagedResult.Page,
            TotalPages = pagedResult.TotalPages,
            TotalCount = pagedResult.TotalCount
        };

        return View(viewModel);
    }

    [HttpGet]
    public async Task<IActionResult> Configuration()
    {
        var result = await _configService.GetAllAsync();
        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Errors.FirstOrDefault();
            return RedirectToAction(nameof(Dashboard));
        }

        var viewModel = new GlobalConfigurationListViewModel
        {
            Items = result.Data!.Select(gc => new GlobalConfigurationItemViewModel
            {
                Key = gc.Key,
                Value = gc.Value,
                Description = gc.Description,
                UpdatedAt = gc.UpdatedAt,
                UpdatedByDisplayName = gc.UpdatedByUser?.DisplayName
            }).ToList()
        };

        return View(viewModel);
    }

    [HttpGet]
    public async Task<IActionResult> EditConfiguration(string key)
    {
        var result = await _configService.GetByKeyAsync(key);
        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Errors.FirstOrDefault();
            return RedirectToAction(nameof(Configuration));
        }

        var config = result.Data!;
        var viewModel = new GlobalConfigurationEditViewModel
        {
            Key = config.Key,
            Description = config.Description,
            Value = config.Value
        };

        return View(viewModel);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditConfiguration(GlobalConfigurationEditViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _configService.UpdateAsync(model.Key, model.Value, GetUserId());
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }
            return View(model);
        }

        TempData["SuccessMessage"] = $"Configuration '{model.Key}' has been updated.";
        return RedirectToAction(nameof(Configuration));
    }

    [HttpGet]
    public async Task<IActionResult> UserTokens(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null)
        {
            TempData["ErrorMessage"] = "User not found.";
            return RedirectToAction(nameof(Users));
        }

        var tokensResult = await _patService.GetTokensForUserAsync(user.Id);
        var viewModel = new AdminUserTokensViewModel
        {
            UserId = user.Id,
            UserDisplayName = user.DisplayName,
            UserEmail = user.Email ?? string.Empty,
            Tokens = tokensResult.Succeeded ? tokensResult.Data! : new List<PersonalAccessToken>()
        };

        return View(viewModel);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RevokeUserToken(string userId, int tokenId)
    {
        var result = await _patService.RevokeTokenAsync(tokenId, GetUserId(), isAdmin: true);
        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Errors.FirstOrDefault();
        }
        else
        {
            TempData["SuccessMessage"] = "Token has been revoked.";
        }

        return RedirectToAction(nameof(UserTokens), new { id = userId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RevokeAllUserTokens(string userId)
    {
        var result = await _patService.RevokeAllTokensForUserAsync(userId);
        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Errors.FirstOrDefault();
        }
        else
        {
            TempData["SuccessMessage"] = "All tokens have been revoked.";
        }

        return RedirectToAction(nameof(UserTokens), new { id = userId });
    }

    // --- Agent Management ---

    [HttpGet]
    public async Task<IActionResult> Agents()
    {
        var result = await _adminService.GetAgentUsers();
        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Errors.FirstOrDefault();
            return RedirectToAction(nameof(Dashboard));
        }

        return View(result.Data!);
    }

    [HttpGet]
    public IActionResult CreateAgent()
    {
        return View(new CreateAgentViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateAgent(CreateAgentViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _adminService.CreateAgentUser(model.DisplayName, model.Description, GetUserId());
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }
            return View(model);
        }

        var (user, rawToken) = result.Data!;
        var viewModel = new AgentCreatedViewModel
        {
            DisplayName = user.DisplayName,
            UserId = user.Id,
            RawToken = rawToken
        };

        return View("AgentCreated", viewModel);
    }

    [HttpGet]
    public async Task<IActionResult> AgentDetail(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null || !user.IsAgent)
        {
            TempData["ErrorMessage"] = "Agent not found.";
            return RedirectToAction(nameof(Agents));
        }

        var tokensResult = await _patService.GetTokensForUserAsync(user.Id);

        var projectMemberships = await _context.ProjectMembers
            .Include(pm => pm.Project)
            .Where(pm => pm.UserId == user.Id)
            .Select(pm => new AgentProjectMembership
            {
                ProjectId = pm.ProjectId,
                ProjectName = pm.Project.Name,
                Role = pm.Role.ToString()
            })
            .ToListAsync();

        var recentLogs = await _context.AdminActivityLogs
            .Include(l => l.AdminUser)
            .Where(l => l.TargetUserId == user.Id)
            .OrderByDescending(l => l.CreatedAt)
            .Take(10)
            .Select(l => new AdminActivityLogEntryViewModel
            {
                Id = l.Id,
                AdminDisplayName = l.AdminUser != null ? l.AdminUser.DisplayName : "Unknown",
                Action = l.Action,
                Details = l.Details,
                CreatedAt = l.CreatedAt
            })
            .ToListAsync();

        var viewModel = new AgentDetailViewModel
        {
            UserId = user.Id,
            DisplayName = user.DisplayName,
            AgentDescription = user.AgentDescription,
            IsAgentActive = user.IsAgentActive,
            LastActiveAt = user.LastActiveAt,
            Tokens = tokensResult.Succeeded ? tokensResult.Data! : new List<PersonalAccessToken>(),
            ProjectMemberships = projectMemberships,
            RecentActivity = recentLogs
        };

        return View(viewModel);
    }

    [HttpGet]
    public async Task<IActionResult> EditAgent(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null || !user.IsAgent)
        {
            TempData["ErrorMessage"] = "Agent not found.";
            return RedirectToAction(nameof(Agents));
        }

        var viewModel = new EditAgentViewModel
        {
            UserId = user.Id,
            DisplayName = user.DisplayName,
            Description = user.AgentDescription,
            IsActive = user.IsAgentActive
        };

        return View(viewModel);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditAgent(EditAgentViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var updateResult = await _adminService.UpdateAgentUser(model.UserId, model.DisplayName, model.Description, GetUserId());
        if (!updateResult.Succeeded)
        {
            foreach (var error in updateResult.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }
            return View(model);
        }

        var user = await _userManager.FindByIdAsync(model.UserId);
        if (user != null && user.IsAgentActive != model.IsActive)
        {
            await _adminService.SetAgentActive(model.UserId, model.IsActive, GetUserId());
        }

        TempData["SuccessMessage"] = "Agent has been updated.";
        return RedirectToAction(nameof(AgentDetail), new { id = model.UserId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleAgentActive(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null || !user.IsAgent)
        {
            TempData["ErrorMessage"] = "Agent not found.";
            return RedirectToAction(nameof(Agents));
        }

        var result = await _adminService.SetAgentActive(id, !user.IsAgentActive, GetUserId());
        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Errors.FirstOrDefault();
        }
        else
        {
            TempData["SuccessMessage"] = user.IsAgentActive ? "Agent has been paused." : "Agent has been resumed.";
        }

        return RedirectToAction(nameof(Agents));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAgent(string id)
    {
        var result = await _adminService.DeleteAgentUser(id, GetUserId());
        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Errors.FirstOrDefault();
        }
        else
        {
            TempData["SuccessMessage"] = "Agent has been deleted.";
        }

        return RedirectToAction(nameof(Agents));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GenerateAgentToken(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null || !user.IsAgent)
        {
            TempData["ErrorMessage"] = "Agent not found.";
            return RedirectToAction(nameof(Agents));
        }

        var result = await _patService.CreateTokenAsync(user.Id, "Agent Token", null);
        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Errors.FirstOrDefault();
            return RedirectToAction(nameof(AgentDetail), new { id });
        }

        TempData["NewToken"] = result.Data;
        TempData["SuccessMessage"] = "New token generated. Copy it now — it will not be shown again.";
        return RedirectToAction(nameof(AgentDetail), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RevokeAgentToken(string userId, int tokenId)
    {
        var result = await _patService.RevokeTokenAsync(tokenId, GetUserId(), isAdmin: true);
        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Errors.FirstOrDefault();
        }
        else
        {
            TempData["SuccessMessage"] = "Token has been revoked.";
        }

        return RedirectToAction(nameof(AgentDetail), new { id = userId });
    }
}
