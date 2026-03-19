using TeamWare.Web.Models;

namespace TeamWare.Web.ViewModels;

public class ProjectListViewModel
{
    public List<ProjectListItemViewModel> Projects { get; set; } = new();
}

public class ProjectListItemViewModel
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public ProjectStatus Status { get; set; }

    public int MemberCount { get; set; }

    public DateTime UpdatedAt { get; set; }
}
