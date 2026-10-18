using FluentResults;

namespace Domain.ValueObjects;

public sealed record AccountName
{
    public string Value { get; }

    private AccountName(string value) => Value = value;

    public static Result<AccountName> Create(string? name) =>
        Validate(name).Map(value => new AccountName(value));

    public static Result<string> Validate(string? name) =>
        string.IsNullOrWhiteSpace(name)
            ? Result.Fail<string>("Account name is required.")
            : Result.Ok(name.Trim());
}
