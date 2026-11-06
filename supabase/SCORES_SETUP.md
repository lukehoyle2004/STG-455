# Saving scores (submit-score Edge Function)

The game never writes to the `scores` table itself; row level security blocks that, so players can't cheat
by sending fake rows. Instead it calls the **submit-score** Edge Function, which:

1. checks the player's login token (guests can't submit),
2. checks the game is believable (`supabase/functions/submit-score/validate.ts`: whole numbers, wave ≥ 1,
   game ≤ 6 h, at most 5,000 points per second, played in the last 30 days and not in the future),
3. allows at most 10 games per player per minute,
4. stores the game. Each game has its own `client_game_id`, so a retry from the offline queue is never stored
   twice.

`get_leaderboard()` then ranks each player's best score.

## Deploy it (once, and again after changing the function)

You need Node.js, which is already installed on this PC. Run these from the repo folder:

```bash
npx supabase login
```

```bash
npx supabase link --project-ref YOUR_PROJECT_REF
```

```bash
npx supabase functions deploy submit-score --no-verify-jwt --use-api
```

- `login` opens the browser so you can sign in to Supabase.
- `YOUR_PROJECT_REF` is the part of the project URL before `.supabase.co`. If `link` asks for the database
  password, you can press Enter to skip it; deploying functions doesn't need it.
- `--no-verify-jwt` is intended: the function checks the player's token itself, and that works with both the
  legacy and the new API keys.
- `--use-api` bundles the function on Supabase's servers, so you don't need Docker.

The function gets `SUPABASE_URL` and the service key from Supabase automatically. **Don't** add the secret
key anywhere in the repo or the game.

To check it's live: **Edge Functions → submit-score** in the dashboard should show it, and its **Logs** tab
shows every call.

## Use it from the game

When a game ends:

```csharp
using CapstoneGame.Online;

ScoreServices.Submitter.Record(score, wave, secondsPlayed);
```

- If the player is signed in, the game is saved on the phone and sent right away, or the next time they're
  online.
- Guests are skipped; `Record` returns `false`.
- Games waiting to be sent are also sent after signing in on the Login screen.

## Tuning

Once real scoring exists, tighten `maxScorePerSecond` (and the others) in
`supabase/functions/submit-score/validate.ts`, then redeploy.

## Tests

- `supabase/functions/submit-score/validate.test.ts` runs in GitHub Actions ("Edge functions") with
  `deno test`. You can also run it locally with
  `node --test supabase/functions/submit-score/validate.test.ts`.
- Unity Edit Mode `ScoreSubmitterTests` covers the queue, offline retries, and which errors keep or drop a game.
