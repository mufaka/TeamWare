using TeamWare.Web.Services;

namespace TeamWare.Tests.Services;

public class ServiceResultTests
{
    [Fact]
    public void Success_ReturnsSucceededTrue()
    {
        var result = ServiceResult.Success();

        Assert.True(result.Succeeded);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Failure_ReturnsSucceededFalseWithErrors()
    {
        var result = ServiceResult.Failure("Error 1", "Error 2");

        Assert.False(result.Succeeded);
        Assert.Equal(2, result.Errors.Count);
        Assert.Contains("Error 1", result.Errors);
        Assert.Contains("Error 2", result.Errors);
    }

    [Fact]
    public void GenericSuccess_ReturnsDataAndSucceededTrue()
    {
        var result = ServiceResult<int>.Success(42);

        Assert.True(result.Succeeded);
        Assert.Equal(42, result.Data);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void GenericFailure_ReturnsDefaultDataAndErrors()
    {
        var result = ServiceResult<string>.Failure("Not found");

        Assert.False(result.Succeeded);
        Assert.Null(result.Data);
        Assert.Single(result.Errors);
        Assert.Equal("Not found", result.Errors[0]);
    }

    [Fact]
    public void Failure_WithEnumerable_ReturnsAllErrors()
    {
        var errors = new List<string> { "A", "B", "C" };
        var result = ServiceResult.Failure(errors);

        Assert.False(result.Succeeded);
        Assert.Equal(3, result.Errors.Count);
    }

    [Fact]
    public void GenericFailure_WithEnumerable_ReturnsAllErrors()
    {
        var errors = new List<string> { "X", "Y" };
        var result = ServiceResult<int>.Failure(errors);

        Assert.False(result.Succeeded);
        Assert.Equal(default, result.Data);
        Assert.Equal(2, result.Errors.Count);
    }
}
