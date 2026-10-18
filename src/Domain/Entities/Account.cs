using Domain.ValueObjects;

namespace Domain.Entities;

public class Account : Entity
{
    public required AccountName Name { get; set; }
    public required Cpf Cpf { get; set; }
}
