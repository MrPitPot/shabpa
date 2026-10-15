# Shabpa relay (the shared Telegram bot)

One official bot for everyone. The bot token lives only here, as a Cloudflare secret. Each Shabpa install keeps a
WebSocket open to its own Durable Object; Telegram reaches the Worker through a webhook. Users link their Telegram
account with a one-time code and then approve it on the PC, so nobody has to create a bot or handle a token.

The relay only routes text. The app parses commands and writes every reply itself, and it only ever runs its fixed
list of commands (status, sleep, hibernate, shut down, restart, lock, cancel, update from GitHub Releases).
Keep it that way: never add a command that runs arbitrary programs or downloads from an address the server sends.

## What the server stores
| Data | Why | Kept |
|---|---|---|
| Random device id, SHA-256 of the device secret, the PC's name | Authenticate the app, label PCs when one account has several | Until removed, or 90 days without connecting |
| Telegram chat id per linked device | Route commands and replies | Until unlinked |
| SHA-256 of pairing codes | Linking | 10 minutes |
| Rate-limit counters (IPs only as a truncated hash) | Abuse limits | 1 to 2 hours |

Not stored: message text, replies, Telegram names or usernames, IP addresses. Message text does pass through the
Worker (and Telegram), so this is not end-to-end encrypted; people who want that can still use their own bot.

## Setup (once)
1. Create the bot in Telegram with @BotFather (`/newbot`). Keep the token to yourself: don't paste it in chats, issues or code.
   If it ever leaks, send `/revoke` to @BotFather and store the new one with step 5.
2. `npm i -g wrangler` and `wrangler login`
3. `wrangler d1 create shabpa-relay`, paste the id into `wrangler.toml`, and set `BOT_USERNAME` there (without @).
4. `wrangler d1 execute shabpa-relay --remote --file=schema.sql`
5. Secrets (each command asks for the value, so it never lands in your shell history):
   - `wrangler secret put TELEGRAM_BOT_TOKEN`
   - `wrangler secret put WEBHOOK_SECRET` (a long random string of letters, digits, `_` and `-`)
   - `wrangler secret put ADMIN_KEY` (another long random string)
6. `wrangler deploy`. A custom domain (Workers, Settings, Domains & Routes) is strongly recommended: `*.workers.dev` is often filtered in Iran.
7. Point the bot at the Worker: `curl -X POST -H "x-admin-key: <ADMIN_KEY>" https://<your-relay>/admin/setup`
   (sets the webhook with the secret header, and the bot's command menu).
8. Put the address in `TelegramBot.DefaultRelay` (src/Shabpa/Core/TelegramBot.cs) and release. Until then the app keeps
   the personal-bot setup only.

## Limits
- Pairing: 8-character codes from a 31-letter alphabet, one-time, 10 minutes; 10 tries per chat per hour; approval on the PC.
- 20 messages per chat per minute; 120 replies and notifications per device per hour; a device can only write to chats linked to it.
- 10 device registrations per IP per hour.
- Free plan: about 100,000 requests a day, which is roughly 3,000 active users. Idle WebSockets hibernate and the
  30-second keep-alive pings are answered without waking the Durable Object.

## Protocol (app ⇄ relay, JSON over `wss://<relay>/v1/connect`, header `Authorization: Bearer <device>.<secret>`)
App to relay: `hello {label}`, `code`, `pair-ok {chat, text, keyboard}`, `pair-no {chat, text}`, `send {chat, text, keyboard}`,
`notify {text}`, `unlink {chat, text}`, `forget`, and the plain text `ping`.
Relay to app: `ready {bot, chats}`, `code {code, ttl, bot}`, `pair {chat, name, username}`, `msg {chat, text, date, name}`,
`unlinked {chat}`, `forgotten`, `error {error}`, and `pong`.
`POST /v1/devices` registers a new install and returns `{device, secret}`.

## Tests
`node test.mjs` (Node 22.5+) runs the whole flow against in-memory SQLite and fake sockets.
`wrangler dev` runs it for real locally; apply the schema with `wrangler d1 execute shabpa-relay --local --file=schema.sql` first.
