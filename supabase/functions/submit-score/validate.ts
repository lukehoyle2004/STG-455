// Checks a finished game sent by the client before it is stored.
// Kept free of Deno/Supabase imports so it can be unit-tested with `deno test` (or Node).

export interface ScoreSubmission {
  score: number;
  wave: number;
  duration_s: number;
  client_game_id: string;
  played_at: string;
}

export type ValidationResult =
  | { ok: true; value: ScoreSubmission }
  | { ok: false; error: string };

// Generous sanity limits. They stop obviously fake scores, not skilled players.
// Tighten maxScorePerSecond once the game's real scoring is known.
export const LIMITS = {
  maxScore: 10_000_000,
  maxWave: 10_000,
  maxDurationS: 6 * 60 * 60, // one game can't last more than 6 hours
  maxScorePerSecond: 5_000,
  maxAgeDays: 30, // offline games older than this are rejected
  maxClockSkewS: 5 * 60, // allow phones whose clock is a few minutes fast
};

export type Limits = typeof LIMITS;

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function isWholeNumber(v: unknown): v is number {
  return typeof v === "number" && Number.isInteger(v);
}

export function validateSubmission(
  body: unknown,
  now: Date = new Date(),
  limits: Limits = LIMITS,
): ValidationResult {
  if (typeof body !== "object" || body === null || Array.isArray(body)) {
    return { ok: false, error: "Body must be a JSON object." };
  }
  const b = body as Record<string, unknown>;

  if (!isWholeNumber(b.score) || b.score < 0 || b.score > limits.maxScore) {
    return { ok: false, error: `score must be a whole number from 0 to ${limits.maxScore}.` };
  }
  if (!isWholeNumber(b.wave) || b.wave < 1 || b.wave > limits.maxWave) {
    return { ok: false, error: `wave must be a whole number from 1 to ${limits.maxWave}.` };
  }
  if (!isWholeNumber(b.duration_s) || b.duration_s < 0 || b.duration_s > limits.maxDurationS) {
    return { ok: false, error: `duration_s must be a whole number from 0 to ${limits.maxDurationS}.` };
  }
  if (b.score > Math.max(b.duration_s, 1) * limits.maxScorePerSecond) {
    return { ok: false, error: "score is too high for the game length." };
  }
  if (typeof b.client_game_id !== "string" || !UUID.test(b.client_game_id)) {
    return { ok: false, error: "client_game_id must be a UUID." };
  }
  if (typeof b.played_at !== "string") {
    return { ok: false, error: "played_at must be an ISO 8601 date-time." };
  }
  const playedAt = new Date(b.played_at);
  if (Number.isNaN(playedAt.getTime())) {
    return { ok: false, error: "played_at must be an ISO 8601 date-time." };
  }
  if (playedAt.getTime() > now.getTime() + limits.maxClockSkewS * 1000) {
    return { ok: false, error: "played_at is in the future." };
  }
  if (playedAt.getTime() < now.getTime() - limits.maxAgeDays * 24 * 60 * 60 * 1000) {
    return { ok: false, error: `played_at is more than ${limits.maxAgeDays} days ago.` };
  }

  return {
    ok: true,
    value: {
      score: b.score,
      wave: b.wave,
      duration_s: b.duration_s,
      client_game_id: b.client_game_id.toLowerCase(),
      played_at: playedAt.toISOString(),
    },
  };
}
