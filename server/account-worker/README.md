# Shabpa account server (optional)

A small Cloudflare Worker for the **optional** account: the person logs in with Telegram and their progress (feathers, medals, counters)
is kept in the cloud, so a Windows reinstall or a new PC does not wipe it. The app works completely without it; nobody is forced to sign up.

The free plan is enough. You need a **custom domain** (workers.dev is filtered in Iran), the same one planned for the shared bot relay.

## Set up
1. `npm i -g wrangler` and `wrangler login`
2. `wrangler kv namespace create ACCOUNTS`, paste the id into `wrangler.toml`
3. Secrets (never commit, never paste in chat):
   - `wrangler secret put TELEGRAM_BOT_TOKEN` (the bot's token from BotFather)
   - `wrangler secret put WEBHOOK_SECRET` (a long random string)
   - `wrangler secret put PEPPER` (another long random string; changing it later detaches every account)
4. `wrangler deploy`, then add your custom domain to the worker.
5. Tell Telegram where to send messages: open `https://api.telegram.org/bot<TOKEN>/setWebhook?url=https://YOUR-DOMAIN/telegram/<WEBHOOK_SECRET>`
   (if the same bot is also used for the relay, route both from one worker instead).
6. Put the domain in `AccountService.DefaultServer` (src/Shabpa/Core/Account.cs). Until then the app hides the whole feature.

## How it works
- `POST /v1/login/start` returns a one-time code and a `t.me` link. The app opens the link; in Telegram the person presses Start.
- Telegram calls the webhook (the secret is in the path), the worker links the code to an opaque id: an HMAC of the Telegram id with `PEPPER`, so the stored data does not list Telegram ids.
- The app polls `GET /v1/login/poll`, gets a random token once, and keeps it protected with Windows DPAPI. Only a hash of the token is stored here.
- `GET/PUT /v1/save` reads and writes one file (at most 64 KB) with a revision number. A write with an old revision gets `409` with the current file, so two PCs never overwrite each other silently; the app merges and retries.
- `POST /v1/logout` removes one token. `DELETE /v1/account` removes the save, every token and the id.

## What is stored
The opaque id, hashed tokens and the progress file. No name, no username, no paths, no usage history, no settings.
It is protected in transit by TLS and at rest by Cloudflare; it is **not** end-to-end encrypted (so a lost password cannot lock you out), which is fine because it only holds game-like progress.

Test the logic locally with `node test.mjs`.
