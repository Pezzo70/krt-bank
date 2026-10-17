CREATE DEEP CACHE contas_por_id FROM
SELECT id, nome, cpf, is_active, created_at, updated_at
FROM public.contas
WHERE id = $1 AND is_active = TRUE;
