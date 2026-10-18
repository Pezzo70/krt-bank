using FluentResults;

namespace Domain.Errors;

public sealed class ConflictError(string message) : Error(message);
