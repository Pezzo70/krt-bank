using Domain.Entities;
using Domain.Interfaces;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class ContaRepository(BankDbContext context) : IContaRepository
{
    public Task<Conta?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Contas.SingleOrDefaultAsync(conta => conta.Id == id, cancellationToken);

    public void Add(Conta conta) => context.Contas.Add(conta);

    public void Remove(Conta conta) => conta.SetActive(false);
}
