using FluentResults;

namespace Domain.Errors;

public sealed class NotFoundError(string message) : Error(message);
