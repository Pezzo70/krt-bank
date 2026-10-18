using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Infra.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infra.ReadySet;

public sealed class ReadySetCacheDefinition
{
    private readonly Func<BankDbContext, IQueryable> _query;
    public string Name { get; }

    public ReadySetCacheDefinition(string name, Func<BankDbContext, IQueryable> query)
    {
        if (!Regex.IsMatch(name, @"\A[a-z][a-z0-9_]{0,39}\z"))
            throw new ArgumentException("Use a lowercase cache name of up to 40 letters, digits or underscores.", nameof(name));
        Name = name;
        _query = query;
    }

    public ReadySetCacheCommand CreateCommand(BankDbContext context)
    {
        // Use the same LINQ expression as the reader. Replace Npgsql parameter
        // metadata with positional parameters; example values are never cached as literals.
        var generated = _query(context).ToQueryString().Replace("\r\n", "\n");
        var parameters = Regex.Matches(generated, @"(?m)^-- (@\w+)=")
            .Select(match => match.Groups[1].Value).Distinct().ToArray();
        var sql = string.Join('\n', generated.Split('\n').Where(line => !line.StartsWith("-- ")));
        for (var index = 0; index < parameters.Length; index++)
            sql = Regex.Replace(sql, Regex.Escape(parameters[index]) + @"\b", _ => "$" + (index + 1));

        sql = sql.TrimEnd(';', '\n', '\r');
        var version = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sql)))[..12].ToLowerInvariant();
        return new ReadySetCacheCommand($"{Name}_{version}", sql);
    }
}

public sealed record ReadySetCacheCommand(string Name, string Query)
{
    public string CreateSql => $"CREATE DEEP CACHE {Name} FROM\n{Query};";
}
