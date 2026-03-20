using System.ComponentModel.DataAnnotations;

namespace TeamWare.Web.ViewModels;

public class CompleteReviewViewModel
{
    [StringLength(2000)]
    public string? Notes { get; set; }
}
