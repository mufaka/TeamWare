namespace TeamWare.Web.ViewModels;

public class DashboardProjectViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int TotalTasks { get; set; }
    public int CompletedTasks { get; set; }
}
