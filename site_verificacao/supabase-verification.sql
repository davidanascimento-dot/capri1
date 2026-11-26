-- Verificação de idade para o Discord do Projeto_Capri
-- Rode este SQL no Supabase (SQL Editor) do projeto:
-- https://uxuagcqifqkunibazuor.supabase.co

create table if not exists public.discord_verifications (
  id bigint generated always as identity primary key,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  discord_id text not null unique,
  username text,
  global_name text,
  age_years integer not null check (age_years between 13 and 120),
  is_minor boolean not null,
  self_age integer,
  google_age integer,
  google_match boolean,
  guild_id text not null,
  role_applied text,
  role_error text,
  source text not null default 'site',
  ip_hash text
);

-- Migração para quem já criou a tabela antes da checagem com Google.
alter table public.discord_verifications
  add column if not exists self_age integer,
  add column if not exists google_age integer,
  add column if not exists google_match boolean;

create index if not exists discord_verifications_created_at_idx
  on public.discord_verifications (created_at desc);

create index if not exists discord_verifications_is_minor_idx
  on public.discord_verifications (is_minor);

alter table public.discord_verifications enable row level security;

-- Nenhuma leitura/escrita pública. O site usa a service role no servidor.
-- Se quiser um painel admin depois, crie políticas específicas para usuários autenticados.

create or replace function public.touch_discord_verifications_updated_at()
returns trigger
language plpgsql
as $$
begin
  new.updated_at = now();
  return new;
end;
$$;

drop trigger if exists discord_verifications_touch_updated_at on public.discord_verifications;
create trigger discord_verifications_touch_updated_at
  before update on public.discord_verifications
  for each row execute function public.touch_discord_verifications_updated_at();
