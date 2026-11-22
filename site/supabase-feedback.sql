-- Crie esta tabela no Supabase (SQL Editor) para ativar a área de Feedback.
-- Projeto: https://uxuagcqifqkunibazuor.supabase.co

create table if not exists public.feedback (
  id bigint generated always as identity primary key,
  created_at timestamptz not null default now(),
  name text not null,
  avatar text,
  message text not null
);

alter table public.feedback enable row level security;

-- Permitir leitura pública dos comentários
create policy "feedback_read_public"
  on public.feedback
  for select
  to anon, authenticated
  using (true);

-- Permitir inserir comentários (área pública de dúvidas)
create policy "feedback_insert_public"
  on public.feedback
  for insert
  to anon, authenticated
  with check (
    length(trim(name)) between 1 and 40
    and length(trim(message)) between 1 and 500
  );
