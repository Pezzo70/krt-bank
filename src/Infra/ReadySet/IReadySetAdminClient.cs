namespace Infra.ReadySet;

public interface IReadySetAdminClient
{
    Task<string> ExecuteAsync(string command, CancellationToken cancellationToken);
}

public sealed class ReadySetAdminException() : Exception("The ReadySet administrative command failed.");
