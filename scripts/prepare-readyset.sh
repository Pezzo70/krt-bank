#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

env_file="${1:-.env}"
if [[ ! -f "$env_file" ]]; then
  echo 'Crie .env a partir de .env.example ou informe um arquivo de ambiente existente.' >&2
  exit 1
fi

query() {
  docker compose --env-file "$env_file" exec -T postgres sh -c \
    'PGPASSWORD="$POSTGRES_PASSWORD" psql -X -v ON_ERROR_STOP=1 -A -t -F "|" -h readyset -p 5433 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"
}

ready=false
for ((attempt = 0; attempt < 120; attempt++)); do
  if status=$(query 'SHOW READYSET STATUS;' 2>/dev/null) && [[ "$status" == *'Replication Status|Running'* ]]; then
    ready=true
    break
  fi
  sleep 1
done

if [[ "$ready" != true ]]; then
  echo 'O snapshot do ReadySet não concluiu em 120 segundos. Confira os logs do serviço.' >&2
  exit 1
fi

caches=$(query 'SHOW CACHES;')
if [[ "$caches" != *contas_por_id* ]]; then
  query "$(cat scripts/readyset-cache.sql)"
fi
query 'SHOW CACHES;'
