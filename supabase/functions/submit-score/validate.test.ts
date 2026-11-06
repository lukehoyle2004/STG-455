// Run with `deno test supabase/functions` (CI) or `node --test supabase/functions/submit-score/validate.test.ts`.
import { test } from "node:test";
import assert from "node:assert/strict";
import { LIMITS, validateSubmission } from "./validate.ts";

const NOW = new Date("2026-11-06T12:00:00Z");

function game(overrides: Record<string, unknown> = {}) {
  return {
    score: 12_340,
    wave: 7,
    duration_s: 300,
    client_game_id: "3F2504E0-4F89-41D3-9A0C-0305E82C3301",
    played_at: "2026-11-06T11:55:00Z",
    ...overrides,
  };
}

function rejects(overrides: Record<string, unknown>, messagePart: string) {
  const result = validateSubmission(game(overrides), NOW);
  assert.equal(result.ok, false, `expected ${JSON.stringify(overrides)} to be rejected`);
  if (!result.ok) assert.match(result.error, new RegExp(messagePart));
}

test("accepts a normal game and normalizes id and date", () => {
  const result = validateSubmission(game(), NOW);
  assert.ok(result.ok);
  if (result.ok) {
    assert.equal(result.value.client_game_id, "3f2504e0-4f89-41d3-9a0c-0305e82c3301");
    assert.equal(result.value.played_at, "2026-11-06T11:55:00.000Z");
    assert.equal(result.value.score, 12_340);
  }
});

test("ignores extra fields such as user_id", () => {
  const result = validateSubmission(game({ user_id: "someone-else" }), NOW);
  assert.ok(result.ok);
  if (result.ok) assert.equal("user_id" in result.value, false);
});

test("rejects non-objects", () => {
  for (const body of [null, 5, "x", [game()]]) {
    assert.equal(validateSubmission(body, NOW).ok, false);
  }
});

test("rejects bad numbers", () => {
  rejects({ score: -1 }, "score");
  rejects({ score: 1.5 }, "score");
  rejects({ score: "100" }, "score");
  rejects({ score: LIMITS.maxScore + 1 }, "score");
  rejects({ wave: 0 }, "wave");
  rejects({ duration_s: -3 }, "duration_s");
  rejects({ duration_s: LIMITS.maxDurationS + 1 }, "duration_s");
});

test("rejects a score too high for the game length", () => {
  rejects({ score: 10 * LIMITS.maxScorePerSecond + 1, duration_s: 10 }, "too high");
  assert.ok(validateSubmission(game({ score: 10 * LIMITS.maxScorePerSecond, duration_s: 10 }), NOW).ok);
});

test("rejects bad ids and dates", () => {
  rejects({ client_game_id: "not-a-uuid" }, "UUID");
  rejects({ played_at: "yesterday" }, "ISO");
  rejects({ played_at: 123 }, "ISO");
  rejects({ played_at: "2026-11-06T12:30:00Z" }, "future");
  rejects({ played_at: "2026-09-01T12:00:00Z" }, "days ago");
});

test("allows a phone clock a little fast", () => {
  assert.ok(validateSubmission(game({ played_at: "2026-11-06T12:04:00Z" }), NOW).ok);
});
