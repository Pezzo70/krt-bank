using Domain.Entities;
using Domain.Errors;
using Domain.Interfaces;
using Domain.ValueObjects;
using FluentResults;

namespace Service;

public sealed class AccountService(
    IAccountRepository repository,
    IUnitOfWork unitOfWork
) : IAccountService
{
    public async Task<Result<Account>> CreateAsync(
        string? name,
        string? cpf,
        CancellationToken cancellationToken = default
    )
    {
        var input = Validate(name, cpf);
        if (input.IsFailed)
            return Result.Fail<Account>(input.Errors);
        if (
            await repository.ExistsByCpfAsync(input.Value.Cpf, cancellationToken: cancellationToken)
        )
            return Result.Fail<Account>(new ConflictError("CPF is already registered."));

        var account = new Account { Name = input.Value.Name, Cpf = input.Value.Cpf };
        repository.Add(account);
        var commit = await unitOfWork.CommitAsync(cancellationToken);
        return commit.IsFailed ? Result.Fail<Account>(commit.Errors) : Result.Ok(account);
    }

    public async Task<Result<Account>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default
    )
    {
        var account = await repository.GetByIdAsync(id, cancellationToken: cancellationToken);
        return account is null
            ? Result.Fail<Account>(new NotFoundError("Account was not found."))
            : Result.Ok(account);
    }

    public async Task<Result<Account>> UpdateAsync(
        Guid id,
        string? name,
        string? cpf,
        CancellationToken cancellationToken = default
    )
    {
        var input = Validate(name, cpf);
        if (input.IsFailed)
            return Result.Fail<Account>(input.Errors);
        var account = await repository.GetByIdAsync(id, cacheable: false, cancellationToken: cancellationToken);
        if (account is null)
            return Result.Fail<Account>(new NotFoundError("Account was not found."));
        if (account.Cpf != input.Value.Cpf &&
            await repository.ExistsByCpfAsync(input.Value.Cpf, cancellationToken))
            return Result.Fail<Account>(new ConflictError("CPF is already registered."));

        account.Name = input.Value.Name;
        account.Cpf = input.Value.Cpf;
        var commit = await unitOfWork.CommitAsync(cancellationToken);
        return commit.IsFailed ? Result.Fail<Account>(commit.Errors) : Result.Ok(account);
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var account = await repository.GetByIdAsync(id, cacheable: false, cancellationToken: cancellationToken);
        if (account is null)
            return Result.Fail(new NotFoundError("Account was not found."));
        repository.Remove(account);
        return await unitOfWork.CommitAsync(cancellationToken);
    }

    private static Result<(AccountName Name, Cpf Cpf)> Validate(string? name, string? cpf)
    {
        var errors = new List<IError>();
        var validatedName = AccountName.Create(name);
        errors.AddRange(
            validatedName.Errors.Select(error => new ValidationError("name", error.Message))
        );
        var validatedCpf = Cpf.Create(cpf);
        errors.AddRange(
            validatedCpf.Errors.Select(error => new ValidationError("cpf", error.Message))
        );
        return errors.Count > 0
            ? Result.Fail<(AccountName Name, Cpf Cpf)>(errors)
            : Result.Ok((Name: validatedName.Value, Cpf: validatedCpf.Value));
    }
}
