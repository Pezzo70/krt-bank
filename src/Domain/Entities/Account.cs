using Domain.ValueObjects;

namespace Domain.Entities;

public class Account : Entity
{
    public required string Name { get; set; }
    public required Cpf Cpf { get; set; }
}
