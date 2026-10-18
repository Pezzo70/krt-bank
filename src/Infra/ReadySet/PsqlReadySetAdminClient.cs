using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Infra.ReadySet;

public sealed class PsqlReadySetAdminClient(IConfiguration configuration, ReadySetCacheOptions options)
    : IReadySetAdminClient
{
    public async Task<string> ExecuteAsync(string command, CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnectionStringBuilder(configuration.GetConnectionString("ReadySet"));
        var start = new ProcessStartInfo(options.PsqlPath)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in new[] { "-X", "-A", "-t", "-F", "|", "--no-password", "-v", "ON_ERROR_STOP=1" })
            start.ArgumentList.Add(argument);

        // Credentials and SQL never appear in process arguments or exception messages.
        start.Environment["PGHOST"] = connection.Host;
        start.Environment["PGPORT"] = connection.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        start.Environment["PGDATABASE"] = connection.Database;
        start.Environment["PGUSER"] = connection.Username;
        start.Environment["PGPASSWORD"] = connection.Password ?? string.Empty;
        start.Environment["PGCONNECT_TIMEOUT"] = Math.Clamp(connection.Timeout, 1, 10).ToString();
        start.Environment["PGSSLMODE"] = connection.SslMode switch
        {
            SslMode.VerifyCA => "verify-ca",
            SslMode.VerifyFull => "verify-full",
            _ => connection.SslMode.ToString().ToLowerInvariant()
        };
        if (!string.IsNullOrEmpty(connection.RootCertificate))
            start.Environment["PGSSLROOTCERT"] = connection.RootCertificate;
        start.Environment["PGCLIENTENCODING"] = "UTF8";

        using var process = new Process { StartInfo = start };
        try
        {
            process.Start();
        }
        catch (Win32Exception)
        {
            throw new InvalidOperationException("Install the PostgreSQL client (psql) or configure ReadySet:PsqlPath.");
        }

        try
        {
            var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.StandardInput.WriteLineAsync(command.AsMemory(), cancellationToken);
            process.StandardInput.Close();
            await process.WaitForExitAsync(cancellationToken);
            await error;
            var result = await output;
            if (process.ExitCode != 0) throw new ReadySetAdminException();
            return result;
        }
        catch (IOException)
        {
            Terminate(process);
            throw new ReadySetAdminException();
        }
        catch
        {
            Terminate(process);
            throw;
        }
    }

    private static void Terminate(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // The process may have exited between the check and termination.
        }
    }
}
