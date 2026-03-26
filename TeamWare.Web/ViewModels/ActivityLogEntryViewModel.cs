using TeamWare.Web.Models;

namespace TeamWare.Web.ViewModels;

public class ActivityLogEntryViewModel
{
    public int Id { get; set; }

    public int TaskItemId { get; set; }

    public string TaskTitle { get; set; } = string.Empty;

    public string UserDisplayName { get; set; } = string.Empty;

    public bool IsUserAgent { get; set; }

    public ActivityChangeType ChangeType { get; set; }

    public string? OldValue { get; set; }

    public string? NewValue { get; set; }

    public DateTime CreatedAt { get; set; }

    public string Description
    {
        get
        {
            return ChangeType switch
            {
                ActivityChangeType.Created => $"{UserDisplayName} created this task",
                ActivityChangeType.StatusChanged => $"{UserDisplayName} changed status from {FormatValue(OldValue)} to {FormatValue(NewValue)}",
                ActivityChangeType.PriorityChanged => $"{UserDisplayName} changed priority from {FormatValue(OldValue)} to {FormatValue(NewValue)}",
                ActivityChangeType.Assigned => $"{UserDisplayName} assigned {FormatValue(NewValue)}",
                ActivityChangeType.Unassigned => $"{UserDisplayName} unassigned {FormatValue(OldValue)}",
                ActivityChangeType.MarkedNextAction => $"{UserDisplayName} marked as Next Action",
                ActivityChangeType.ClearedNextAction => $"{UserDisplayName} removed from Next Actions",
                ActivityChangeType.MarkedSomedayMaybe => $"{UserDisplayName} marked as Someday/Maybe",
                ActivityChangeType.ClearedSomedayMaybe => $"{UserDisplayName} removed from Someday/Maybe",
                ActivityChangeType.Updated => $"{UserDisplayName} updated this task",
                ActivityChangeType.Deleted => $"{UserDisplayName} deleted this task",
                _ => $"{UserDisplayName} made a change"
            };
        }
    }

    private static string FormatValue(string? value) => value ?? "none";
}
