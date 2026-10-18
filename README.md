# krt-bank

.NET 8 banking domain with PostgreSQL, Entity Framework Core, a transactional Outbox and ReadySet.

## Projects

- `Domain`: Account, Entity, CPF value object, Outbox message and all service, repository and Unit of Work interfaces.
- `Application`: ASP.NET Core Web API with Controllers, HTTP contracts, error responses and all dependency injection registrations.
- `Service`: account use cases, input validation and simulated Outbox processing; references only Domain.
- `Infra`: EF contexts, migrations, repositories, Unit of Work, Outbox persistence and ReadySet queries.
- `Domain.Tests`: CPF and account name value object unit tests.
- `Infra.Tests`: integration tests against disposable PostgreSQL and ReadySet containers, including HTTP tests of the actual API and service flow.

## Local setup

Requires the .NET 8 SDK pinned in `global.json`, Docker and Compose. From the repository root in Bash:

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
dotnet ef database update --context BankDbContext --project src/Infra --startup-project src/Application
docker compose up -d readyset
dotnet run --project src/Application
```

Apply migrations before starting the API. ReadySet creates deep caches automatically when EF queries reach it; the API does not register caches or wait for replication during startup. URL-reserved characters in credentials must be encoded in ReadySet's `UPSTREAM_DB_URL`.

Default ports: API `5080`, PostgreSQL `5432`, ReadySet `5433`, ReadySet metrics `6034`. Database/cache ports are configurable in `.env`; the API URL is configured in `launchSettings.json` or with `--urls`. Data uses Docker volumes. Images are PostgreSQL 16 and ReadySet `stable-260924`.

The account rename migration preserves existing rows and identifiers by renaming the table, column and indexes. Earlier migrations retain their original names and schema so existing databases can upgrade. Previously stored Outbox messages retain their historical payloads and event types.

## Application and Service

`Application` is the composition root. `Program.cs` registers Controllers, built-in Problem Details/exception handling and `AddApplication(configuration)`. All repository, service, EF, Outbox and ReadySet registrations are in `Application/Configuration`; Infra and Service contain no registration extensions.

Dependency direction: Application references Service and Infra; Service references only Domain; Infra references only Domain. `AccountService` implements `Domain.Interfaces.IAccountService` and depends on domain repository and Unit of Work contracts, without EF or HTTP dependencies. All custom interfaces live in `Domain/Interfaces`; implementations stay in their respective layers.

Both `ConnectionStrings:Postgres` and `ConnectionStrings:ReadySet` are required. Contexts receive their connection strings directly through DI; Npgsql manages connection pooling without a custom connection wrapper. Environment variable equivalents are included in `.env.example`. Repository, primary context, account service and Unit of Work share the HTTP request scope. Migrations remain an explicit setup step. EF tools use Application as the startup project to resolve the context through DI; no design-time context factory is needed.

### Account endpoints

| Method | Route | Success | Expected errors |
| --- | --- | --- | --- |
| POST | `/api/accounts` | 201 with body and Location | 400 invalid input; 409 duplicate CPF |
| GET | `/api/accounts/{id}` | 200 | 404 missing or inactive account |
| PUT | `/api/accounts/{id}` | 200 | 400 invalid input; 404 missing account; 409 duplicate CPF |
| DELETE | `/api/accounts/{id}` | 204 | 404 missing or inactive account |

Create and update accept:

```json
{
  "name": "Example account",
  "cpf": "287.841.420-90"
}
```

`Account.Name` is an immutable `AccountName` value object. `AccountName.Create()` rejects blank names and trims surrounding whitespace using `Result`, without validation exceptions. The service returns invalid input as HTTP 400. EF stores the value as text; HTTP responses and Outbox payloads expose it as a string. The service validates CPF using `Cpf.Create()` and uses FluentResults for expected validation, not-found and conflict outcomes. CPF uniqueness includes inactive accounts. Updates only check for a duplicate CPF when it differs from the loaded account's CPF. The database constraint remains the final concurrency safeguard; duplicate writes return 409 even if the pre-check races.

HTTP responses contain `id`, `name`, normalized `cpf` as a string, `isActive`, `createdAt` and `updatedAt`. `InternalId` remains private to persistence. `AccountResponse` is an HTTP contract; the repository still returns the Account entity directly.

Errors use Problem Details, including field errors for validation. Validation, not-found and conflict errors are typed FluentResults errors defined in `Domain/Errors`. Unexpected exceptions use the framework's built-in generic 500 response. Unit of Work translates the PostgreSQL CPF unique-constraint violation into `ConflictError`; Service propagates the failed result and the Controller maps it to HTTP 409 without depending on EF or PostgreSQL. HTTP responses do not expose exception internals. Edit requests are available in `src/Application/Application.http`.

GET uses ReadySet and is eventually consistent: immediately after POST, PUT or DELETE, a GET may briefly reflect the previous state. Create/update responses come from the primary write. Updates and deletion load the tracked entity from PostgreSQL.

## Persistence and Outbox

- `InternalId`: PostgreSQL-generated integer primary key.
- `Id`: domain GUID with a unique index.
- Normalized CPF: required, unique across active and inactive accounts.
- Timestamps: UTC (`timestamptz`); only Unit of Work sets `UpdatedAt`, on commit. Changing `IsActive` does not set a timestamp before persistence.
- `Remove(account)`: soft deletion. The row and CPF remain reserved; normal queries hide inactive accounts.

Repositories stage changes. Business writes go through `IUnitOfWork.CommitAsync()`, which returns `Result`, inspects tracked entities in one loop and saves changes and Outbox messages in one transaction. After failure, discard the Unit of Work and context; reuse is rejected.

New events are `account.created`, `account.updated` and `account.deleted`. A commit without changes creates no messages. `OutboxMessage(account, operation)` in Domain builds and serializes the public account event fields. Unit of Work adds the message and saves it with the account in one transaction. There is no separate factory, factory interface, payload class or mapper registration. EF mappings are centralized in `BankDbContext.ConfigureModel()`, shared by the primary and ReadySet contexts. Unit of Work and repository implementations remain separate.

`OutboxMessage` inherits `Entity`, using its `Id`, `InternalId`, `IsActive`, `CreatedAt` and `UpdatedAt`. The migration renames `event_id` to `id` and `occurred_at` to `created_at`, preserving existing identifiers and timestamps and keeping existing messages active. `outbox_messages` also stores `entity_id`, `type`, JSON `payload` and `published_at`. Account payload fields are `accountId`, `name`, `cpf`, `isActive`, `createdAt` and `updatedAt`; `InternalId` is excluded.

Changes to Outbox messages do not generate further Outbox messages. `OutboxWorker` runs inside the API and checks active messages with null `published_at` immediately on startup and then every 5 seconds, processing up to 100 per batch. The worker only schedules calls to `IOutboxService.ProcessPendingAsync()` and logs the processed count. `OutboxService` obtains tracked pending messages through `IOutboxRepository`, sets `PublishedAt` to simulate publication and commits through Unit of Work, which also updates `UpdatedAt`. `OutboxRepository` in Infra owns the EF query against PostgreSQL; the worker and Service have no direct DbContext access. Published and inactive messages are skipped; failed processing leaves messages pending for another attempt. This is a local simulation with no broker or delivery to other systems, intended for one API instance. Operations that bypass EF change tracking also bypass Outbox creation.

## ReadySet and Entity Framework Core

`AccountRepository` owns both reads and writes. `GetByIdAsync(id)` reads through `ReadySetDbContext` with `AsNoTracking()`. The same LINQ query runs without tracking against PostgreSQL when the cache is unavailable. `GetByIdAsync(id, cacheable: false)` loads the tracked entity directly from the primary `BankDbContext`; update and delete use this path. CPF checks and writes also use the primary. EF Core and Npgsql generate and parameterize the SQL. There is no separate query interface or implementation.

The deep cache replicates `public.accounts` through PostgreSQL logical replication. Changes appear asynchronously; reads requiring immediate consistency should use the primary repository.

Transient connection failures and timeouts trigger a warning and run the same LINQ query through the primary context. A missing cached row and caller cancellation do not trigger fallback.

ReadySet is configured in `compose.yaml`:

```yaml
CACHE_MODE: deep
QUERY_CACHING: in-request-path
```

The first eligible SELECT received from EF Core creates a deep cache natively. Subsequent reads reuse the parameterized cache for different IDs. New query shapes are considered automatically. No cache definitions, SQL extraction, startup service or PostgreSQL client are required in the application.

The pinned `stable-260924` image accepts `in-request-path` with hyphens. ReadySet's [configuration documentation](https://readyset.io/docs/reference/config-options#--query-caching) classifies this mode as experimental; `explicit` remains the non-experimental option. `CACHE_MODE: deep` retains logical replication instead of switching to TTL-based shallow caching.

For manual inspection:

```bash
docker compose exec postgres sh -c 'PGPASSWORD="$POSTGRES_PASSWORD" psql -h readyset -p 5433 -U "$POSTGRES_USER" -d "$POSTGRES_DB"'
```

```sql
SHOW READYSET STATUS;
SHOW CACHES;
```

The integration suite starts with no registered caches, executes the real EF query and verifies that ReadySet creates a deep cache and reuses it for another ID. It also verifies replicated updates, soft deletion and fallback. Administrative inspection uses the PostgreSQL client already present inside the test container.

Metrics: `http://localhost:6034/metrics` using the configured port.

```bash
docker compose logs --tail 100 readyset
docker compose down
```

`down` preserves volumes. Production migrations and replication permissions require deployment-specific configuration. After changing the cache configuration, recreate the ReadySet service with `docker compose up -d readyset`. Existing compatible caches can be reused; changed query shapes are considered automatically.

## Tests

```bash
dotnet test KrtBanking.sln
```

Integration tests start isolated PostgreSQL and ReadySet containers, apply migrations and remove containers on completion. They never use the `.env` database. Only Docker must be accessible to the test process; no host installation of `psql` is needed. Tests use the same native automatic deep-cache configuration as Compose.

Coverage includes HTTP CRUD, validation, CPF conflicts (including concurrent requests), response contracts, error responses, identifiers, CPF and timestamp materialization, soft deletion, batch persistence, Outbox atomicity, unchanged commits, automatic deep-cache creation and parameter reuse, replication, fallback and simulated Outbox processing.

Domain tests only:

```bash
dotnet test tests/Domain.Tests
```
