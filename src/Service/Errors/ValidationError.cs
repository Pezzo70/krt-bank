using FluentResults;

namespace Service.Errors;

public sealed class ValidationError(string field, string message) : Error(message)
{
    public string Field { get; } = field;
}
