namespace Infra.ReadySet;

public sealed class ReadySetCacheOptions
{
    public string PsqlPath { get; init; } = "psql";
    public int StartupTimeoutSeconds { get; init; } = 120;
}
