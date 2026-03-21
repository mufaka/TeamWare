using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using TeamWare.Web.Models;
using TeamWare.Web.Services;
using TeamWare.Web.ViewModels;

namespace TeamWare.Web.Controllers;

[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    private readonly IAdminService _adminService;
    private readonly IAdminActivityLogService _activityLogService;
    private readonly UserManager<ApplicationUser> _userManager;

    public AdminController(
        IAdminService adminService,
        IAdminActivityLogService activityLogService,
        UserManager<ApplicationUser> userManager)
    {
        _adminService = adminService;
        _activityLogService = activityLogService;
        _userManager = userManager;
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
            userViewModels.Add(new AdminUserViewModel
            {
                Id = user.Id,
                Email = user.Email ?? string.Empty,
                DisplayName = user.DisplayName,
                IsLockedOut = user.LockoutEnd != null && user.LockoutEnd > DateTimeOffset.UtcNow,
                IsAdmin = await _userManager.IsInRoleAsync(user, "Admin")
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
}
