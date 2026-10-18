# krt-bank

.NET 8 banking domain with PostgreSQL, Entity Framework Core, a transactional Outbox and ReadySet.

## Projects

- `Domain`: Account, Entity, CPF value object, Outbox message/payload definitions and persistence/query interfaces.
- `Infra`: EF contexts, migrations, repositories, Unit of Work, Outbox serialization/persistence and ReadySet queries.
- `Domain.Tests`: existing CPF unit tests.
- `Infra.Tests`: integration tests against disposable PostgreSQL and ReadySet containers.
- `ReadySet.Setup`: a small C# host that runs the same automatic cache initialization as the future API host.

## Local setup

Requires the .NET 8 SDK pinned in `global.json`, Docker, Compose and the PostgreSQL client (`psql`) installed alongside the C# host. The application invokes `psql` directly, without Bash or Docker commands. On Ubuntu, the client is available in `postgresql-client-16`. From the repository root in Bash:

```bash
cp -n .env.example .env
```

Update `.env` before starting. Example credentials are for local development. If database credentials or ports change, update both connection strings. `.env` is ignored by Git.

```bash
set -a
source .env
set +a

dotnet tool restore
dotnet restore KrtBanking.sln
docker compose up -d --wait postgres
dotnet ef database update --context BankDbContext --project src/Infra --startup-project src/Infra
docker compose up -d readyset
dotnet run --project tools/ReadySet.Setup
```

Apply migrations before starting a host that uses Infra. `AddInfra()` registers an `IHostedService` that waits for ReadySet replication and registers the caches from C# definitions. No Bash setup script is required. The setup tool runs that same hosted service; when an API host is added, starting the API will run it automatically. URL-reserved characters in credentials must be encoded in ReadySet's `UPSTREAM_DB_URL`.

Default ports: PostgreSQL `5432`, ReadySet `5433`, ReadySet metrics `6034`. Override them in `.env`. Data uses Docker volumes. Images are PostgreSQL 16 and ReadySet `stable-260924`.

The account rename migration preserves existing rows and identifiers by renaming the table, column and indexes. Earlier migrations retain their original names and schema so existing databases can upgrade. Previously stored Outbox messages retain their historical payloads and event types.

## Registration and usage

The future application host loads configuration and registers:

```csharp
services.AddInfra(configuration);
```

Both `ConnectionStrings:Postgres` and `ConnectionStrings:ReadySet` are required. Environment variable equivalents are included in `.env.example`. The repository, primary context and Unit of Work share a service scope.

```csharp
var account = new Account { Name = "Example account", Cpf = validatedCpf };
accountRepository.Add(account);
await unitOfWork.CommitAsync(cancellationToken);

var existing = await accountRepository.GetByIdAsync(account.Id, cancellationToken);
if (existing is not null)
{
    existing.Name = "Updated name";
    await unitOfWork.CommitAsync(cancellationToken);
}

var result = await accountQueries.GetByIdAsync(account.Id, cancellationToken);
```

`Cpf.Create()` returns `Result<Cpf>`; check success before constructing the account. `required` enforces C# initialization but does not validate empty names. Input validation and database error translation belong in the future application/API layer.

## Persistence and Outbox

- `InternalId`: PostgreSQL-generated integer primary key.
- `Id`: domain GUID with a unique index.
- Normalized CPF: required, unique across active and inactive accounts.
- Timestamps: UTC (`timestamptz`); updates set `UpdatedAt` on commit.
- `Remove(account)`: soft deletion. The row and CPF remain reserved; normal queries hide inactive accounts.

Repositories stage changes. Business writes go through `IUnitOfWork.CommitAsync()`, which inspects tracked entities in one loop and saves changes and Outbox messages in one transaction. After failure, discard the Unit of Work and context; reuse is rejected.

New events are `account.created`, `account.updated` and `account.deleted`. A commit without changes creates no messages. Each entity type registers its EF configuration and Outbox payload mapper. The Outbox message, factory/mapper interfaces and typed account payload are defined in Domain. Infra implements the factory (JSON serialization), EF mapping and transactional persistence.

`OutboxMessage` inherits `Entity`, using its `Id`, `InternalId`, `IsActive`, `CreatedAt` and `UpdatedAt`. The migration renames `event_id` to `id` and `occurred_at` to `created_at`, preserving existing identifiers and timestamps and keeping existing messages active. `outbox_messages` also stores `entity_id`, `type`, JSON `payload` and `published_at`. Account payload fields are `accountId`, `name`, `cpf`, `isActive`, `createdAt` and `updatedAt`; `InternalId` is excluded.

Changes to Outbox messages do not generate further Outbox messages. `published_at` remains null until a future publisher is implemented. Operations that bypass EF change tracking also bypass Outbox creation.

## ReadySet and Entity Framework Core

`IAccountRepository` uses the PostgreSQL `BankDbContext`. `IAccountQueries` uses LINQ through `ReadySetDbContext`, returning `Account` with `AsNoTracking()`. The same query runs without tracking when falling back to PostgreSQL. EF Core and Npgsql generate and parameterize the SQL. The initializer derives the cache definition from that same LINQ expression.

The deep cache replicates `public.accounts` through PostgreSQL logical replication. Changes appear asynchronously; reads requiring immediate consistency should use the primary repository.

Transient connection failures and timeouts trigger a warning and run the same LINQ query through the primary context. A missing cached row and caller cancellation do not trigger fallback.

Cache definitions live in Infra and use the shared query expression:

```csharp
services.AddReadySetCache<Account>(
    "accounts_by_id",
    (context, id) => AccountReadQuery.ById(context.Accounts, id));
```

This registration is already included in `AddInfra()`. `AddReadySetCache` is our extension, not a built-in EF feature. `Guid.Empty` is used internally only to generate EF parameter metadata; it does not restrict the cache to that GUID. Actual reads supply their own ID.

On host startup, `ReadySetCacheInitializer`:

1. Waits until ReadySet reports running replication.
2. Generates the parameterized query through EF Core.
3. Computes a stable query version and checks for `accounts_by_id_<version>`.
4. Creates the deep cache only if that version is missing.

Repeated starts reuse the same cache. Changed query SQL produces a different version, preserving the caches used by older application instances. Concurrent starts tolerate another instance registering the same version. Old versions are not deleted automatically.

The startup wait defaults to 120 seconds. If ReadySet remains unavailable, the initializer logs a warning and permits the host to start; reads can still use PostgreSQL. Initialization is retried during that wait and again on a later startup. Caller cancellation is propagated. Missing `psql` fails startup with a configuration error.

Optional configuration:

```text
ReadySet__PsqlPath=psql
ReadySet__StartupTimeoutSeconds=120
```

`PsqlPath` can be an absolute executable path. Production images must include the PostgreSQL client. Credentials are passed through the child process environment and commands through standard input, without a shell. Only cache administration uses `psql`; business reads and writes remain EF Core queries. This is needed because the pinned ReadySet version rejected these administrative commands through Npgsql's extended protocol.

For manual inspection:

```bash
docker compose exec postgres sh -c 'PGPASSWORD="$POSTGRES_PASSWORD" psql -h readyset -p 5433 -U "$POSTGRES_USER" -d "$POSTGRES_DB"'
```

```sql
SHOW READYSET STATUS;
SHOW CACHES;
```

The integration suite verifies `readyset(accounts_by_id_<version>)` using `EXPLAIN LAST STATEMENT` after executing an EF-generated query. It also reads through the EF context with an unavailable origin to confirm cache usage.

Metrics: `http://localhost:6034/metrics` using the configured port.

```bash
docker compose logs --tail 100 readyset
docker compose down
```

`down` preserves volumes. Production migrations and replication permissions require deployment-specific configuration. If upgrading an existing local ReadySet deployment, starting a host registers the current version automatically. Legacy caches are left intact and are no longer used when their query shape differs.

## Tests

```bash
dotnet test KrtBanking.sln
```

Integration tests start isolated PostgreSQL and ReadySet containers, apply migrations and remove containers on completion. They never use the `.env` database. Docker and `psql` must be accessible to the test process. Set `ReadySet__PsqlPath` if the client is not on PATH. The fixture starts a real C# host to register caches, exercising the production initializer.

Coverage includes identifiers, CPF and timestamp materialization, updates, soft deletion, CPF uniqueness, batch persistence, Outbox atomicity, unchanged commits, actual cache usage, replication, fallback, automatic host initialization, query version changes, repeated startup and cancellation.

Domain tests only:

```bash
dotnet test tests/Domain.Tests
```
