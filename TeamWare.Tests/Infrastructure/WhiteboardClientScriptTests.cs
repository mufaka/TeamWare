namespace TeamWare.Tests.Infrastructure;

public class WhiteboardClientScriptTests
{
    [Fact]
    public void WhiteboardClientScript_ConfiguresSignalRConnectionAndJoinFlow()
    {
        var script = ReadScript("whiteboard.js");

        Assert.Contains("/hubs/whiteboard", script);
        Assert.Contains("withAutomaticReconnect", script);
        Assert.Contains("JoinBoard", script);
        Assert.Contains("onreconnected", script);
    }

    [Fact]
    public void WhiteboardClientScript_HandlesCanvasPresenterAndPresenceEvents()
    {
        var script = ReadScript("whiteboard.js");

        Assert.Contains("CanvasUpdated", script);
        Assert.Contains("SendCanvasUpdate", script);
        Assert.Contains("ChatMessageReceived", script);
        Assert.Contains("SendChatMessage", script);
        Assert.Contains("AssignPresenter", script);
        Assert.Contains("RemoveUser", script);
        Assert.Contains("ReclaimPresenter", script);
        Assert.Contains("SaveToProject", script);
        Assert.Contains("ClearProject", script);
        Assert.Contains("saveToProject", script);
        Assert.Contains("PresenterChanged", script);
        Assert.Contains("UserJoined", script);
        Assert.Contains("UserLeft", script);
        Assert.Contains("UserRemoved", script);
        Assert.Contains("BoardDeleted", script);
    }

    [Fact]
    public void WhiteboardClientScript_UsesDebounceAndLoadsInitialCanvasState()
    {
        var script = ReadScript("whiteboard.js");

        Assert.Contains("debounce", script);
        Assert.Contains("200", script);
        Assert.Contains("whiteboard-initial-canvas", script);
        Assert.Contains("canvas.deserialize", script);
        Assert.Contains("scrollChatToBottom", script);
        Assert.Contains("whiteboard-chat-form", script);
    }

    [Fact]
    public void WhiteboardClientScript_SubmitsChatOnEnterAndPreservesShiftEnter()
    {
        var script = ReadScript("whiteboard.js");

        Assert.Contains("chatInput.addEventListener(\"keydown\"", script);
        Assert.Contains("event.key !== \"Enter\"", script);
        Assert.Contains("event.shiftKey", script);
        Assert.Contains("chatForm.requestSubmit()", script);
    }

    [Fact]
    public void WhiteboardClientScript_WiresColorPickersAndDeleteButton()
    {
        var script = ReadScript("whiteboard.js");

        Assert.Contains("whiteboard-stroke-color", script);
        Assert.Contains("whiteboard-fill-color", script);
        Assert.Contains("whiteboard-delete-button", script);
        Assert.Contains("setStrokeColor", script);
        Assert.Contains("setFillColor", script);
        Assert.Contains("deleteSelectedShape", script);
        Assert.Contains("textContent = message.content || message.Content || \"\"", script);
    }

    private static string ReadScript(string fileName)
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "TeamWare.Web", "wwwroot", "js", fileName));
        Assert.True(File.Exists(path), $"Expected script file to exist: {path}");
        return File.ReadAllText(path);
    }
}
