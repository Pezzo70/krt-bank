using Domain.ValueObjects;

namespace Domain.Entities;

public class Conta : Entity
{
    public required string Nome { get; set; }
    public required Cpf Cpf { get; set; }
}
