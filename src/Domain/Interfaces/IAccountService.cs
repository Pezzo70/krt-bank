using Domain.Entities;
using FluentResults;

namespace Domain.Interfaces;

public interface IAccountService
{
    Task<Result<Account>> CreateAsync(string? name, string? cpf, CancellationToken cancellationToken = default);
    Task<Result<Account>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result<Account>> UpdateAsync(Guid id, string? name, string? cpf, CancellationToken cancellationToken = default);
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
