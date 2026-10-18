using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Domain.Entities;
using Domain.Interfaces;
using Domain.ValueObjects;
using Infra.Persistence;
using Infra.Repositories;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infra.Tests;

[Collection("Infra")]
public sealed class AccountsApiTests(InfraFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Lifecycle_ShouldPersistChangesOutboxAndReadThroughReadySet()
    {
        await using var factory = new AccountApiFactory(fixture);
        using var client = factory.CreateClient();
        using var created = await client.PostAsJsonAsync("/api/accounts", new { name = "  Example account  ", cpf = "287.841.420-90" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = body.RootElement.GetProperty("id").GetGuid();
        Assert.Equal("Example account", body.RootElement.GetProperty("name").GetString());
        Assert.Equal("28784142090", body.RootElement.GetProperty("cpf").GetString());
        Assert.False(body.RootElement.TryGetProperty("internalId", out _));
        Assert.EndsWith($"/api/accounts/{id}", created.Headers.Location!.ToString());

        await InfraFixture.EventuallyAsync(async () =>
        {
            using var response = await client.GetAsync($"/api/accounts/{id}");
            return response.StatusCode == HttpStatusCode.OK;
        });
        using var updated = await client.PutAsJsonAsync($"/api/accounts/{id}",
            new { name = "Updated account", cpf = "087.827.580-00" });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        await InfraFixture.EventuallyAsync(async () =>
        {
            using var response = await client.GetAsync($"/api/accounts/{id}");
            if (response.StatusCode != HttpStatusCode.OK) return false;
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return json.RootElement.GetProperty("name").GetString() == "Updated account" &&
                json.RootElement.GetProperty("cpf").GetString() == "08782758000";
        });
        using var deleted = await client.DeleteAsync($"/api/accounts/{id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        await InfraFixture.EventuallyAsync(async () =>
        {
            using var response = await client.GetAsync($"/api/accounts/{id}");
            return response.StatusCode == HttpStatusCode.NotFound;
        });

        await using var context = fixture.CreateContext();
        var persisted = await context.Accounts.IgnoreQueryFilters().SingleAsync();
        Assert.False(persisted.IsActive);
        Assert.Equal("08782758000", persisted.Cpf.Value);
        Assert.Equal(new[] { "account.created", "account.deleted", "account.updated" },
            await context.OutboxMessages.OrderBy(message => message.Type).Select(message => message.Type).ToArrayAsync());
    }

    [Theory]
    [InlineData(" ", "28784142090", "name")]
    [InlineData("Example account", "123", "cpf")]
    [InlineData(null, null, "name")]
    [InlineData("Example account", "11111111111", "cpf")]
    public async Task InvalidInput_ShouldReturnValidationProblemWithoutWriting(string? name, string? cpf, string field)
    {
        await using var factory = new AccountApiFactory(fixture);
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/accounts", new { name, cpf });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty(field, out _));
        await using var context = fixture.CreateContext();
        Assert.Equal(0, await context.Accounts.CountAsync());
        Assert.Equal(0, await context.OutboxMessages.CountAsync());
    }

    [Fact]
    public async Task DuplicateCpf_ShouldReturnConflictIncludingAfterSoftDeletion()
    {
        await using var factory = new AccountApiFactory(fixture);
        using var client = factory.CreateClient();
        var request = new { name = "Example account", cpf = "28784142090" };
        using var first = await client.PostAsJsonAsync("/api/accounts", request);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using var duplicate = await client.PostAsJsonAsync("/api/accounts", request);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        using var deleted = await client.DeleteAsync(first.Headers.Location);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var reserved = await client.PostAsJsonAsync("/api/accounts", request);
        Assert.Equal(HttpStatusCode.Conflict, reserved.StatusCode);
        await using var context = fixture.CreateContext();
        Assert.Equal(1, await context.Accounts.IgnoreQueryFilters().CountAsync());
        Assert.Equal(2, await context.OutboxMessages.CountAsync());
    }

    [Fact]
    public async Task ConcurrentCreates_ShouldReturnOneCreatedAndOneConflict()
    {
        // Simulate stale pre-checks so the database constraint must resolve the conflict.
        await using var factory = new AccountApiFactory(fixture, services =>
            services.Replace(ServiceDescriptor.Scoped<IAccountRepository, StaleCpfCheckRepository>()));
        using var client = factory.CreateClient();
        var request = new { name = "Example account", cpf = "28784142090" };
        var responses = await Task.WhenAll(client.PostAsJsonAsync("/api/accounts", request),
            client.PostAsJsonAsync("/api/accounts", request));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
            await using var context = fixture.CreateContext();
            Assert.Equal(1, await context.Accounts.CountAsync());
            Assert.Equal(1, await context.OutboxMessages.CountAsync());
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    [Fact]
    public async Task Update_ShouldRejectAnotherAccountsCpfWithoutChangingData()
    {
        await using var factory = new AccountApiFactory(fixture, services =>
            services.Replace(ServiceDescriptor.Scoped<IAccountRepository, StaleCpfCheckRepository>()));
        using var client = factory.CreateClient();
        using var first = await client.PostAsJsonAsync("/api/accounts", new { name = "First", cpf = "28784142090" });
        using var second = await client.PostAsJsonAsync("/api/accounts", new { name = "Second", cpf = "08782758000" });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        using var conflict = await client.PutAsJsonAsync(second.Headers.Location, new { name = "Changed", cpf = "28784142090" });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        await using var context = fixture.CreateContext();
        Assert.Equal("08782758000", (await context.Accounts.SingleAsync(account => account.Name == "Second")).Cpf.Value);
        Assert.Equal(2, await context.OutboxMessages.CountAsync());
    }

    [Fact]
    public async Task UnchangedUpdate_ShouldAcceptOwnCpfWithoutProducingAnotherEvent()
    {
        await using var factory = new AccountApiFactory(fixture);
        using var client = factory.CreateClient();
        using var created = await client.PostAsJsonAsync("/api/accounts", new { name = "Example", cpf = "28784142090" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var updated = await client.PutAsJsonAsync(created.Headers.Location, new { name = " Example ", cpf = "287.841.420-90" });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        await using var context = fixture.CreateContext();
        Assert.Equal(1, await context.OutboxMessages.CountAsync());
        Assert.Null((await context.Accounts.SingleAsync()).UpdatedAt);
    }

    [Fact]
    public async Task MissingAccount_ShouldReturnNotFoundForReadsUpdatesAndDeletion()
    {
        await using var factory = new AccountApiFactory(fixture);
        using var client = factory.CreateClient();
        var path = $"/api/accounts/{Guid.NewGuid()}";
        using var read = await client.GetAsync(path);
        using var update = await client.PutAsJsonAsync(path, new { name = "Example", cpf = "28784142090" });
        using var delete = await client.DeleteAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
    }

    [Fact]
    public async Task UnexpectedFailure_ShouldReturnProblemWithoutLeakingExceptionDetails()
    {
        await using var factory = new AccountApiFactory(fixture, services =>
            services.Replace(ServiceDescriptor.Scoped<IAccountRepository, FailingRepository>()));
        using var client = factory.CreateClient();
        using var response = await client.GetAsync($"/api/accounts/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("sensitive details", body);
        Assert.DoesNotContain(nameof(InvalidOperationException), body);
    }

    private sealed class AccountApiFactory(InfraFixture fixture, Action<IServiceCollection>? configure = null)
        : WebApplicationFactory<Program>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(configuration => configuration
                .AddConfiguration(fixture.CreateConfiguration())
                .AddInMemoryCollection(new Dictionary<string, string?> { [HostDefaults.EnvironmentKey] = "Testing" }));
            return base.CreateHost(builder);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton(fixture.GetConnections()));
                configure?.Invoke(services);
            });
        }
    }

    private sealed class FailingRepository : IAccountRepository
    {
        public Task<Account?> GetByIdAsync(Guid id, bool cacheable = true, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("sensitive details");

        public Task<bool> ExistsByCpfAsync(Cpf cpf, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public void Add(Account account) => throw new NotSupportedException();
        public void Remove(Account account) => throw new NotSupportedException();
    }

    private sealed class StaleCpfCheckRepository(BankDbContext context, ReadySetDbContext readySet, ILogger<AccountRepository> logger) : IAccountRepository
    {
        private readonly AccountRepository _repository = new(context, readySet, logger);

        public Task<Account?> GetByIdAsync(Guid id, bool cacheable = true, CancellationToken cancellationToken = default) =>
            _repository.GetByIdAsync(id, cacheable, cancellationToken);

        public Task<bool> ExistsByCpfAsync(Cpf cpf, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public void Add(Account account) => _repository.Add(account);
        public void Remove(Account account) => _repository.Remove(account);
    }
}
