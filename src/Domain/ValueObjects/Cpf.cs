using System.Text.RegularExpressions;
using FluentResults;

namespace Domain.ValueObjects;

public sealed record Cpf
{
    public string Value { get; }

    private Cpf(string cpf) => Value = cpf;

    public static Result<Cpf> Create(string? cpf) =>
        Validate(cpf).Map(value => new Cpf(value));

    public static Result<string> Validate(string? cpf)
    {
        if (string.IsNullOrWhiteSpace(cpf))
        {
            return Result.Fail<string>("CPF is required.");
        }

        var input = cpf.Trim();

        if (!Regex.IsMatch(input, @"\A(?:[0-9]{11}|[0-9]{3}\.[0-9]{3}\.[0-9]{3}-[0-9]{2})\z"))
        {
            return Result.Fail<string>("CPF must contain 11 digits, with or without the standard mask.");
        }

        var value = input.Replace(".", string.Empty).Replace("-", string.Empty);

        if (value.All(digit => digit == value[0]))
        {
            return Result.Fail<string>("CPF cannot contain only repeated digits.");
        }

        if (CalculateCheckDigit(value.AsSpan(0, 9)) != value[9] - '0' ||
            CalculateCheckDigit(value.AsSpan(0, 10)) != value[10] - '0')
        {
            return Result.Fail<string>("CPF check digits are invalid.");
        }

        return Result.Ok(value);
    }

    private static int CalculateCheckDigit(ReadOnlySpan<char> digits)
    {
        var sum = 0;

        for (var index = 0; index < digits.Length; index++)
        {
            sum += (digits[index] - '0') * (digits.Length + 1 - index);
        }

        var remainder = sum % 11;

        return remainder < 2 ? 0 : 11 - remainder;
    }
}
