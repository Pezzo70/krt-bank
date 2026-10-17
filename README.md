# krt-bank

Solution C# em .NET 8 com domínio de contas e Infrastructure usando PostgreSQL, Entity Framework Core e ReadySet.

## Projetos

- `Domain`: conta, entidade base, CPF e contratos de persistência/consulta.
- `Infrastructure`: contexto EF, migrations, repositório, Unit of Work, Outbox e consultas pelo ReadySet.
- `Domain.Tests`: testes do CPF.
- `Infrastructure.Tests`: integração com PostgreSQL e ReadySet reais em containers descartáveis.

## Ambiente local

Requer SDK .NET 8 (versão em `global.json`) e Docker com Compose. Na raiz, em Bash:

```bash
cp -n .env.example .env
```

Ajuste `.env` antes de iniciar. Os valores do exemplo são para desenvolvimento local. Ao alterar banco, usuário, senha ou portas, ajuste também as duas connection strings. O `.env` é ignorado pelo Git.

```bash
set -a
source .env
set +a

dotnet tool restore
dotnet restore KrtBanking.sln
docker compose up -d --wait postgres
dotnet ef database update --project src/Infrastructure --startup-project src/Infrastructure
docker compose up -d readyset
bash scripts/prepare-readyset.sh
```

A migration deve ser aplicada antes de preparar o cache. O script espera o snapshot inicial e registra a consulta por GUID apenas se ela ainda não existir; pode ser executado novamente. A conta PostgreSQL do Compose possui as permissões locais necessárias à replicação e ao acompanhamento de DDL. Credenciais com caracteres reservados de URL exigem codificação no `UPSTREAM_DB_URL` do ReadySet.

Portas locais padrão: PostgreSQL `5432`, ReadySet `5433`, métricas do ReadySet `6034`. São configuráveis pelo `.env`. Os dados ficam em volumes Docker. PostgreSQL 16 e ReadySet `stable-260924` são as imagens utilizadas.

## Registro e uso

O futuro host carrega a configuração e registra:

```csharp
services.AddInfrastructure(configuration);
```

São exigidas `ConnectionStrings:Postgres` e `ConnectionStrings:ReadySet`, equivalentes às variáveis `ConnectionStrings__Postgres` e `ConnectionStrings__ReadySet` do exemplo. Repositório, contexto e Unit of Work compartilham o mesmo escopo.

```csharp
var conta = new Conta { Nome = "Conta exemplo", Cpf = cpfValidado };
contaRepository.Add(conta);
await unitOfWork.CommitAsync(cancellationToken);

// Carrega uma entidade rastreada diretamente no PostgreSQL para alterar.
var existente = await contaRepository.GetByIdAsync(conta.Id, cancellationToken);
if (existente is not null)
{
    existente.Nome = "Novo nome";
    await unitOfWork.CommitAsync(cancellationToken);
}

// Retorna um modelo imutável de consulta pelo ReadySet.
var consulta = await contaQueries.GetByIdAsync(conta.Id, cancellationToken);
```

`Cpf.Create()` retorna `Result<Cpf>`: valide o resultado antes de construir a conta. `required` exige inicialização no C#, mas não valida nome vazio. Validação da entrada e tradução de erros de banco para respostas HTTP pertencem à camada de aplicação/API futura.

## Persistência e Outbox

- `InternalId`: chave primária inteira, gerada pelo PostgreSQL.
- `Id`: GUID do domínio com índice único.
- CPF normalizado: obrigatório, com até 11 caracteres e único, incluindo contas inativas.
- Datas: UTC (`timestamptz`); `UpdatedAt` é preenchido no commit de alterações.
- `Remove(conta)` faz exclusão lógica. O registro e o CPF são preservados; as consultas normais ocultam contas inativas.

Os repositórios apenas registram alterações. As gravações de negócio passam por `IUnitOfWork.CommitAsync()`, que detecta as entidades alteradas em uma única iteração e salva os dados e as mensagens da Outbox na mesma transação. Depois de uma falha, descarte a operação e seu contexto; uma segunda tentativa na mesma Unit of Work é rejeitada.

Inclusões geram `conta.criada`; alterações geram `conta.atualizada`; a transição de ativa para inativa gera `conta.excluida`. Um commit sem mudanças não cria mensagens. A factory recebe mapeadores registrados por tipo; uma nova entidade precisa do seu mapeador e configuração EF, mantendo o processamento comum.

`outbox_messages` contém `event_id`, `entity_id` (GUID da conta), `type`, `occurred_at`, `payload` JSON e `published_at`. O payload usa camelCase e contém `contaId`, `nome`, `cpf`, `isActive`, `createdAt` e `updatedAt`, sem expor `InternalId`.

`published_at` permanece nulo nesta etapa: broker e processo de publicação ficam para a próxima implementação. Operações SQL diretas e atualizações em lote que ignoram o ChangeTracker também ignoram a criação de notificações.

## ReadySet

`IContaRepository` usa diretamente o PostgreSQL. `IContaQueries` usa SQL parametrizado pelo ReadySet e retorna `ContaConsulta`, sem rastreamento.

O **deep cache** da consulta de contas ativas por GUID recebe alterações por replicação lógica somente de `public.contas`. Há um pequeno atraso entre gravar no PostgreSQL e refletir a alteração no cache; não se mantém uma cópia congelada por 24 horas.

Falhas transitórias de conexão ou timeout do ReadySet geram um aviso e fallback para a mesma consulta no PostgreSQL. Resultado nulo e cancelamento solicitado não acionam fallback. Leituras que exigem dados imediatamente atualizados devem usar o PostgreSQL, como ocorre no repositório de edição.

### Inspecionar contas e Outbox

```bash
docker compose exec postgres sh -c 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB"'
```

```sql
SELECT internal_id, id, nome, cpf, is_active, created_at, updated_at FROM contas;
SELECT event_id, type, entity_id, payload, published_at FROM outbox_messages ORDER BY occurred_at;
```

### Confirmar uso do cache

```bash
docker compose exec postgres sh -c 'PGPASSWORD="$POSTGRES_PASSWORD" psql -h readyset -p 5433 -U "$POSTGRES_USER" -d "$POSTGRES_DB"'
```

Na mesma sessão, substitua o GUID por uma conta existente:

```sql
SHOW READYSET STATUS;
SHOW CACHES;
SELECT id, nome, cpf, is_active, created_at, updated_at
FROM public.contas
WHERE id = '00000000-0000-0000-0000-000000000000' AND is_active = TRUE;
EXPLAIN LAST STATEMENT;
```

`Query_destination = readyset(contas_por_id)` confirma atendimento pelo deep cache na versão fixada. `upstream` indica encaminhamento ao PostgreSQL. Os comandos administrativos são executados pelo `psql`, usando o protocolo simples; a aplicação usa Npgsql para o SELECT parametrizado. Métricas: `http://localhost:6034/metrics`, na porta configurada.

```bash
docker compose logs --tail 100 readyset
docker compose down
```

`down` preserva os volumes. A configuração acima é local; migrations e replicação em serviços gerenciados precisam de configuração de implantação própria.

## Testes

```bash
dotnet test KrtBanking.sln
```

Os testes de integração iniciam PostgreSQL e ReadySet em rede isolada, aplicam migrations e descartam os containers ao terminar. Não usam o banco do `.env`. O Docker precisa estar acessível ao processo dos testes.

A suíte verifica materialização de identidades/CPF/datas, alterações, exclusão lógica, CPF reservado, múltiplas contas, atomicidade da Outbox, commits sem alterações, cache real, replicação e fallback. Os testes do CPF são mantidos; não há novos testes unitários da `Entity`.

Para executar somente os testes de domínio:

```bash
dotnet test tests/Domain.Tests
```
