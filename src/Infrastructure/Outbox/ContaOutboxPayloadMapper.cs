using Domain.Entities;

namespace Infrastructure.Outbox;

public sealed class ContaOutboxPayloadMapper : IOutboxPayloadMapper
{
    public Type EntityType => typeof(Conta);
    public string EventPrefix => "conta";

    public object Map(Entity entity)
    {
        var conta = (Conta)entity;
        return new
        {
            ContaId = conta.Id,
            conta.Nome,
            Cpf = conta.Cpf.Value,
            conta.IsActive,
            conta.CreatedAt,
            conta.UpdatedAt
        };
    }
}
