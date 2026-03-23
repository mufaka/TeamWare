using System.ComponentModel.DataAnnotations;

namespace TeamWare.Web.ViewModels;

public class GlobalConfigurationEditViewModel
{
    public string Key { get; set; } = string.Empty;

    public string? Description { get; set; }

    [Required]
    [StringLength(2000)]
    public string Value { get; set; } = string.Empty;
}
