using Domain.Entities;
using Domain.Interfaces;
using Domain.ValueObjects;
using FluentResults;
using Service.Errors;

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
        await unitOfWork.CommitAsync(cancellationToken);
        return Result.Ok(account);
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
        await unitOfWork.CommitAsync(cancellationToken);
        return Result.Ok(account);
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var account = await repository.GetByIdAsync(id, cacheable: false, cancellationToken: cancellationToken);
        if (account is null)
            return Result.Fail(new NotFoundError("Account was not found."));
        repository.Remove(account);
        await unitOfWork.CommitAsync(cancellationToken);
        return Result.Ok();
    }

    private static Result<(string Name, Cpf Cpf)> Validate(string? name, string? cpf)
    {
        var errors = new List<IError>();
        if (string.IsNullOrWhiteSpace(name))
            errors.Add(new ValidationError("name", "Account name is required."));
        var validatedCpf = Cpf.Create(cpf);
        errors.AddRange(
            validatedCpf.Errors.Select(error => new ValidationError("cpf", error.Message))
        );
        return errors.Count > 0
            ? Result.Fail<(string Name, Cpf Cpf)>(errors)
            : Result.Ok((Name: name!.Trim(), Cpf: validatedCpf.Value));
    }
}
