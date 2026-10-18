using Domain.Entities;
using Domain.Interfaces;
using Infra.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infra.Repositories;

public sealed class AccountRepository(BankDbContext context) : IAccountRepository
{
    public Task<Account?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Accounts.SingleOrDefaultAsync(account => account.Id == id, cancellationToken);

    public void Add(Account account) => context.Accounts.Add(account);

    public void Remove(Account account) => account.SetActive(false);
}
