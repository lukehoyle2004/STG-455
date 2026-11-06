-- Void Barrage: initial schema
-- Players, scores, and a public leaderboard.
-- Scores are written only by the submit-score Edge Function (service role),
-- never directly by the game client.

-- Player profiles: one row per auth user, created automatically on sign-up
create table public.profiles (
  id uuid primary key references auth.users on delete cascade,
  display_name text not null check (char_length(display_name) between 3 and 16),
  created_at timestamptz not null default now()
);

-- One row per finished game
create table public.scores (
  id bigint generated always as identity primary key,
  user_id uuid not null references auth.users on delete cascade,
  score integer not null check (score >= 0),
  wave integer not null check (wave >= 1),
  duration_s integer not null check (duration_s >= 0),
  client_game_id uuid not null unique,  -- stops offline-queue retries from storing a game twice
  played_at timestamptz not null,
  created_at timestamptz not null default now()
);

create index scores_user_id_idx on public.scores (user_id);
create index scores_score_idx on public.scores (score desc);

-- Row Level Security: players can only see and edit their own rows
alter table public.profiles enable row level security;
alter table public.scores enable row level security;

create policy "Players read own profile" on public.profiles
  for select to authenticated using ((select auth.uid()) = id);

create policy "Players update own profile" on public.profiles
  for update to authenticated
  using ((select auth.uid()) = id) with check ((select auth.uid()) = id);

create policy "Players read own scores" on public.scores
  for select to authenticated using ((select auth.uid()) = user_id);
-- No insert/update/delete policies on scores: only the service role can write them.

-- Create a profile automatically when someone signs up
create function public.handle_new_user()
returns trigger
language plpgsql
security definer set search_path = ''
as $$
begin
  insert into public.profiles (id, display_name)
  values (new.id, 'Pilot' || substr(replace(new.id::text, '-', ''), 1, 6));
  return new;
end;
$$;

create trigger on_auth_user_created
  after insert on auth.users
  for each row execute function public.handle_new_user();

-- Public leaderboard: exposes only display name, best score and wave
create function public.get_leaderboard(max_rows integer default 100)
returns table (rank bigint, display_name text, best_score integer, best_wave integer)
language sql
stable
security definer set search_path = ''
as $$
  select rank() over (order by b.best_score desc) as rank,
         p.display_name, b.best_score, b.best_wave
  from (
    select user_id, max(score) as best_score, max(wave) as best_wave
    from public.scores
    group by user_id
  ) b
  join public.profiles p on p.id = b.user_id
  order by b.best_score desc
  limit least(greatest(max_rows, 1), 100);
$$;

revoke execute on function public.get_leaderboard(integer) from public;
grant execute on function public.get_leaderboard(integer) to anon, authenticated;
revoke execute on function public.handle_new_user() from public, anon, authenticated;
