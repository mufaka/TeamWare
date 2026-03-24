using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TeamWare.Web.Models;
using TeamWare.Web.Services;
using TeamWare.Web.ViewModels;

namespace TeamWare.Web.Controllers;

[Authorize]
public class ProfileController : Controller
{
    private readonly IUserProfileService _profileService;
    private readonly IPersonalAccessTokenService _tokenService;

    public ProfileController(
        IUserProfileService profileService,
        IPersonalAccessTokenService tokenService)
    {
        _profileService = profileService;
        _tokenService = tokenService;
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

        var tokenResult = await _tokenService.GetTokensForUserAsync(GetUserId());
        ViewBag.TokenList = new PersonalAccessTokenListViewModel
        {
            Tokens = tokenResult.Succeeded ? tokenResult.Data! : []
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

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateToken(CreateTokenViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return await LoadProfileWithTokens(createForm: model);
        }

        var result = await _tokenService.CreateTokenAsync(GetUserId(), model.Name, model.ExpiresAt);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }
            return await LoadProfileWithTokens(createForm: model);
        }

        return await LoadProfileWithTokens(newlyCreatedToken: result.Data!);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RevokeToken(int tokenId)
    {
        var result = await _tokenService.RevokeTokenAsync(tokenId, GetUserId(), User.IsInRole("Admin"));
        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Errors.FirstOrDefault();
        }
        else
        {
            TempData["SuccessMessage"] = "Token revoked.";
        }

        return RedirectToAction(nameof(Index));
    }

    private async Task<IActionResult> LoadProfileWithTokens(
        CreateTokenViewModel? createForm = null,
        string? newlyCreatedToken = null)
    {
        var profileResult = await _profileService.GetProfile(GetUserId());
        if (!profileResult.Succeeded)
        {
            return RedirectToAction("Index", "Home");
        }

        var user = profileResult.Data!;
        var viewModel = new ProfileViewModel
        {
            UserId = user.Id,
            Email = user.Email ?? string.Empty,
            DisplayName = user.DisplayName,
            AvatarUrl = user.AvatarUrl,
            ThemePreference = user.ThemePreference
        };

        var tokenResult = await _tokenService.GetTokensForUserAsync(GetUserId());
        ViewBag.TokenList = new PersonalAccessTokenListViewModel
        {
            Tokens = tokenResult.Succeeded ? tokenResult.Data! : [],
            NewlyCreatedToken = newlyCreatedToken,
            CreateForm = createForm ?? new CreateTokenViewModel()
        };

        return View(nameof(Index), viewModel);
    }
}
