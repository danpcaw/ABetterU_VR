-- DRAFT: not applied. Review before running on the ABetterU project.
-- Additive only: creates one new table, touches no existing table.
-- Records what the user SAYS hurts. No diagnosis, no severity scoring, no medical advice.

create table public.user_injuries (
  id          uuid primary key default gen_random_uuid(),
  user_id     uuid not null references auth.users(id) on delete cascade,
  body_area   text not null check (body_area in (
                'neck','shoulder','upper_back','lower_back','chest','elbow','wrist_hand',
                'hip','glute','hamstring','quad','knee','calf_shin','ankle_foot','other')),
  side        text check (side in ('left','right','both')),
  note        text check (char_length(note) <= 280),
  status      text not null default 'active' check (status in ('active','resolved')),
  reported_at timestamptz not null default now(),
  resolved_at timestamptz,
  source      text not null default 'manual' check (source in ('manual','vr','ai_assistant'))
);

create index user_injuries_user_status_idx on public.user_injuries (user_id, status);

alter table public.user_injuries enable row level security;

-- Own rows only, same pattern as food_log_entries / strength_sets.
create policy "own injuries: select" on public.user_injuries for select to authenticated using (user_id = auth.uid());
create policy "own injuries: insert" on public.user_injuries for insert to authenticated with check (user_id = auth.uid());
create policy "own injuries: update" on public.user_injuries for update to authenticated using (user_id = auth.uid()) with check (user_id = auth.uid());
create policy "own injuries: delete" on public.user_injuries for delete to authenticated using (user_id = auth.uid());
