// submit-score: the only way a finished game gets into public.scores.
//
// The game calls   POST /functions/v1/submit-score
//   headers: apikey: <publishable key>, Authorization: Bearer <player's access token>
//   body:    { score, wave, duration_s, client_game_id, played_at }
//
// Replies: 201 saved, 200 duplicate (this game was already stored: safe to drop from the offline queue),
//          400 invalid, 401 not signed in, 409 id used by another player, 429 too many, 500 server error.
//
// It writes with the service role, which bypasses row level security, so every check happens here first.
// Deploy with JWT verification off (`supabase functions deploy submit-score --no-verify-jwt`):
// the function checks the player's token itself, which works with both legacy and new Supabase API keys.

import { createClient } from "npm:@supabase/supabase-js@2.49.4";
import { validateSubmission } from "./validate.ts";

const MAX_GAMES_PER_MINUTE = 10;

function json(status: number, body: Record<string, unknown>): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

function serviceKey(): string {
  // Older projects provide SUPABASE_SERVICE_ROLE_KEY; projects on the new API keys provide SUPABASE_SECRET_KEYS.
  const legacy = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY");
  if (legacy) return legacy;
  const keys = JSON.parse(Deno.env.get("SUPABASE_SECRET_KEYS") ?? "{}");
  return keys.default ?? Object.values(keys)[0] ?? "";
}

const admin = createClient(Deno.env.get("SUPABASE_URL") ?? "", serviceKey(), {
  auth: { persistSession: false, autoRefreshToken: false },
});

Deno.serve(async (req) => {
  if (req.method !== "POST") return json(405, { error: "method_not_allowed" });

  // 1. Who is this? The token must belong to a real, signed-in player.
  const token = (req.headers.get("Authorization") ?? "").replace(/^Bearer\s+/i, "").trim();
  if (!token) return json(401, { error: "not_signed_in" });

  const { data: userData, error: userError } = await admin.auth.getUser(token);
  if (userError || !userData?.user) return json(401, { error: "not_signed_in" });
  const userId = userData.user.id;

  // 2. Is the game believable?
  let body: unknown;
  try {
    body = await req.json();
  } catch {
    return json(400, { error: "invalid_json" });
  }
  const check = validateSubmission(body);
  if (!check.ok) return json(400, { error: "invalid_score", message: check.error });

  // 3. Simple rate limit, so one account can't flood the leaderboard.
  const since = new Date(Date.now() - 60_000).toISOString();
  const { count, error: countError } = await admin
    .from("scores")
    .select("id", { count: "exact", head: true })
    .eq("user_id", userId)
    .gte("created_at", since);
  if (countError) {
    console.error("rate limit query failed", countError);
    return json(500, { error: "server_error" });
  }
  if ((count ?? 0) >= MAX_GAMES_PER_MINUTE) return json(429, { error: "too_many_requests" });

  // 4. Store it. client_game_id is unique, so a retry from the offline queue can't store a game twice.
  const { error: insertError } = await admin.from("scores").insert({ user_id: userId, ...check.value });
  if (insertError) {
    if (insertError.code === "23505") {
      const { data: existing } = await admin
        .from("scores")
        .select("user_id")
        .eq("client_game_id", check.value.client_game_id)
        .maybeSingle();
      return existing?.user_id === userId
        ? json(200, { status: "duplicate" })
        : json(409, { error: "conflict" });
    }
    console.error("insert failed", insertError);
    return json(500, { error: "server_error" });
  }

  return json(201, { status: "saved" });
});
