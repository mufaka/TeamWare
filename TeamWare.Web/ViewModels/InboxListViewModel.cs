using TeamWare.Web.Models;

namespace TeamWare.Web.ViewModels;

public class InboxListViewModel
{
    public List<InboxItemViewModel> Items { get; set; } = new();
    public int UnprocessedCount { get; set; }
}
