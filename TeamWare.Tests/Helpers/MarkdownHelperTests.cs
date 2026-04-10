using TeamWare.Web.Helpers;

namespace TeamWare.Tests.Helpers;

public class MarkdownHelperTests
{
    [Fact]
    public void RenderMarkdown_NullInput_ReturnsEmpty()
    {
        var result = MarkdownHelper.RenderMarkdown(null);

        Assert.NotNull(result);
        Assert.Equal(string.Empty, result.ToString());
    }

    [Fact]
    public void RenderMarkdown_EmptyInput_ReturnsEmpty()
    {
        var result = MarkdownHelper.RenderMarkdown("");

        Assert.Equal(string.Empty, result.ToString());
    }

    [Fact]
    public void RenderMarkdown_WhitespaceInput_ReturnsEmpty()
    {
        var result = MarkdownHelper.RenderMarkdown("   ");

        Assert.Equal(string.Empty, result.ToString());
    }

    [Fact]
    public void RenderMarkdown_PlainText_WrapsInParagraph()
    {
        var result = MarkdownHelper.RenderMarkdown("Hello world");

        Assert.Contains("<p>Hello world</p>", result.ToString());
    }

    [Fact]
    public void RenderMarkdown_Bold_RendersStrongTag()
    {
        var result = MarkdownHelper.RenderMarkdown("This is **bold** text");

        Assert.Contains("<strong>bold</strong>", result.ToString());
    }

    [Fact]
    public void RenderMarkdown_Italic_RendersEmTag()
    {
        var result = MarkdownHelper.RenderMarkdown("This is _italic_ text");

        Assert.Contains("<em>italic</em>", result.ToString());
    }

    [Fact]
    public void RenderMarkdown_Heading_RendersHeadingTag()
    {
        var result = MarkdownHelper.RenderMarkdown("## My Heading");

        Assert.Contains("<h2>My Heading</h2>", result.ToString());
    }

    [Fact]
    public void RenderMarkdown_BulletList_RendersUlTag()
    {
        var result = MarkdownHelper.RenderMarkdown("- Item 1\n- Item 2");
        var html = result.ToString();

        Assert.Contains("<ul>", html);
        Assert.Contains("<li>Item 1</li>", html);
        Assert.Contains("<li>Item 2</li>", html);
    }

    [Fact]
    public void RenderMarkdown_InlineCode_RendersCodeTag()
    {
        var result = MarkdownHelper.RenderMarkdown("Use `var x = 1;` here");

        Assert.Contains("<code>var x = 1;</code>", result.ToString());
    }

    [Fact]
    public void RenderMarkdown_Link_RendersAutoLink()
    {
        var result = MarkdownHelper.RenderMarkdown("Visit https://example.com");

        Assert.Contains("<a href=\"https://example.com\">https://example.com</a>", result.ToString());
    }

    [Fact]
    public void RenderMarkdown_SoftLineBreak_RendersBreakTag()
    {
        var result = MarkdownHelper.RenderMarkdown("First line\nSecond line");

        Assert.Contains("First line<br", result.ToString());
        Assert.Contains("Second line", result.ToString());
    }

    [Fact]
    public void RenderMarkdown_RawHtml_IsEscaped()
    {
        var result = MarkdownHelper.RenderMarkdown("<script>alert('xss')</script>");
        var html = result.ToString();

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void RenderMarkdown_MarkdownLink_RendersAnchorTag()
    {
        var result = MarkdownHelper.RenderMarkdown("[click here](https://example.com)");

        Assert.Contains("<a href=\"https://example.com\">click here</a>", result.ToString());
    }
}
