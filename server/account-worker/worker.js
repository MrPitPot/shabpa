// Shabpa optional account: «log in with Telegram» and one small progress file per person.
// Nothing here is needed to use the app; the app works fully without it.
//
// Stored: an opaque id (HMAC of the Telegram id with a server secret, so a leaked database does not list Telegram ids),
// hashed login tokens, and the progress file (feathers, medals, counters). No names, no paths, no usage data.
//
// Secrets (never commit them): wrangler secret put TELEGRAM_BOT_TOKEN / WEBHOOK_SECRET / PEPPER

const LIMITS = { blob: 64 * 1024, loginTtl: 600, tokensPerUser: 5, startsPerHour: 20, pollsPerMinute: 40, putsPerHour: 60 };
const enc = new TextEncoder();
const json = (obj, status = 200, extra = {}) => new Response(JSON.stringify(obj), { status, headers: { "content-type": "application/json; charset=utf-8", "cache-control": "no-store", ...extra } });
const b64u = (bytes) => btoa(String.fromCharCode(...new Uint8Array(bytes))).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
const randomId = (n) => b64u(crypto.getRandomValues(new Uint8Array(n)));
const sha256 = async (s) => b64u(await crypto.subtle.digest("SHA-256", enc.encode(s)));
async function hmac(key, msg) {
  const k = await crypto.subtle.importKey("raw", enc.encode(key), { name: "HMAC", hash: "SHA-256" }, false, ["sign"]);
  return b64u(await crypto.subtle.sign("HMAC", k, enc.encode(msg)));
}
const codeOk = (c) => typeof c === "string" && /^[A-Za-z0-9_-]{16,32}$/.test(c);

// a small fixed-window counter in KV (good enough to stop a loop, not a precise limiter)
async function limit(env, name, ip, max, seconds) {
  const key = `rl:${name}:${ip}:${Math.floor(Date.now() / 1000 / seconds)}`;
  const n = parseInt((await env.ACCOUNTS.get(key)) || "0", 10);
  if (n >= max) return false;
  await env.ACCOUNTS.put(key, String(n + 1), { expirationTtl: Math.max(60, seconds * 2) });
  return true;
}

async function auth(request, env) {
  const h = request.headers.get("authorization") || "";
  const m = /^Bearer ([A-Za-z0-9_-]{32,64})$/.exec(h);
  if (!m) return null;
  const hash = await sha256(m[1]);
  const t = await env.ACCOUNTS.get("tok:" + hash, "json");
  return t ? { uid: t.uid, hash } : null;
}

async function telegramSend(env, chatId, text) {
  if (!env.TELEGRAM_BOT_TOKEN) return;
  try {
    await fetch(`https://api.telegram.org/bot${env.TELEGRAM_BOT_TOKEN}/sendMessage`, {
      method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify({ chat_id: chatId, text }),
    });
  } catch { /* the login still works; only the confirmation message is lost */ }
}

async function handleWebhook(request, env) {
  let update;
  try { update = await request.json(); } catch { return json({ ok: true }); }
  const msg = update && update.message;
  const text = msg && typeof msg.text === "string" ? msg.text : "";
  const m = /^\/start login_([A-Za-z0-9_-]{16,32})$/.exec(text.trim());
  if (!m || !msg.from || !msg.from.id) return json({ ok: true });
  const key = "login:" + m[1];
  const pending = await env.ACCOUNTS.get(key, "json");
  if (!pending || pending.uid) { await telegramSend(env, msg.chat.id, "این لینک ورود دیگه معتبر نیست. از داخل شبپا دوباره امتحان کن."); return json({ ok: true }); }
  const uid = await hmac(env.PEPPER, "tg:" + msg.from.id);
  await env.ACCOUNTS.put(key, JSON.stringify({ uid }), { expirationTtl: LIMITS.loginTtl });
  await telegramSend(env, msg.chat.id, "وصل شدی ✅ حالا به شبپا برگرد؛ پیشرفتت خودکار ذخیره می‌شه.");
  return json({ ok: true });
}

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    const p = url.pathname;
    const ip = request.headers.get("cf-connecting-ip") || "0";
    try {
      // Telegram → us (the secret is part of the path so only Telegram, who was told it, can call this)
      if (request.method === "POST" && env.WEBHOOK_SECRET && p === "/telegram/" + env.WEBHOOK_SECRET) return await handleWebhook(request, env);

      if (request.method === "POST" && p === "/v1/login/start") {
        if (!(await limit(env, "start", ip, LIMITS.startsPerHour, 3600))) return json({ error: "slow-down" }, 429);
        const code = randomId(18);
        await env.ACCOUNTS.put("login:" + code, JSON.stringify({}), { expirationTtl: LIMITS.loginTtl });
        return json({ code, url: `https://t.me/${env.BOT_USERNAME}?start=login_${code}`, expiresIn: LIMITS.loginTtl });
      }

      if (request.method === "GET" && p === "/v1/login/poll") {
        const code = url.searchParams.get("code");
        if (!codeOk(code)) return json({ error: "bad-code" }, 400);
        if (!(await limit(env, "poll", ip, LIMITS.pollsPerMinute, 60))) return json({ error: "slow-down" }, 429);
        const key = "login:" + code;
        const l = await env.ACCOUNTS.get(key, "json");
        if (!l) return json({ error: "expired" }, 410);
        if (!l.uid) return json({ status: "pending" }, 202);
        await env.ACCOUNTS.delete(key);                            // a login code is used once
        const token = randomId(32);
        const hash = await sha256(token);
        const list = (await env.ACCOUNTS.get("utok:" + l.uid, "json")) || [];
        list.push(hash);
        while (list.length > LIMITS.tokensPerUser) await env.ACCOUNTS.delete("tok:" + list.shift());
        await env.ACCOUNTS.put("utok:" + l.uid, JSON.stringify(list));
        await env.ACCOUNTS.put("tok:" + hash, JSON.stringify({ uid: l.uid, at: Date.now() }));
        return json({ status: "ok", token });
      }

      const who = await auth(request, env);
      if (!who) return json({ error: "unauthorized" }, 401);

      if (p === "/v1/save" && request.method === "GET") {
        const s = await env.ACCOUNTS.get("save:" + who.uid, "json");
        return s ? json(s) : json({ error: "none" }, 404);
      }

      if (p === "/v1/save" && request.method === "PUT") {
        if (!(await limit(env, "put", who.uid, LIMITS.putsPerHour, 3600))) return json({ error: "slow-down" }, 429);
        const raw = await request.text();
        if (raw.length > LIMITS.blob + 256) return json({ error: "too-big" }, 413);
        let body;
        try { body = JSON.parse(raw); } catch { return json({ error: "bad-json" }, 400); }
        if (typeof body.blob !== "string" || body.blob.length > LIMITS.blob || !Number.isInteger(body.rev) || body.rev < 0) return json({ error: "bad-request" }, 400);
        const cur = await env.ACCOUNTS.get("save:" + who.uid, "json");
        const curRev = cur ? cur.rev : 0;
        if (body.rev !== curRev) return json({ error: "conflict", ...cur }, 409);   // someone else (another PC) saved in between
        const next = { rev: curRev + 1, updated: new Date().toISOString(), blob: body.blob };
        await env.ACCOUNTS.put("save:" + who.uid, JSON.stringify(next));
        return json({ rev: next.rev, updated: next.updated });
      }

      if (p === "/v1/logout" && request.method === "POST") {
        await env.ACCOUNTS.delete("tok:" + who.hash);
        const list = ((await env.ACCOUNTS.get("utok:" + who.uid, "json")) || []).filter((h) => h !== who.hash);
        await env.ACCOUNTS.put("utok:" + who.uid, JSON.stringify(list));
        return json({ ok: true });
      }

      // delete everything we hold about this person
      if (p === "/v1/account" && request.method === "DELETE") {
        for (const h of (await env.ACCOUNTS.get("utok:" + who.uid, "json")) || []) await env.ACCOUNTS.delete("tok:" + h);
        await env.ACCOUNTS.delete("utok:" + who.uid);
        await env.ACCOUNTS.delete("save:" + who.uid);
        await env.ACCOUNTS.delete("tok:" + who.hash);
        return json({ ok: true });
      }

      return json({ error: "not-found" }, 404);
    } catch (e) {
      return json({ error: "server" }, 500);
    }
  },
};
