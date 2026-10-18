using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infra.Queries;

public static class AccountReadQuery
{
    public static IQueryable<Account> ById(IQueryable<Account> accounts, Guid id) =>
        accounts.AsNoTracking()
            .Where(account => account.Id == id);
}
