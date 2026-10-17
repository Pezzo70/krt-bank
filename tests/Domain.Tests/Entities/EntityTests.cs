using Domain.Entities;

namespace Domain.Tests.Entities;

public class EntityTests
{
    private static readonly DateTimeOffset InitialTime =
        new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Construtor_DeveGerarGuidsUnicosENaoVazios()
    {
        var primeira = new TestEntity();
        var segunda = new TestEntity();

        Assert.NotEqual(Guid.Empty, primeira.Id);
        Assert.NotEqual(Guid.Empty, segunda.Id);
        Assert.NotEqual(primeira.Id, segunda.Id);
    }

    [Fact]
    public void Construtor_DeveIniciarAtivaComDataDeCriacaoEmUtc()
    {
        var clock = new ManualTimeProvider(InitialTime);

        var entidade = new TestEntity(clock);

        Assert.Equal(0, entidade.InternalId);
        Assert.True(entidade.IsActive);
        Assert.Equal(InitialTime, entidade.CreatedAt);
        Assert.Equal(TimeSpan.Zero, entidade.CreatedAt.Offset);
        Assert.Null(entidade.UpdatedAt);
    }

    [Fact]
    public void Deactivate_DeveDesativarERegistrarAtualizacao()
    {
        var clock = new ManualTimeProvider(InitialTime);
        var entidade = new TestEntity(clock);
        var id = entidade.Id;
        clock.Advance(TimeSpan.FromMinutes(1));

        entidade.Deactivate();

        Assert.False(entidade.IsActive);
        Assert.Equal(clock.GetUtcNow(), entidade.UpdatedAt);
        Assert.Equal(TimeSpan.Zero, entidade.UpdatedAt!.Value.Offset);
        Assert.Equal(InitialTime, entidade.CreatedAt);
        Assert.Equal(id, entidade.Id);
    }

    [Fact]
    public void Activate_DeveReativarERegistrarNovaAtualizacao()
    {
        var clock = new ManualTimeProvider(InitialTime);
        var entidade = new TestEntity(clock);
        clock.Advance(TimeSpan.FromMinutes(1));
        entidade.Deactivate();
        clock.Advance(TimeSpan.FromMinutes(1));

        entidade.Activate();

        Assert.True(entidade.IsActive);
        Assert.Equal(clock.GetUtcNow(), entidade.UpdatedAt);
        Assert.Equal(InitialTime, entidade.CreatedAt);
    }

    [Fact]
    public void Activate_NaoDeveRegistrarAtualizacaoQuandoJaEstiverAtiva()
    {
        var clock = new ManualTimeProvider(InitialTime);
        var entidade = new TestEntity(clock);
        clock.Advance(TimeSpan.FromMinutes(1));

        entidade.Activate();

        Assert.True(entidade.IsActive);
        Assert.Null(entidade.UpdatedAt);
        Assert.Equal(InitialTime, entidade.CreatedAt);
    }

    [Fact]
    public void Activate_NaoDeveAlterarAtualizacaoAnteriorQuandoJaEstiverAtiva()
    {
        var clock = new ManualTimeProvider(InitialTime);
        var entidade = new TestEntity(clock);
        clock.Advance(TimeSpan.FromMinutes(1));
        entidade.Deactivate();
        clock.Advance(TimeSpan.FromMinutes(1));
        entidade.Activate();
        var ultimaAtualizacao = entidade.UpdatedAt;
        clock.Advance(TimeSpan.FromMinutes(1));

        entidade.Activate();

        Assert.True(entidade.IsActive);
        Assert.Equal(ultimaAtualizacao, entidade.UpdatedAt);
        Assert.Equal(InitialTime, entidade.CreatedAt);
    }

    [Fact]
    public void Deactivate_NaoDeveAlterarAtualizacaoAnteriorQuandoJaEstiverInativa()
    {
        var clock = new ManualTimeProvider(InitialTime);
        var entidade = new TestEntity(clock);
        clock.Advance(TimeSpan.FromMinutes(1));
        entidade.Deactivate();
        var ultimaAtualizacao = entidade.UpdatedAt;
        clock.Advance(TimeSpan.FromMinutes(1));

        entidade.Deactivate();

        Assert.False(entidade.IsActive);
        Assert.Equal(ultimaAtualizacao, entidade.UpdatedAt);
        Assert.Equal(InitialTime, entidade.CreatedAt);
    }

    [Fact]
    public void MarkAsUpdated_DeveRegistrarNovaDataEPreservarIdentidadeECriacao()
    {
        var clock = new ManualTimeProvider(InitialTime);
        var entidade = new TestEntity(clock);
        var id = entidade.Id;
        clock.Advance(TimeSpan.FromMinutes(1));
        entidade.Update();
        Assert.Equal(clock.GetUtcNow(), entidade.UpdatedAt);
        clock.Advance(TimeSpan.FromMinutes(1));

        entidade.Update();

        Assert.Equal(clock.GetUtcNow(), entidade.UpdatedAt);
        Assert.Equal(InitialTime, entidade.CreatedAt);
        Assert.Equal(id, entidade.Id);
        Assert.True(entidade.IsActive);
    }

    private sealed class TestEntity(TimeProvider? timeProvider = null) : Entity(timeProvider)
    {
        public void Update() => MarkAsUpdated();
    }

    private sealed class ManualTimeProvider(DateTimeOffset initialTime) : TimeProvider
    {
        private DateTimeOffset _utcNow = initialTime;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan interval) => _utcNow = _utcNow.Add(interval);
    }
}
