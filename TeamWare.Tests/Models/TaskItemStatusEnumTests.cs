using TeamWare.Web.Models;

namespace TeamWare.Tests.Models;

public class TaskItemStatusEnumTests
{
    [Fact]
    public void TaskItemStatus_ToDo_HasValue0()
    {
        Assert.Equal(0, (int)TaskItemStatus.ToDo);
    }

    [Fact]
    public void TaskItemStatus_InProgress_HasValue1()
    {
        Assert.Equal(1, (int)TaskItemStatus.InProgress);
    }

    [Fact]
    public void TaskItemStatus_InReview_HasValue2()
    {
        Assert.Equal(2, (int)TaskItemStatus.InReview);
    }

    [Fact]
    public void TaskItemStatus_Done_HasValue3()
    {
        Assert.Equal(3, (int)TaskItemStatus.Done);
    }

    [Fact]
    public void TaskItemStatus_Blocked_HasValue4()
    {
        Assert.Equal(4, (int)TaskItemStatus.Blocked);
    }

    [Fact]
    public void TaskItemStatus_Error_HasValue5()
    {
        Assert.Equal(5, (int)TaskItemStatus.Error);
    }

    [Fact]
    public void TaskItemStatus_HasSixValues()
    {
        var values = Enum.GetValues<TaskItemStatus>();
        Assert.Equal(6, values.Length);
    }

    [Theory]
    [InlineData("Blocked", TaskItemStatus.Blocked)]
    [InlineData("blocked", TaskItemStatus.Blocked)]
    [InlineData("BLOCKED", TaskItemStatus.Blocked)]
    [InlineData("Error", TaskItemStatus.Error)]
    [InlineData("error", TaskItemStatus.Error)]
    [InlineData("ERROR", TaskItemStatus.Error)]
    public void TaskItemStatus_ParsesNewStatusValues(string input, TaskItemStatus expected)
    {
        Assert.True(Enum.TryParse<TaskItemStatus>(input, ignoreCase: true, out var result));
        Assert.Equal(expected, result);
    }
}
