// Shabpa support tickets: a tiny Cloudflare Worker with one KV namespace (binding TICKETS).
// Secrets: ADMIN_KEY (required), TELEGRAM_BOT_TOKEN + TELEGRAM_CHAT_ID (optional, to be told about new tickets).
// Nothing is stored beyond what the app sends: title, text, category, an optional contact, an optional diagnostics block,
// a random device id and the replies. A person (or the owner) can close and delete a ticket; deleting removes it for good.

const LIMITS = { title: 120, body: 4000, diag: 3000, contact: 100, reply: 2000 };
const STATUSES = ["open", "reviewing", "answered", "closed"];
const CATEGORIES = ["bug", "idea", "question", "game", "other"];
const PER_HOUR = 5;

const json = (data, status = 200) =>
  new Response(JSON.stringify(data), { status, headers: { "content-type": "application/json; charset=utf-8", "cache-control": "no-store" } });

const clip = (value, max) => (typeof value === "string" ? value.trim().slice(0, max) : "");

function safeEqual(a, b) {
  if (typeof a !== "string" || typeof b !== "string" || a.length !== b.length || a.length === 0) return false;
  let diff = 0;
  for (let i = 0; i < a.length; i++) diff |= a.charCodeAt(i) ^ b.charCodeAt(i);
  return diff === 0;
}

const isAdmin = (request, env) => safeEqual(request.headers.get("x-admin-key") ?? "", env.ADMIN_KEY ?? "");
const validDevice = (d) => typeof d === "string" && /^[0-9a-f]{32}$/.test(d);

function newId() {
  const bytes = new Uint8Array(5);
  crypto.getRandomValues(bytes);
  return [...bytes].map((b) => b.toString(16).padStart(2, "0")).join("");
}

async function readList(env, key) {
  return (await env.TICKETS.get(key, "json")) ?? [];
}

async function notify(env, ticket) {
  if (!env.TELEGRAM_BOT_TOKEN || !env.TELEGRAM_CHAT_ID) return;
  const text = "New Shabpa ticket " + ticket.id + " [" + ticket.category + "]\n" + ticket.title;
  try {
    await fetch("https://api.telegram.org/bot" + env.TELEGRAM_BOT_TOKEN + "/sendMessage", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ chat_id: env.TELEGRAM_CHAT_ID, text }),
    });
  } catch { /* a failed notification never fails the ticket */ }
}

async function create(request, env) {
  let input;
  try { input = await request.json(); } catch { return json({ error: "bad-json" }, 400); }
  if (!validDevice(input.device)) return json({ error: "bad-device" }, 400);
  const title = clip(input.title, LIMITS.title);
  const body = clip(input.body, LIMITS.body);
  if (title.length < 3 || body.length < 5) return json({ error: "too-short" }, 400);

  const hour = Math.floor(Date.now() / 3_600_000);
  const rateKey = "rl:" + input.device + ":" + hour;
  const used = Number((await env.TICKETS.get(rateKey)) ?? 0);
  if (used >= PER_HOUR) return json({ error: "rate-limited" }, 429);
  await env.TICKETS.put(rateKey, String(used + 1), { expirationTtl: 7200 });

  const ticket = {
    id: newId(),
    device: input.device,
    title,
    body,
    category: CATEGORIES.includes(input.category) ? input.category : "other",
    contact: clip(input.contact, LIMITS.contact),
    diag: clip(input.diag, LIMITS.diag),
    app: clip(input.app, 40),
    status: "open",
    created: new Date().toISOString(),
    replies: [],
  };
  await env.TICKETS.put("t:" + ticket.id, JSON.stringify(ticket));
  const mine = await readList(env, "d:" + ticket.device);
  await env.TICKETS.put("d:" + ticket.device, JSON.stringify([ticket.id, ...mine].slice(0, 50)));
  const all = await readList(env, "all");
  await env.TICKETS.put("all", JSON.stringify([ticket.id, ...all].slice(0, 500)));
  await notify(env, ticket);
  return json({ id: ticket.id, status: ticket.status });
}

async function load(env, ids) {
  const tickets = await Promise.all(ids.map((id) => env.TICKETS.get("t:" + id, "json")));
  return tickets.filter(Boolean);
}

// removes a ticket and its place in the device list and the owner's list
async function remove(env, ticket) {
  await env.TICKETS.delete("t:" + ticket.id);
  for (const key of ["d:" + ticket.device, "all"]) {
    const list = await readList(env, key);
    await env.TICKETS.put(key, JSON.stringify(list.filter((id) => id !== ticket.id)));
  }
}

const publicView = (t) => ({ id: t.id, title: t.title, body: t.body, category: t.category, status: t.status, created: t.created, replies: t.replies });

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    const path = url.pathname.replace(/\/+$/, "");

    if (request.method === "POST" && path === "/tickets") return create(request, env);

    if (request.method === "GET" && path === "/tickets") {
      const device = url.searchParams.get("device");
      if (!validDevice(device)) return json({ error: "bad-device" }, 400);
      const tickets = await load(env, await readList(env, "d:" + device));
      return json({ tickets: tickets.map(publicView) });
    }

    // a person closes or deletes only their own ticket: the request must carry the device id the ticket was made with
    const own = path.match(/^\/tickets\/([0-9a-f]{10})(\/close)?$/);
    if (own && (request.method === "DELETE" || (request.method === "POST" && own[2]))) {
      const device = url.searchParams.get("device");
      if (!validDevice(device)) return json({ error: "bad-device" }, 400);
      const ticket = await env.TICKETS.get("t:" + own[1], "json");
      if (!ticket || ticket.device !== device) return json({ error: "not-found" }, 404);
      if (request.method === "DELETE") {
        await remove(env, ticket);
        return json({ ok: true });
      }
      ticket.status = "closed";
      await env.TICKETS.put("t:" + ticket.id, JSON.stringify(ticket));
      return json({ ok: true, status: ticket.status });
    }

    if (path.startsWith("/admin/")) {
      if (!isAdmin(request, env)) return json({ error: "forbidden" }, 403);
      if (request.method === "GET" && path === "/admin/tickets") {
        const tickets = await load(env, await readList(env, "all"));
        return json({ tickets });
      }
      const gone = path.match(/^\/admin\/tickets\/([0-9a-f]{10})$/);
      if (request.method === "DELETE" && gone) {
        const ticket = await env.TICKETS.get("t:" + gone[1], "json");
        if (!ticket) return json({ error: "not-found" }, 404);
        await remove(env, ticket);
        return json({ ok: true });
      }
      const match = path.match(/^\/admin\/tickets\/([0-9a-f]{10})\/reply$/);
      if (request.method === "POST" && match) {
        const ticket = await env.TICKETS.get("t:" + match[1], "json");
        if (!ticket) return json({ error: "not-found" }, 404);
        let input;
        try { input = await request.json(); } catch { return json({ error: "bad-json" }, 400); }
        const text = clip(input.text, LIMITS.reply);
        if (text) ticket.replies.push({ text, at: new Date().toISOString(), admin: true });
        ticket.status = STATUSES.includes(input.status) ? input.status : text ? "answered" : ticket.status;
        await env.TICKETS.put("t:" + ticket.id, JSON.stringify(ticket));
        return json({ ok: true, status: ticket.status });
      }
    }
    return json({ error: "not-found" }, 404);
  },
};
