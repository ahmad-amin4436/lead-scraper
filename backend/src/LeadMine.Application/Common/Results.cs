namespace LeadMine.Application.Common;

/// <summary>
/// Outcome of an operation that can fail for expected reasons.
/// <para>
/// Used instead of exceptions for domain failures (bad credentials, duplicate
/// email) so the happy path stays exception-free and controllers can map a
/// failure to the right status code without catching.
/// </para>
/// </summary>
public class Result
{
    protected Result(bool succeeded, string? error, IReadOnlyList<string>? errors, ResultErrorType errorType)
    {
        Succeeded = succeeded;
        Error = error;
        Errors = errors ?? Array.Empty<string>();
        ErrorType = errorType;
    }

    public bool Succeeded { get; }

    public string? Error { get; }

    public IReadOnlyList<string> Errors { get; }

    public ResultErrorType ErrorType { get; }

    public static Result Success() => new(true, null, null, ResultErrorType.None);

    public static Result Failure(string error, ResultErrorType type = ResultErrorType.Validation) =>
        new(false, error, new[] { error }, type);

    public static Result Failure(IReadOnlyList<string> errors, ResultErrorType type = ResultErrorType.Validation) =>
        new(false, errors.FirstOrDefault(), errors, type);

    public static Result NotFound(string error = "Resource not found") =>
        new(false, error, new[] { error }, ResultErrorType.NotFound);

    public static Result Forbidden(string error = "You do not have permission to do this") =>
        new(false, error, new[] { error }, ResultErrorType.Forbidden);

    public static Result Conflict(string error) =>
        new(false, error, new[] { error }, ResultErrorType.Conflict);

    public static Result Unauthorized(string error = "Authentication required") =>
        new(false, error, new[] { error }, ResultErrorType.Unauthorized);
}

public sealed class Result<T> : Result
{
    private Result(bool succeeded, T? value, string? error, IReadOnlyList<string>? errors, ResultErrorType errorType)
        : base(succeeded, error, errors, errorType)
    {
        Value = value;
    }

    public T? Value { get; }

    public static Result<T> Success(T value) => new(true, value, null, null, ResultErrorType.None);

    public static new Result<T> Failure(string error, ResultErrorType type = ResultErrorType.Validation) =>
        new(false, default, error, new[] { error }, type);

    public static new Result<T> Failure(IReadOnlyList<string> errors, ResultErrorType type = ResultErrorType.Validation) =>
        new(false, default, errors.FirstOrDefault(), errors, type);

    public static new Result<T> NotFound(string error = "Resource not found") =>
        new(false, default, error, new[] { error }, ResultErrorType.NotFound);

    public static new Result<T> Forbidden(string error = "You do not have permission to do this") =>
        new(false, default, error, new[] { error }, ResultErrorType.Forbidden);

    public static new Result<T> Conflict(string error) =>
        new(false, default, error, new[] { error }, ResultErrorType.Conflict);

    public static new Result<T> Unauthorized(string error = "Authentication required") =>
        new(false, default, error, new[] { error }, ResultErrorType.Unauthorized);
}

public enum ResultErrorType
{
    None = 0,
    Validation = 1,
    NotFound = 2,
    Conflict = 3,
    Forbidden = 4,
    Unauthorized = 5,
}

/// <summary>One page of results plus the totals a client needs to paginate.</summary>
public sealed class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();

    public int Total { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 25;

    public int PageCount => PageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));

    public static PagedResult<T> Create(IReadOnlyList<T> items, int total, int page, int pageSize) =>
        new() { Items = items, Total = total, Page = page, PageSize = pageSize };
}
