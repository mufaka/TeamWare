using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TeamWare.Web.Services;
using TeamWare.Web.ViewModels;

namespace TeamWare.Web.Controllers;

[Authorize]
public class DirectoryController : Controller
{
    private readonly IUserDirectoryService _directoryService;

    public DirectoryController(IUserDirectoryService directoryService)
    {
        _directoryService = directoryService;
    }

    public async Task<IActionResult> Index(string? search, string sortBy = "displayname", bool ascending = true, int page = 1)
    {
        ServiceResult<PagedResult<UserDirectoryEntryViewModel>> result;

        if (!string.IsNullOrWhiteSpace(search))
        {
            result = await _directoryService.SearchUsers(search, page);
        }
        else
        {
            result = await _directoryService.GetUsersSorted(sortBy, ascending, page);
        }

        if (!result.Succeeded)
        {
            return View(new DirectoryListViewModel());
        }

        var viewModel = new DirectoryListViewModel
        {
            Users = result.Data!.Items.ToList(),
            SearchTerm = search,
            SortBy = sortBy,
            Ascending = ascending,
            Page = result.Data.Page,
            TotalPages = result.Data.TotalPages,
            TotalCount = result.Data.TotalCount
        };

        return View(viewModel);
    }

    public async Task<IActionResult> Profile(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return NotFound();
        }

        var result = await _directoryService.GetUserProfile(id);
        if (!result.Succeeded)
        {
            return NotFound();
        }

        return View(result.Data);
    }
}
