using Domain.Entities;
using Domain.Interfaces;

namespace Domain.Outbox;

public sealed class AccountOutboxPayloadMapper : IOutboxPayloadMapper
{
    public Type EntityType => typeof(Account);
    public string EventPrefix => "account";

    public object Map(Entity entity)
    {
        var account = (Account)entity;
        return new AccountOutboxPayload(
            account.Id, account.Name, account.Cpf.Value, account.IsActive,
            account.CreatedAt, account.UpdatedAt);
    }
}
