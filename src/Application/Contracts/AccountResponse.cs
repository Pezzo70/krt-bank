using Domain.Entities;

namespace Application.Contracts;

public sealed record AccountResponse(
    Guid Id, string Name, string Cpf, bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt)
{
    public static AccountResponse From(Account account) => new(
        account.Id, account.Name.Value, account.Cpf.Value, account.IsActive, account.CreatedAt, account.UpdatedAt);
}
