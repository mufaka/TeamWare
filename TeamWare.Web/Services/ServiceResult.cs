namespace TeamWare.Web.Services;

public class ServiceResult
{
    public bool Succeeded { get; }
    public IReadOnlyList<string> Errors { get; }

    protected ServiceResult(bool succeeded, IEnumerable<string> errors)
    {
        Succeeded = succeeded;
        Errors = errors.ToList().AsReadOnly();
    }

    public static ServiceResult Success() => new(true, []);

    public static ServiceResult Failure(params string[] errors) => new(false, errors);

    public static ServiceResult Failure(IEnumerable<string> errors) => new(false, errors);
}

public class ServiceResult<T> : ServiceResult
{
    public T? Data { get; }

    private ServiceResult(bool succeeded, T? data, IEnumerable<string> errors)
        : base(succeeded, errors)
    {
        Data = data;
    }

    public static ServiceResult<T> Success(T data) => new(true, data, []);

    public static new ServiceResult<T> Failure(params string[] errors) => new(false, default, errors);

    public static new ServiceResult<T> Failure(IEnumerable<string> errors) => new(false, default, errors);
}
