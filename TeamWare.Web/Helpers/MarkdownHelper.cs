using Markdig;
using Microsoft.AspNetCore.Html;

namespace TeamWare.Web.Helpers;

public static class MarkdownHelper
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UseAutoLinks()
        .UseGenericAttributes()
        .Build();

    public static HtmlString RenderMarkdown(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return HtmlString.Empty;

        var html = Markdown.ToHtml(markdown, Pipeline);
        return new HtmlString(html);
    }
}
