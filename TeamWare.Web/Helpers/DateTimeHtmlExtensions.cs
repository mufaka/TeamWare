using System.Net;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace TeamWare.Web.Helpers;

public static class DateTimeHtmlExtensions
{
    public static IHtmlContent LocalTime(this IHtmlHelper html, DateTime utcDate, string format = "datetime")
    {
        var fallback = format switch
        {
            "date" => utcDate.ToString("MMM d, yyyy"),
            "short-date" => utcDate.ToString("MMM d"),
            "short-datetime" => utcDate.ToString("MMM d, h:mm tt"),
            _ => utcDate.ToString("MMM d, yyyy h:mm tt")
        };

        var iso = utcDate.ToString("yyyy-MM-ddTHH:mm:ssZ");
        return new HtmlString(
            $"<time datetime=\"{WebUtility.HtmlEncode(iso)}\" data-format=\"{WebUtility.HtmlEncode(format)}\">{WebUtility.HtmlEncode(fallback)}</time>");
    }
}
