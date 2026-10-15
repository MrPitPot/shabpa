# Shabpa support server (optional)

A small Cloudflare Worker that receives support tickets from the app. The free plan is enough.

1. `npm i -g wrangler` and `wrangler login`
2. `wrangler kv namespace create TICKETS`, then paste the id into `wrangler.toml`
3. `wrangler secret put ADMIN_KEY` (a long random string; this is your admin key, never commit it)
4. Optional Telegram alert for new tickets: `wrangler secret put TELEGRAM_BOT_TOKEN` and `wrangler secret put TELEGRAM_CHAT_ID`
5. `wrangler deploy`, and note the `https://....workers.dev` address
6. Put that address in `SupportClient.DefaultEndpoint` (src/Shabpa/Core/Support.cs) and release, or paste it into the app under Support, Admin, "Server address" on your own PC.

In the app, enter the same ADMIN_KEY under Support, Admin to see and answer every ticket.

Stored per ticket: title, text, category, optional contact, optional diagnostics block, random device id, replies.
Limits: 5 tickets per device per hour; text sizes are capped (see `LIMITS` in worker.js).

Test the logic locally with `node test.mjs`.
