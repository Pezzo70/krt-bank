# krt-bank

## Stack usada

.NET 8, ASP.NET Core Controllers, EF Core, Npgsql, PostgreSQL 16, ReadySet, FluentResults, xUnit, Testcontainers e Docker Compose.

## Como iniciar

Requisitos: SDK .NET 8, Docker e Docker Compose.

```bash
cp -n .env.example .env
```

Ajuste as credenciais e connection strings no `.env`. Na raiz do projeto, usando Bash:

```bash
set -a
source .env
set +a

dotnet tool restore
dotnet restore
docker compose up -d --wait postgres
dotnet ef database update --project src/Infra --startup-project src/Application --context BankDbContext
docker compose up -d readyset
dotnet run --project src/Application
```

API: `http://localhost:5080`. Requisições: [Application.http](src/Application/Application.http).

Testes: `dotnet test`, com Docker ativo. São 81 casos; os testes de integração usam containers isolados.

## Resoluções dos problemas

### CRUD de contas

`POST`, `GET /{id}`, `PUT /{id}` e `DELETE /{id}` em `/api/accounts`.

A conta possui ID, nome, CPF e status. Value Objects validam nome e CPF. Um índice único impede CPF duplicado. A exclusão inativa a conta e mantém o CPF reservado.

### Eventos de criação, atualização e exclusão

Conta e evento são salvos na mesma transação usando Outbox. Os eventos são `account.created`, `account.updated` e `account.deleted`.

A cada 5 segundos, o worker chama o `OutboxService`, que busca até 100 pendências pelo `OutboxRepository` e preenche `PublishedAt`. Falhas permitem nova tentativa.

**Publicação simulada: não há broker nem envio para outros sistemas.**

### Consultas repetidas

O EF consulta o ReadySet, que cria deep caches automaticamente. Leituras atendidas pelo cache evitam SELECTs no PostgreSQL. A replicação lógica atualiza o cache de forma assíncrona, sem exigir expiração diária.

Escritas e leituras para alteração usam PostgreSQL. Falhas transitórias no ReadySet acionam fallback para o banco. O modo de cache automático utilizado é experimental.
