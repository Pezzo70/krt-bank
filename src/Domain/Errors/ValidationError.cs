using FluentResults;

namespace Domain.Errors;

public sealed class ValidationError(string field, string message) : Error(message)
{
    public string Field { get; } = field;
}
