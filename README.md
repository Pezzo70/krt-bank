# krt-bank

.NET 8 Web API for the KRT bank onboarding challenge: manage customer accounts, record account lifecycle events and avoid repeated reads against the primary database.

## Run locally

Requires the .NET 8 SDK pinned in [global.json](global.json), Docker and Docker Compose. No PostgreSQL client installation on the host is required.

From the repository root in Bash:

```bash
cp -n .env.example .env
```

Edit `.env` with local credentials and ports. If these change, update both connection strings too. The example credentials are for local development, and `.env` is ignored by Git. URL-reserved credential characters must be encoded in ReadySet's `UPSTREAM_DB_URL`.

Load the environment, start PostgreSQL, apply migrations, start ReadySet and run the API:

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

Both `ConnectionStrings:Postgres` and `ConnectionStrings:ReadySet` must be configured. Apply migrations before starting the API. The API does not wait for cache warmup; ReadySet creates eligible caches when queries arrive.

| Service | Default address |
| --- | --- |
| API | `http://localhost:5080` |
| PostgreSQL | `localhost:5432` |
| ReadySet | `localhost:5433` |
| ReadySet metrics | `http://localhost:6034/metrics` |

Database/cache ports are configurable in `.env`; the API URL is in `launchSettings.json` or can be set with `--urls`. PostgreSQL and ReadySet data use Docker volumes.

## How the challenge is addressed

| Challenge problem | Implemented solution | Where to look |
| --- | --- | --- |
| Create, read, update and delete accounts with ID, holder name, CPF and status | ASP.NET Core Controllers, account services, domain validation and EF Core persistence in PostgreSQL. Deletion makes the account inactive. | [AccountsController](src/Application/Controllers/AccountsController.cs), [AccountService](src/Service/AccountService.cs) |
| Let other bank areas know when an account changes | A transactional Outbox records `account.created`, `account.updated` and `account.deleted`. A background worker simulates publication by marking pending messages as processed. | [UnitOfWork](src/Infra/Persistence/UnitOfWork.cs), [OutboxWorker](src/Application/Workers/OutboxWorker.cs), [OutboxService](src/Service/OutboxService.cs) |
| Avoid charging the primary database for repeated reads of the same account | ReadySet serves eligible account queries from a deep cache maintained through PostgreSQL logical replication. EF Core queries the cache using the PostgreSQL protocol. | [AccountRepository](src/Infra/Repositories/AccountRepository.cs), [compose.yaml](compose.yaml) |

The publication step is **simulated**: this project does not send events to a broker, fraud service or card service. The worker demonstrates how pending Outbox messages are processed without adding an external integration that the challenge does not specify.

## 1. Account CRUD and data integrity

An account has a public GUID `id`, holder `name`, normalized `cpf` and `isActive` status. Responses also include `createdAt` and `updatedAt`, in UTC. PostgreSQL uses a separate integer primary key, `InternalId`, which is not exposed by the API.

| Method | Route | Behavior | Responses |
| --- | --- | --- | --- |
| POST | `/api/accounts` | Create an active account | 201; 400 invalid input; 409 duplicate CPF |
| GET | `/api/accounts/{id}` | Read an active account through ReadySet | 200; 404 missing or inactive account |
| PUT | `/api/accounts/{id}` | Update holder name and CPF | 200; 400 invalid input; 404 missing account; 409 duplicate CPF |
| DELETE | `/api/accounts/{id}` | Set `IsActive` to false; preserve the row | 204; 404 missing or inactive account |

Create and update accept the same body:

```json
{
  "name": "Example account",
  "cpf": "287.841.420-90"
}
```

The domain protects the input rules:

- [AccountName](src/Domain/ValueObjects/AccountName.cs) rejects null, empty and whitespace-only names, and trims their outer whitespace.
- [Cpf](src/Domain/ValueObjects/Cpf.cs) validates format, repeated digits and check digits, accepting either 11 digits or the standard mask. Its stored value contains only digits.
- Both are immutable Value Objects. `Create()` and `Validate()` return FluentResults; expected invalid input is returned as an error rather than thrown as an exception.

`AccountService` coordinates these validations, account existence checks and CPF uniqueness checks. Updates only query for a duplicate CPF when the CPF changes. A unique database index is the final safeguard when concurrent requests pass the initial check. CPF remains reserved after soft deletion.

The Unit of Work translates a CPF unique-index violation into a typed `ConflictError`. The Controller maps domain errors to HTTP 400, 404 or 409 without referencing EF or PostgreSQL. Errors use Problem Details; unexpected exceptions receive the framework's generic 500 response.

Updates and deletion load the account directly from PostgreSQL with tracking, using `cacheable: false`. `UpdatedAt` is set by Unit of Work on commit. A commit with no changes produces no event and does not advance the timestamp.

## 2. Account events and the transactional Outbox

Saving an account and notifying other systems are separate operations. Recording the event in the **same transaction** as the account prevents a successful account change from losing its corresponding notification intent.

The write flow is:

```text
Controller -> AccountService -> AccountRepository -> UnitOfWork
                                                     |
                           PostgreSQL transaction: account + Outbox message
```

At commit time, Unit of Work inspects tracked entities, applies timestamps and soft deletion, and derives the event from the account change:

| Account change | Outbox event |
| --- | --- |
| New account | `account.created` |
| Changed account | `account.updated` |
| Account becomes inactive or is removed through the context | `account.deleted` |

[OutboxMessage](src/Domain/Entities/OutboxMessage.cs) builds the event type and JSON payload. The payload contains `accountId`, `name`, `cpf`, `isActive`, `createdAt` and `updatedAt`; it does not include `InternalId`. Account and message are saved by a single EF `SaveChangesAsync()` call. If saving fails, the transaction rolls back. The failed Unit of Work and context must then be discarded.

### Simulated publication

The API starts one [OutboxWorker](src/Application/Workers/OutboxWorker.cs). It runs immediately and then waits 5 seconds between processing cycles:

```text
OutboxWorker -> IOutboxService.ProcessPendingAsync()
                    |
                    +-> IOutboxRepository: fetch up to 100 pending messages
                    +-> set PublishedAt in UTC to simulate publication
                    +-> IUnitOfWork: persist the batch and update UpdatedAt
```

[OutboxRepository](src/Infra/Repositories/OutboxRepository.cs) queries PostgreSQL with EF, selecting active messages whose `PublishedAt` is null, ordered by creation time and internal ID. The worker and Service do not access DbContext directly.

After a successful batch, the worker logs `Simulated publication of ... Outbox messages`. Already processed and inactive messages are skipped. If processing fails, pending messages remain available for a later cycle. Updating Outbox messages does not generate more Outbox messages.

This simulator is intended for a single API instance. It does not claim delivery to another bank area or coordinate concurrent publishers. A real integration would publish to an agreed destination and mark the message only after confirmation; that integration is outside this implementation's scope.

## 3. Repeated account reads and ReadySet

The challenge describes several bank areas reading the same account repeatedly during the day. The solution puts ReadySet between the EF reader and PostgreSQL:

```text
GET /api/accounts/{id} -> AccountService -> AccountRepository
                                              |
                                              +-> ReadySet deep cache
                                              +-> PostgreSQL on transient cache failure

PostgreSQL account changes -> logical replication -> ReadySet
```

The repository uses `ReadySetDbContext` for ordinary reads and `BankDbContext` for writes, CPF checks and tracked reads needed by updates. Both use EF Core and Npgsql. Application code contains no handwritten business SQL.

ReadySet is configured in [compose.yaml](compose.yaml):

```yaml
CACHE_MODE: deep
QUERY_CACHING: in-request-path
REPLICATION_TABLES: public.accounts
```

When the EF account query first reaches ReadySet, it becomes a candidate for automatic deep-cache creation. The parameterized cache can then serve reads for different account IDs. Reads served by that cache do not execute the corresponding SELECT against PostgreSQL. PostgreSQL still receives writes and supplies the replication stream.

**No daily expiration is necessary.** The deep cache updates its results from replicated account changes instead of retaining an unchanged value until midnight or a fixed TTL. The challenge's repeated reads within the same day are covered by this reuse. The project verifies cache creation and reuse locally; it does not include an AWS cost benchmark.

There are two operational tradeoffs:

- **Eventual consistency:** immediately after creating, updating or deleting an account, GET may briefly return the previous state. Create/update responses use the primary write, and update/delete lookups always use PostgreSQL.
- **Availability:** transient connection failures and timeouts while reading ReadySet trigger a PostgreSQL fallback. A missing row or request cancellation does not trigger fallback.

The pinned image is `readysettech/readyset:stable-260924`, which accepts `in-request-path` with hyphens. ReadySet's [configuration documentation](https://readyset.io/docs/reference/config-options#--query-caching) classifies automatic query caching as experimental. The chosen mode is deep caching with logical replication, not TTL-based shallow caching.

## Architecture and design choices

| Project | Responsibility |
| --- | --- |
| `Domain` | Account and Outbox entities, CPF and account name Value Objects, typed errors and service/repository/Unit of Work interfaces. |
| `Service` | Account use cases and simulated Outbox processing. References only Domain. |
| `Infra` | EF contexts, mappings, migrations, repositories and Unit of Work. References only Domain. |
| `Application` | ASP.NET Core Web API with Controllers, HTTP contracts, worker and all dependency injection registrations. References Service and Infra. |
| `Domain.Tests` | Value Object unit tests. |
| `Infra.Tests` | HTTP, persistence, migration, ReadySet and Outbox integration tests. |

The structure applies the challenge's design expectations in concrete ways:

- **MVC:** Controllers handle HTTP input and output; account behavior is coordinated by services, with entities and Value Objects representing the domain.
- **DDD:** CPF and name rules belong to immutable Value Objects; entities hold identity and state. Lifecycle events are derived from EF-tracked changes at commit time.
- **SOLID:** services depend on domain interfaces, persistence details stay in Infra, and DI composes implementations in Application. The worker schedules execution while service and repository handle processing and persistence.
- **Clean Code and KISS:** mappings are centralized in `BankDbContext`, connection strings go directly to EF, and the implementation uses the framework's DI, logging and exception handling. The Outbox simulation uses one worker, one service and one repository.

EF migrations use Application as the startup project and obtain the context through DI. Mappings are shared by the primary and ReadySet contexts. Older migrations retain historical schema names; the rename migrations preserve existing rows, identifiers and Outbox payloads.

## Exercise the solution

[Application.http](src/Application/Application.http) contains create, read, update and delete requests. To create an account from a terminal:

```bash
curl -i http://localhost:5080/api/accounts \
  -H 'Content-Type: application/json' \
  -d '{"name":"Example account","cpf":"287.841.420-90"}'
```

Copy the returned ID into `accountId` in `Application.http`. Repeated GETs exercise the cache. PUT and DELETE produce additional Outbox events; within a processing cycle, the API console should show the simulated publication count. Reusing the CPF in another POST returns 409, including after soft deletion.

To inspect cache status, use the PostgreSQL client already available inside Docker:

```bash
docker compose exec postgres sh -c 'PGPASSWORD="$POSTGRES_PASSWORD" psql -h readyset -p 5433 -U "$POSTGRES_USER" -d "$POSTGRES_DB"'
```

Then run these ReadySet administrative commands:

```sql
SHOW READYSET STATUS;
SHOW CACHES;
```

`SHOW CACHES` shows the automatically created deep cache and its usage count. ReadySet logs can be viewed with `docker compose logs --tail 100 readyset`. Stop the database/cache containers with `docker compose down`; this preserves their volumes. Stop the API with Ctrl+C.

## Tests and evidence

With Docker running:

```bash
dotnet test KrtBanking.sln
```

The current suite has **81 passing test cases**: 55 Domain and 26 integration cases. The integration tests create disposable PostgreSQL and ReadySet containers, apply migrations and clean up on completion. They do not use or modify the local `.env` database.

| Behavior | Verification |
| --- | --- |
| Name and CPF validation | Unit tests for invalid input, normalization and equality. |
| HTTP CRUD and errors | Tests against the real API for success, validation, missing accounts, CPF conflicts and unexpected errors. |
| Concurrent CPF writes | Tests force stale pre-checks and verify the database conflict becomes HTTP 409. |
| Atomic account and Outbox persistence | Tests verify lifecycle messages, rollback, unchanged commits and soft deletion. |
| Migration compatibility | Tests upgrade and roll back an actual PostgreSQL database while preserving historical records. |
| Reduced origin reads | Tests verify automatic deep-cache creation and reuse for different IDs, replication and fallback. |
| Simulated Outbox processing | Tests verify batches of 100, remaining messages, empty batches, processed/inactive messages and worker shutdown. |

To run only the Value Object unit tests:

```bash
dotnet test tests/Domain.Tests
```
