# Game login (Supabase Auth): setup

The game has a **Login** scene (the first scene in the build) where players can create an account, sign in,
sign out, or play as a guest. Players stay signed in between launches.

## How it works

- `CapstoneGame/Assets/Scripts/Auth/SupabaseAuth.cs` calls Supabase Auth's REST API (`/auth/v1/signup`,
  `/auth/v1/token`, `/auth/v1/logout`) with `UnityWebRequest`. There are no third-party packages, so it works
  the same on Android, iOS and in the editor.
- The session (access + refresh token) is saved in `PlayerPrefs` and refreshed automatically before it expires.
  If the phone is offline at launch, the player stays signed in and the token is refreshed later.
- Other scripts use `AuthServices.Client`: `IsSignedIn`, `Session.UserId`, and
  `await AuthServices.Client.GetAccessToken()` (for example to call the submit-score Edge Function).
- When a player signs up, the `on_auth_user_created` trigger in the migration creates their `profiles` row.

## Where the keys go (and which keys)

The game needs only two values: the **Project URL** and the **anon / publishable key**. These are meant to
be public, because row level security protects the data, but this repo is public too, so we still keep
them out of git.

- They go in `CapstoneGame/Assets/Resources/SupabaseSecrets.asset`, which is **gitignored**
  (`**/SupabaseSecrets*.asset`). Each teammate makes their own copy.
- **Never** use the `service_role` / `sb_secret_...` key in the game or anywhere in this repo. It bypasses
  every security rule. Only the submit-score Edge Function uses it, from Supabase's own secrets.

## One-time setup in the Supabase dashboard (project owner)

1. **Run the schema** if it isn't run yet: in **SQL Editor**, paste and run
   `supabase/migrations/20261105000000_initial_schema.sql` (or run `supabase db push` with the Supabase CLI).
2. **Email login**: under **Authentication > Sign In / Providers > Email**, check that Email is enabled.
   - **Confirm email** is on by default. Players then have to click the link in an email before they can
     sign in, and the game tells them so. Supabase's built-in email sender only allows a few emails per hour,
     so while the team is testing you can turn Confirm email **off** (sign-up then signs straight in). Turn it
     back on, or set up custom SMTP, before real players use it.
3. **Redirect after confirming**: under **Authentication > URL Configuration**, set **Site URL** to a page
   you control (for example this repo's GitHub page). The confirmation link opens that page after it confirms
   the account. The default `http://localhost:3000` shows a "can't connect" page even though the account
   *was* confirmed. The game doesn't use deep links, so you don't need any extra Redirect URLs.
4. **Copy the two values** for the next section:
   - Project URL: **Project Settings > Data API** (looks like `https://abcdefghijkl.supabase.co`).
   - Key: **Project Settings > API Keys**. Use the **publishable** key (`sb_publishable_...`) or the legacy
     **anon** key. Both work.

## Each teammate: add the keys in Unity

1. Open `CapstoneGame` in Unity Hub (editor 6000.3.24f1).
2. In the top menu choose **Tools > Supabase > Create or Select Secrets File**. This creates
   `Assets/Resources/SupabaseSecrets.asset` and selects it.
3. In the **Inspector** panel (right side), paste the **Url** and **Anon Key**.
4. Choose **File > Save Project** so the values are written to disk.
5. Check that git doesn't see the file: GitHub Desktop or `git status` should **not** list
   `SupabaseSecrets.asset`.

## Try it

1. In the **Project** panel, open `Assets/Scenes/Login` (double-click), then press **Play** (the ▶ at the top).
2. Type an email and a password (at least 6 characters) and press **Create account**.
3. If Confirm email is on, click the link in the email, then press **Sign in**.
4. You should see "Signed in as ...". Stop and press Play again, and you're still signed in. **Sign out** returns
   to the form. You can see the new user under **Authentication > Users** and their row in the `profiles` table.

Without a secrets file (for example in CI) the Login scene says online login isn't set up, and **Play as
guest** still works.

## Tests

- Edit Mode `SupabaseAuthTests`: sign up/in/out, refresh, offline behavior and error messages, using a fake network.
- Play Mode `LoginScreenTests`: the Login scene's buttons, screen switching, saved login, guest play, and the
  44 pt touch-target rule.

None of the tests contact Supabase, so they need no keys and run in GitHub Actions as they are.
