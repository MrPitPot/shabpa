// Shabpa relay: one official Telegram bot shared by every Shabpa install.
// Telegram calls this Worker through a webhook; each app keeps a WebSocket open to its own Durable Object (DeviceHub).
// The relay only routes: the app parses commands, runs them and writes every reply itself, so the relay never runs
// anything on a PC beyond handing over the text the user typed.
// Secrets: TELEGRAM_BOT_TOKEN, WEBHOOK_SECRET, ADMIN_KEY. Bindings: DB (D1), HUB (Durable Object). Var: BOT_USERNAME.

const CODE_ALPHABET = "ABCDEFGHJKMNPQRSTUVWXYZ23456789"; // no 0/O, 1/I/L
const CODE_LENGTH = 8;
const CODE_TTL = 600;
const DAY = 86_400;
const LIMITS = {
  registerPerHour: 10, // per IP
  messagesPerMinute: 20, // per chat
  pairTriesPerHour: 10, // per chat
  codesPerHour: 20, // per device
  sendsPerHour: 120, // per device, replies and notifications together
  text: 4000,
  keyboard: 2000,
  label: 40,
};

const json = (data, status = 200) =>
  new Response(JSON.stringify(data), { status, headers: { "content-type": "application/json; charset=utf-8", "cache-control": "no-store" } });

const now = () => Math.floor(Date.now() / 1000);
const clip = (value, max) => (typeof value === "string" ? value.trim().slice(0, max) : "");

export function safeEqual(a, b) {
  if (typeof a !== "string" || typeof b !== "string" || a.length !== b.length || a.length === 0) return false;
  let diff = 0;
  for (let i = 0; i < a.length; i++) diff |= a.charCodeAt(i) ^ b.charCodeAt(i);
  return diff === 0;
}

export async function sha256(text) {
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(text));
  return [...new Uint8Array(digest)].map((b) => b.toString(16).padStart(2, "0")).join("");
}

function randomHex(bytes) {
  const buf = new Uint8Array(bytes);
  crypto.getRandomValues(buf);
  return [...buf].map((b) => b.toString(16).padStart(2, "0")).join("");
}

export function newCode() {
  for (;;) {
    let code = "";
    while (code.length < CODE_LENGTH) {
      const [b] = crypto.getRandomValues(new Uint8Array(1));
      if (b < 248) code += CODE_ALPHABET[b % CODE_ALPHABET.length]; // 248 = 8 * 31, so no bias
    }
    if (/[0-9]/.test(code)) return code; // a digit keeps codes apart from typed words like "RESTART"
  }
}

/** "k7qm-3xpd" or "K7QM 3XPD" -> "K7QM3XPD"; null if it cannot be a code. */
export function normCode(text) {
  if (typeof text !== "string" || text.length > 16) return null;
  const code = text.toUpperCase().replace(/[\s-]/g, "");
  if (code.length !== CODE_LENGTH || ![...code].every((c) => CODE_ALPHABET.includes(c))) return null;
  return /[0-9]/.test(code) ? code : null;
}

/** Counts one hit on key; true while the count stays within max. */
async function allow(env, key, max, ttl) {
  const row = await env.DB.prepare(
    "INSERT INTO hits (key, n, expires) VALUES (?, 1, ?) ON CONFLICT(key) DO UPDATE SET n = n + 1 RETURNING n",
  ).bind(key, now() + ttl).first();
  return (row?.n ?? 1) <= max;
}

const minute = () => Math.floor(now() / 60);
const hour = () => Math.floor(now() / 3600);

export async function telegram(env, method, body) {
  for (let attempt = 0; attempt < 2; attempt++) {
    try {
      const response = await fetch("https://api.telegram.org/bot" + env.TELEGRAM_BOT_TOKEN + "/" + method, {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify(body),
      });
      const data = await response.json().catch(() => ({}));
      if (response.status === 429 && attempt === 0) {
        await new Promise((r) => setTimeout(r, Math.min(3, data.parameters?.retry_after ?? 1) * 1000));
        continue;
      }
      return data;
    } catch {
      return { ok: false };
    }
  }
  return { ok: false };
}

const say = (env, chat, text, extra = {}) => telegram(env, "sendMessage", { chat_id: chat, text, ...extra });

// Texts the relay itself sends (everything else is written by the app in the user's language).
const fa = (from) => (from?.language_code ?? "fa").startsWith("fa");
const T = (from, faText, enText) => (fa(from) ? faText : enText);

async function authDevice(env, request) {
  const auth = request.headers.get("authorization") ?? "";
  const match = auth.match(/^Bearer ([0-9a-f]{32})\.([0-9a-f]{64})$/);
  if (!match) return null;
  const row = await env.DB.prepare("SELECT secret_hash FROM devices WHERE id = ?").bind(match[1]).first();
  if (!row || !safeEqual(await sha256(match[2]), row.secret_hash)) return null;
  return match[1];
}

const hub = (env, device) => env.HUB.get(env.HUB.idFromName(device));

/** Hands an event to the device's app; false if the app is not connected right now. */
async function deliver(env, device, event) {
  const response = await hub(env, device).fetch("https://hub/deliver", {
    method: "POST",
    headers: { "content-type": "application/json", "x-device": device },
    body: JSON.stringify(event),
  });
  return response.status === 200;
}

async function confirmedDevices(env, chat) {
  const { results } = await env.DB.prepare(
    "SELECT l.device_id AS id, d.label AS label, d.last_seen AS last_seen FROM links l JOIN devices d ON d.id = l.device_id WHERE l.chat_id = ? AND l.confirmed = 1 ORDER BY l.created",
  ).bind(chat).all();
  return results;
}

async function activeDevice(env, chat, devices) {
  if (devices.length === 0) return null;
  const row = await env.DB.prepare("SELECT active_device FROM chats WHERE chat_id = ?").bind(chat).first();
  return devices.find((d) => d.id === row?.active_device) ?? devices[devices.length - 1];
}

function ago(from, seconds) {
  const minutes = Math.max(1, Math.round(seconds / 60));
  if (minutes < 60) return T(from, `${minutes} دقیقه پیش`, `${minutes} min ago`);
  const hours = Math.round(minutes / 60);
  if (hours < 48) return T(from, `${hours} ساعت پیش`, `${hours} h ago`);
  return T(from, `${Math.round(hours / 24)} روز پیش`, `${Math.round(hours / 24)} days ago`);
}

async function pair(env, chat, from, code) {
  if (!(await allow(env, `pair:${chat}:${hour()}`, LIMITS.pairTriesPerHour, 7200)))
    return say(env, chat, T(from, "تلاش زیادی شد. یه ساعت دیگه دوباره امتحان کن.", "Too many tries. Try again in an hour."));

  const hash = await sha256(code);
  const row = await env.DB.prepare("SELECT device_id, expires FROM codes WHERE hash = ?").bind(hash).first();
  if (!row || row.expires < now())
    return say(env, chat, T(from, "این کد درست نیست یا تموم شده. توی شب‌پا یه کد تازه بگیر.", "That code is wrong or expired. Get a fresh one in Shabpa."));
  await env.DB.prepare("DELETE FROM codes WHERE hash = ?").bind(hash).run();

  const device = row.device_id;
  const existing = await env.DB.prepare("SELECT confirmed FROM links WHERE chat_id = ? AND device_id = ?").bind(chat, device).first();
  if (existing?.confirmed === 1) return say(env, chat, T(from, "این سیستم از قبل به همین حساب وصله.", "This PC is already linked to this account."));

  await env.DB.prepare(
    "INSERT INTO links (chat_id, device_id, confirmed, created) VALUES (?, ?, 0, ?) ON CONFLICT(chat_id, device_id) DO UPDATE SET confirmed = 0, created = excluded.created",
  ).bind(chat, device, now()).run();
  const sent = await deliver(env, device, {
    t: "pair",
    chat,
    name: clip(from?.first_name, 64),
    username: clip(from?.username, 64),
  });
  if (!sent) {
    await env.DB.prepare("DELETE FROM links WHERE chat_id = ? AND device_id = ? AND confirmed = 0").bind(chat, device).run();
    return say(env, chat, T(from, "سیستمت الان به سرور وصل نیست. شب‌پا رو باز کن و دوباره امتحان کن.", "Your PC is not connected right now. Open Shabpa and try again."));
  }
  return say(env, chat, T(from, "یه قدم مونده: روی سیستمت توی شب‌پا «اجازه بده» رو بزن.", "One step left: press “Allow” in Shabpa on your PC."));
}

async function listDevices(env, chat, from) {
  const devices = await confirmedDevices(env, chat);
  if (devices.length === 0) return say(env, chat, T(from, "هنوز سیستمی به این حساب وصل نیست.", "No PC is linked to this account yet."));
  const active = await activeDevice(env, chat, devices);
  const keyboard = devices.map((d) => [{ text: (d.id === active.id ? "● " : "") + (d.label || d.id.slice(0, 6)), callback_data: "dev:" + d.id }]);
  return say(env, chat, T(from, "کدوم سیستم رو کنترل کنم؟", "Which PC should I control?"), { reply_markup: { inline_keyboard: keyboard } });
}

async function unlink(env, chat, from) {
  const devices = await confirmedDevices(env, chat);
  const active = await activeDevice(env, chat, devices);
  if (!active) return say(env, chat, T(from, "هنوز سیستمی به این حساب وصل نیست.", "No PC is linked to this account yet."));
  await env.DB.prepare("DELETE FROM links WHERE chat_id = ? AND device_id = ?").bind(chat, active.id).run();
  await env.DB.prepare("UPDATE chats SET active_device = NULL WHERE chat_id = ? AND active_device = ?").bind(chat, active.id).run();
  await deliver(env, active.id, { t: "unlinked", chat });
  return say(env, chat, T(from, `اتصال «${active.label || "این سیستم"}» قطع شد.`, `“${active.label || "This PC"}” is unlinked.`), {
    reply_markup: { remove_keyboard: true },
  });
}

async function onCallback(env, query) {
  const chat = query.message?.chat?.id;
  const data = query.data ?? "";
  await telegram(env, "answerCallbackQuery", { callback_query_id: query.id });
  if (!chat || !data.startsWith("dev:")) return;
  const devices = await confirmedDevices(env, chat);
  const pick = devices.find((d) => d.id === data.slice(4));
  if (!pick) return;
  await env.DB.prepare("INSERT INTO chats (chat_id, active_device) VALUES (?, ?) ON CONFLICT(chat_id) DO UPDATE SET active_device = excluded.active_device")
    .bind(chat, pick.id).run();
  await say(env, chat, T(query.from, `از این به بعد دستورها به «${pick.label || pick.id.slice(0, 6)}» می‌رن.`, `Commands now go to “${pick.label || pick.id.slice(0, 6)}”.`));
}

export async function onUpdate(env, update) {
  if (update.callback_query) return onCallback(env, update.callback_query);
  const msg = update.message;
  if (!msg || typeof msg.text !== "string" || msg.chat?.type !== "private") return;
  const chat = msg.chat.id;
  const from = msg.from;
  if (!(await allow(env, `chat:${chat}:${minute()}`, LIMITS.messagesPerMinute, 120))) return;

  const text = msg.text.trim().slice(0, LIMITS.text);
  const word = text.startsWith("/") ? text.slice(1).split(/[\s@]/)[0].toLowerCase() : "";
  const devices = await confirmedDevices(env, chat);

  if (word === "start") {
    const code = normCode(text.replace(/^\/start(@\S+)?\s*/i, ""));
    if (code) return pair(env, chat, from, code);
    if (devices.length === 0)
      return say(env, chat, T(from,
        "سلام! من ربات شب‌پا هستم 🌙\nبرای وصل شدن، توی شب‌پا برو به «کنترل از راه دور» و دکمه‌ی «باز کردن در تلگرام» رو بزن، یا کد ۸ حرفی‌ای که اونجا می‌بینی رو همین‌جا بفرست.",
        "Hi! I am the Shabpa bot 🌙\nTo connect, open Remote control in Shabpa and press “Open in Telegram”, or send me the 8-character code shown there."));
  }
  if (word === "unlink") return unlink(env, chat, from);
  if (word === "devices") return listDevices(env, chat, from);

  // A code typed by hand. Once linked, only something with a digit counts, so plain words still reach the PC.
  const code = normCode(text);
  if (code && (devices.length === 0 || /[0-9]/.test(text))) return pair(env, chat, from, code);

  const active = await activeDevice(env, chat, devices);
  if (!active)
    return say(env, chat, T(from,
      "هنوز سیستمی به این حساب وصل نیست. توی شب‌پا برو به «کنترل از راه دور» و کد اتصال رو بفرست.",
      "No PC is linked to this account yet. Open Remote control in Shabpa and send the link code."));

  const sent = await deliver(env, active.id, { t: "msg", chat, text, date: msg.date, name: clip(from?.first_name, 64) });
  if (!sent)
    return say(env, chat, T(from,
      `«${active.label || "سیستمت"}» الان آنلاین نیست (آخرین بار: ${ago(from, now() - active.last_seen)}). دستور اجرا نشد و بعداً هم اجرا نمی‌شه.`,
      `“${active.label || "Your PC"}” is offline (last seen ${ago(from, now() - active.last_seen)}). The command did not run and will not run later.`));
}

async function register(request, env) {
  const ip = request.headers.get("cf-connecting-ip") ?? "unknown";
  const ipKey = (await sha256(ip)).slice(0, 16); // counted, never stored in the clear
  if (!(await allow(env, `reg:${ipKey}:${hour()}`, LIMITS.registerPerHour, 7200))) return json({ error: "rate-limited" }, 429);
  const device = randomHex(16);
  const secret = randomHex(32);
  await env.DB.prepare("INSERT INTO devices (id, secret_hash, label, created, last_seen) VALUES (?, ?, '', ?, ?)")
    .bind(device, await sha256(secret), now(), now()).run();
  return json({ device, secret });
}

async function setup(request, env) {
  const origin = new URL(request.url).origin;
  const webhook = await telegram(env, "setWebhook", {
    url: origin + "/tg/webhook",
    secret_token: env.WEBHOOK_SECRET,
    allowed_updates: ["message", "callback_query"],
    drop_pending_updates: true,
  });
  const commands = [
    ["status", "وضعیت سیستم"], ["sleep", "خواب"], ["shutdown", "خاموش"], ["restart", "ری‌استارت"], ["lock", "قفل صفحه"],
    ["cancel", "لغو شمارش معکوس"], ["update", "به‌روزرسانی شب‌پا"], ["devices", "انتخاب سیستم"], ["unlink", "قطع اتصال این سیستم"],
  ];
  const menu = await telegram(env, "setMyCommands", { commands: commands.map(([command, description]) => ({ command, description })) });
  return json({ webhook, menu });
}

export async function cleanup(env) {
  const t = now();
  await env.DB.prepare("DELETE FROM codes WHERE expires < ?").bind(t).run();
  await env.DB.prepare("DELETE FROM hits WHERE expires < ?").bind(t).run();
  await env.DB.prepare("DELETE FROM links WHERE confirmed = 0 AND created < ?").bind(t - 3600).run();
  await env.DB.prepare("DELETE FROM devices WHERE last_seen < ?").bind(t - 90 * DAY).run();
  await env.DB.prepare("DELETE FROM links WHERE device_id NOT IN (SELECT id FROM devices)").run();
  await env.DB.prepare("DELETE FROM chats WHERE chat_id NOT IN (SELECT chat_id FROM links)").run();
}

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    const path = url.pathname.replace(/\/+$/, "");

    if (request.method === "POST" && path === "/tg/webhook") {
      if (!safeEqual(request.headers.get("x-telegram-bot-api-secret-token") ?? "", env.WEBHOOK_SECRET ?? "")) return json({ error: "forbidden" }, 403);
      let update;
      try { update = await request.json(); } catch { return json({ ok: true }); }
      try { await onUpdate(env, update); } catch (e) { console.log("update failed: " + e); }
      return json({ ok: true }); // always 200, or Telegram keeps re-sending the same update
    }

    if (request.method === "POST" && path === "/v1/devices") return register(request, env);

    if (path === "/v1/connect") {
      if (request.headers.get("upgrade")?.toLowerCase() !== "websocket") return json({ error: "websocket-only" }, 426);
      const device = await authDevice(env, request);
      if (!device) return json({ error: "unauthorized" }, 401);
      const headers = new Headers(request.headers);
      headers.set("x-device", device);
      return hub(env, device).fetch("https://hub/connect", { headers });
    }

    if (request.method === "POST" && path === "/admin/setup") {
      if (!safeEqual(request.headers.get("x-admin-key") ?? "", env.ADMIN_KEY ?? "")) return json({ error: "forbidden" }, 403);
      return setup(request, env);
    }

    if (path === "" || path === "/health") return json({ ok: true, service: "shabpa-relay" });
    return json({ error: "not-found" }, 404);
  },

  async scheduled(_event, env) {
    await cleanup(env);
  },
};

/** Holds one device's WebSocket. Hibernates between messages, so an idle connection costs nothing. */
export class DeviceHub {
  constructor(ctx, env) {
    this.ctx = ctx;
    this.env = env;
    // Keep-alive pings are answered without waking the object.
    if (typeof WebSocketRequestResponsePair !== "undefined") ctx.setWebSocketAutoResponse(new WebSocketRequestResponsePair("ping", "pong"));
  }

  upgradeResponse(client) {
    return new Response(null, { status: 101, webSocket: client });
  }

  async fetch(request) {
    const url = new URL(request.url);
    const device = request.headers.get("x-device") ?? "";
    if (url.pathname === "/connect") {
      // A newer connection from the same device replaces any older one (for example after a network change).
      for (const old of this.ctx.getWebSockets()) {
        try { old.close(4000, "replaced"); } catch { /* already gone */ }
      }
      const [client, server] = Object.values(new WebSocketPair());
      this.ctx.acceptWebSocket(server);
      server.serializeAttachment({ device });
      await this.seen(device);
      return this.upgradeResponse(client);
    }
    if (url.pathname === "/deliver") {
      const sockets = this.ctx.getWebSockets();
      if (sockets.length === 0) return json({ error: "offline" }, 409);
      const body = await request.text();
      for (const ws of sockets) {
        try { ws.send(body); } catch { /* closing */ }
      }
      return json({ ok: true });
    }
    return json({ error: "not-found" }, 404);
  }

  async seen(device) {
    await this.env.DB.prepare("UPDATE devices SET last_seen = ? WHERE id = ?").bind(now(), device).run();
  }

  async linkedChats(device) {
    const { results } = await this.env.DB.prepare("SELECT chat_id FROM links WHERE device_id = ? AND confirmed = 1").bind(device).all();
    return results.map((r) => r.chat_id);
  }

  async ready(ws, device) {
    ws.send(JSON.stringify({ t: "ready", bot: this.env.BOT_USERNAME ?? "", chats: await this.linkedChats(device) }));
  }

  async webSocketMessage(ws, raw) {
    const device = ws.deserializeAttachment()?.device;
    if (!device || typeof raw !== "string" || raw.length > 8000) return;
    let m;
    try { m = JSON.parse(raw); } catch { return; }
    const env = this.env;
    const reply = (data) => ws.send(JSON.stringify(data));
    const chat = Number.isSafeInteger(m.chat) ? m.chat : 0;
    const linked = async () =>
      chat !== 0 && (await env.DB.prepare("SELECT 1 AS ok FROM links WHERE chat_id = ? AND device_id = ? AND confirmed = 1").bind(chat, device).first()) !== null;
    const budget = () => allow(env, `send:${device}:${hour()}`, LIMITS.sendsPerHour, 7200);

    switch (m.t) {
      case "hello": {
        await env.DB.prepare("UPDATE devices SET label = ?, last_seen = ? WHERE id = ?").bind(clip(m.label, LIMITS.label), now(), device).run();
        return this.ready(ws, device);
      }
      case "code": {
        if (!(await allow(env, `code:${device}:${hour()}`, LIMITS.codesPerHour, 7200))) return reply({ t: "error", error: "rate-limited" });
        const code = newCode();
        await env.DB.prepare("DELETE FROM codes WHERE device_id = ?").bind(device).run();
        await env.DB.prepare("INSERT INTO codes (hash, device_id, expires) VALUES (?, ?, ?)").bind(await sha256(code), device, now() + CODE_TTL).run();
        return reply({ t: "code", code, ttl: CODE_TTL, bot: env.BOT_USERNAME ?? "" });
      }
      case "pair-ok": {
        const { meta } = await env.DB.prepare("UPDATE links SET confirmed = 1 WHERE chat_id = ? AND device_id = ? AND confirmed = 0").bind(chat, device).run();
        if (meta.changes === 0) return reply({ t: "error", error: "no-pending-link" });
        await env.DB.prepare("INSERT INTO chats (chat_id, active_device) VALUES (?, ?) ON CONFLICT(chat_id) DO UPDATE SET active_device = excluded.active_device")
          .bind(chat, device).run();
        // The welcome rides on this message: a separate "send" could overtake the update above.
        await this.sendTo(chat, m);
        return this.ready(ws, device);
      }
      case "pair-no": {
        const { meta } = await env.DB.prepare("DELETE FROM links WHERE chat_id = ? AND device_id = ? AND confirmed = 0").bind(chat, device).run();
        if (meta.changes > 0 && m.text) await say(env, chat, clip(m.text, LIMITS.text));
        return;
      }
      case "send": {
        // A device may only write to chats that chose to control it; this keeps the bot from being used to spam anyone else.
        if (!(await linked())) return reply({ t: "error", error: "not-linked" });
        if (!(await budget())) return reply({ t: "error", error: "rate-limited" });
        return this.sendTo(chat, m);
      }
      case "notify": {
        const text = clip(m.text, LIMITS.text);
        if (!text || !(await budget())) return;
        const row = await env.DB.prepare("SELECT label FROM devices WHERE id = ?").bind(device).first();
        for (const target of await this.linkedChats(device)) {
          // Someone with several PCs needs to know which one is talking.
          const { results } = await env.DB.prepare("SELECT COUNT(*) AS n FROM links WHERE chat_id = ? AND confirmed = 1").bind(target).all();
          const prefix = results[0].n > 1 && row?.label ? `[${row.label}] ` : "";
          await say(env, target, prefix + text);
        }
        return;
      }
      case "unlink": {
        if (!(await linked())) return this.ready(ws, device);
        if (m.text) await say(env, chat, clip(m.text, LIMITS.text), { reply_markup: { remove_keyboard: true } });
        await env.DB.prepare("DELETE FROM links WHERE chat_id = ? AND device_id = ?").bind(chat, device).run();
        await env.DB.prepare("UPDATE chats SET active_device = NULL WHERE chat_id = ? AND active_device = ?").bind(chat, device).run();
        return this.ready(ws, device);
      }
      case "forget": {
        // The user removed the shared bot from this PC: drop everything the server knows about it.
        await env.DB.prepare("DELETE FROM links WHERE device_id = ?").bind(device).run();
        await env.DB.prepare("DELETE FROM codes WHERE device_id = ?").bind(device).run();
        await env.DB.prepare("DELETE FROM devices WHERE id = ?").bind(device).run();
        reply({ t: "forgotten" });
        ws.close(1000, "forgotten");
        return;
      }
    }
  }

  async sendTo(chat, m) {
    const text = clip(m.text, LIMITS.text);
    if (!text) return;
    const extra = {};
    if (typeof m.keyboard === "string" && m.keyboard.length <= LIMITS.keyboard) {
      try { extra.reply_markup = JSON.parse(m.keyboard); } catch { /* sent without one */ }
    }
    await say(this.env, chat, text, extra);
  }

  async webSocketClose(ws) {
    const device = ws.deserializeAttachment()?.device;
    if (device) await this.seen(device);
  }

  async webSocketError(ws) {
    await this.webSocketClose(ws);
  }
}
