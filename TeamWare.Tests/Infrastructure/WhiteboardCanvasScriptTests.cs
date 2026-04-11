using System.Text.RegularExpressions;

namespace TeamWare.Tests.Infrastructure;

public class WhiteboardCanvasScriptTests
{
    [Fact]
    public void WhiteboardCanvasScript_DefinesCanvasEngineAndShapeModel()
    {
        var script = ReadScript("whiteboard-canvas.js");

        Assert.Contains("function WhiteboardCanvas", script);
        Assert.Contains("shapes", script);
        Assert.Contains("viewport", script);
        Assert.Contains("rotation", script);
        Assert.Contains("properties", script);
    }

    [Fact]
    public void WhiteboardCanvasScript_SupportsRequiredShapeTypes()
    {
        var script = ReadScript("whiteboard-canvas.js");

        Assert.Contains("rectangle", script);
        Assert.Contains("circle", script);
        Assert.Contains("ellipse", script);
        Assert.Contains("text", script);
        Assert.Contains("line", script);
        Assert.Contains("arrow", script);
        Assert.Contains("connector", script);
        Assert.Contains("server", script);
        Assert.Contains("desktop", script);
        Assert.Contains("mobile", script);
        Assert.Contains("data", script);
        Assert.Contains("switch", script);
        Assert.Contains("router", script);
        Assert.Contains("firewall", script);
        Assert.Contains("cloud", script);
        Assert.Contains("freehand", script);
    }

    [Fact]
    public void WhiteboardCanvasScript_SupportsSelectionManipulationAndViewportOperations()
    {
        var script = ReadScript("whiteboard-canvas.js");

        Assert.Contains("selectShape", script);
        Assert.Contains("moveSelectedShape", script);
        Assert.Contains("resizeSelectedShape", script);
        Assert.Contains("deleteSelectedShape", script);
        Assert.Contains("pan", script);
        Assert.Contains("zoom", script);
    }

    [Fact]
    public void WhiteboardCanvasScript_SerializesDeserializesAndGatesPresenterInput()
    {
        var script = ReadScript("whiteboard-canvas.js");

        Assert.Contains("serialize", script);
        Assert.Contains("deserialize", script);
        Assert.Contains("setPresenterState", script);
        Assert.Contains("if (!self.isPresenter", script);
        Assert.Contains("JSON.stringify", script);
        Assert.Contains("JSON.parse", script);
    }

    private static string ReadScript(string fileName)
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "TeamWare.Web", "wwwroot", "js", fileName));
        Assert.True(File.Exists(path), $"Expected script file to exist: {path}");
        return File.ReadAllText(path);
    }
}
