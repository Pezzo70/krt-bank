using FluentResults;

namespace Service.Errors;

public sealed class ConflictError(string message) : Error(message);
