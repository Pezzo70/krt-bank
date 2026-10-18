using FluentResults;

namespace Service.Errors;

public sealed class NotFoundError(string message) : Error(message);
