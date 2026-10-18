using Application.Contracts;
using Domain.Interfaces;
using FluentResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Service.Errors;

namespace Application.Controllers;

[ApiController]
[Route("api/accounts")]
public sealed class AccountsController(IAccountService service) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(AccountResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AccountResponse>> Create(AccountRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.CreateAsync(request.Name, request.Cpf, cancellationToken);
            if (result.IsFailed) return Failure(result.Errors);
            return CreatedAtAction(nameof(GetById), new { id = result.Value.Id }, AccountResponse.From(result.Value));
        }
        catch (DbUpdateException exception) when (IsDuplicateCpf(exception))
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "CPF is already registered.");
        }
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(AccountResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AccountResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await service.GetByIdAsync(id, cancellationToken);
        if (result.IsFailed) return Failure(result.Errors);
        return Ok(AccountResponse.From(result.Value));
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(AccountResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AccountResponse>> Update(Guid id, AccountRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.UpdateAsync(id, request.Name, request.Cpf, cancellationToken);
            if (result.IsFailed) return Failure(result.Errors);
            return Ok(AccountResponse.From(result.Value));
        }
        catch (DbUpdateException exception) when (IsDuplicateCpf(exception))
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "CPF is already registered.");
        }
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await service.DeleteAsync(id, cancellationToken);
        return result.IsFailed ? Failure(result.Errors) : NoContent();
    }

    private static bool IsDuplicateCpf(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_accounts_cpf"
        };

    private ActionResult Failure(IReadOnlyList<IError> errors)
    {
        var validation = errors.OfType<ValidationError>().ToArray();
        if (validation.Length > 0)
        {
            return ValidationProblem(new ValidationProblemDetails(validation.GroupBy(error => error.Field)
                .ToDictionary(group => group.Key, group => group.Select(error => error.Message).ToArray()))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Validation failed."
            });
        }
        if (errors.OfType<NotFoundError>().Any())
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Account was not found.");
        if (errors.OfType<ConflictError>().Any())
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "CPF is already registered.");
        return Problem(statusCode: StatusCodes.Status500InternalServerError, title: "An unexpected error occurred.");
    }
}
