using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TeamWare.Web.Services;
using TeamWare.Web.ViewModels;

namespace TeamWare.Web.Controllers;

[Authorize]
public class ProfileController : Controller
{
    private readonly IUserProfileService _profileService;

    public ProfileController(IUserProfileService profileService)
    {
        _profileService = profileService;
    }

    private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var result = await _profileService.GetProfile(GetUserId());
        if (!result.Succeeded)
        {
            return RedirectToAction("Index", "Home");
        }

        var user = result.Data!;
        var viewModel = new ProfileViewModel
        {
            UserId = user.Id,
            Email = user.Email ?? string.Empty,
            DisplayName = user.DisplayName,
            AvatarUrl = user.AvatarUrl,
            ThemePreference = user.ThemePreference
        };

        return View(viewModel);
    }

    [HttpGet]
    public async Task<IActionResult> Edit()
    {
        var result = await _profileService.GetProfile(GetUserId());
        if (!result.Succeeded)
        {
            return RedirectToAction(nameof(Index));
        }

        var user = result.Data!;
        var viewModel = new EditProfileViewModel
        {
            DisplayName = user.DisplayName,
            AvatarUrl = user.AvatarUrl
        };

        return View(viewModel);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(EditProfileViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _profileService.UpdateProfile(GetUserId(), model.DisplayName, model.AvatarUrl);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }
            return View(model);
        }

        TempData["SuccessMessage"] = "Profile updated successfully.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public IActionResult ChangePassword()
    {
        return View(new ChangePasswordViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _profileService.ChangePassword(GetUserId(), model.CurrentPassword, model.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }
            return View(model);
        }

        TempData["SuccessMessage"] = "Password changed successfully.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateTheme(string theme)
    {
        var result = await _profileService.UpdateThemePreference(GetUserId(), theme);

        if (Request.Headers["HX-Request"] == "true")
        {
            return result.Succeeded
                ? Content("Theme updated.")
                : Content(result.Errors.FirstOrDefault() ?? "Error updating theme.");
        }

        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Errors.FirstOrDefault();
        }
        else
        {
            TempData["SuccessMessage"] = "Theme updated.";
        }

        return RedirectToAction(nameof(Index));
    }
}
