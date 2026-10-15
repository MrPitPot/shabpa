// Runs the relay against an in-memory SQLite (standing in for D1), fake WebSockets and a fake Telegram.
// node test.mjs   (Node 22.5 or newer)
import { DatabaseSync } from "node:sqlite";
import { readFileSync } from "node:fs";
import assert from "node:assert/strict";
import worker, { DeviceHub, normCode, newCode, cleanup } from "./worker.js";

const db = new DatabaseSync(":memory:");
db.exec(readFileSync(new URL("./schema.sql", import.meta.url), "utf8"));
const D1 = {
  prepare(sql) {
    let args = [];
    const st = {
      bind(...a) { args = a; return st; },
      async first() { return db.prepare(sql).get(...args) ?? null; },
      async all() { return { results: db.prepare(sql).all(...args) }; },
      async run() { const r = db.prepare(sql).run(...args); return { meta: { changes: Number(r.changes) } }; },
    };
    return st;
  },
};

class FakeSocket {
  constructor() { this.inbox = []; this.open = true; }
  send(s) { this.inbox.push(JSON.parse(s)); }
  close() { this.open = false; }
  serializeAttachment(a) { this.att = a; }
  deserializeAttachment() { return this.att; }
  take() { const all = this.inbox; this.inbox = []; return all; }
}
globalThis.WebSocketPair = function () { return { 0: {}, 1: new FakeSocket() }; };
DeviceHub.prototype.upgradeResponse = () => new Response("upgraded");

const hubs = new Map();
const hubFor = (name) => {
  if (!hubs.has(name)) {
    const sockets = [];
    const ctx = {
      getWebSockets: () => sockets.filter((s) => s.open),
      acceptWebSocket: (s) => sockets.push(s),
    };
    hubs.set(name, new DeviceHub(ctx, env));
  }
  return hubs.get(name);
};
const env = {
  DB: D1,
  TELEGRAM_BOT_TOKEN: "123:fake",
  WEBHOOK_SECRET: "hook-secret",
  ADMIN_KEY: "admin-key",
  BOT_USERNAME: "ShabpaBot",
  HUB: {
    idFromName: (n) => n,
    get: (n) => ({ fetch: (url, init) => hubFor(n).fetch(new Request(url, init)) }),
  },
};

const sent = [];
globalThis.fetch = async (url, init) => {
  const method = String(url).split("/").pop();
  sent.push({ method, body: JSON.parse(init.body) });
  return Response.json({ ok: true, result: {} });
};
const said = (chat) => sent.filter((c) => c.method === "sendMessage" && c.body.chat_id === chat).map((c) => c.body.text);
const lastSaid = (chat) => said(chat).at(-1);

const call = (method, path, body, headers = {}) =>
  worker.fetch(new Request("https://relay.test" + path, { method, headers: { "content-type": "application/json", ...headers }, body: body ? JSON.stringify(body) : undefined }), env);
let updateId = 1;
const tg = (chat, text, extra = {}) =>
  call("POST", "/tg/webhook", { update_id: updateId++, message: { chat: { id: chat, type: "private" }, from: { first_name: "Amir", language_code: "fa" }, date: Math.floor(Date.now() / 1000), text, ...extra } },
    { "x-telegram-bot-api-secret-token": "hook-secret" });

async function connect(device, secret) {
  const r = await call("GET", "/v1/connect", null, { upgrade: "websocket", authorization: `Bearer ${device}.${secret}` });
  assert.equal(r.status, 200);
  const ws = hubFor(device).ctx.getWebSockets().at(-1);
  return { ws, msg: (m) => hubFor(device).webSocketMessage(ws, JSON.stringify(m)) };
}

// Codes
for (let i = 0; i < 200; i++) { const c = newCode(); assert.equal(normCode(c), c); }
const sample = newCode();
assert.equal(normCode(sample.slice(0, 4).toLowerCase() + "-" + sample.slice(4)), sample);
assert.equal(normCode("RESTART1"), null, "1 is not in the alphabet");
assert.equal(normCode("SHUTDOWN"), null);
assert.equal(normCode("وضعیت"), null);

// The webhook only trusts Telegram.
assert.equal((await call("POST", "/tg/webhook", { update_id: 1 })).status, 403);
assert.equal((await call("POST", "/tg/webhook", { update_id: 1 }, { "x-telegram-bot-api-secret-token": "nope" })).status, 403);

// Register and connect.
const { device, secret } = await (await call("POST", "/v1/devices")).json();
assert.match(device, /^[0-9a-f]{32}$/);
assert.equal((await call("GET", "/v1/connect", null, { upgrade: "websocket", authorization: `Bearer ${device}.${"0".repeat(64)}` })).status, 401);
assert.equal((await call("GET", "/v1/connect", null, { authorization: `Bearer ${device}.${secret}` })).status, 426);
assert.equal(db.prepare("SELECT secret_hash FROM devices").get().secret_hash.includes(secret), false, "only the hash is stored");
const pc = await connect(device, secret);
await pc.msg({ t: "hello", label: "PC-HOME" });
assert.deepEqual(pc.ws.take(), [{ t: "ready", bot: "ShabpaBot", chats: [] }]);

// Unlinked chats get directions, nothing reaches the PC.
const CHAT = 5001, STRANGER = 6002;
await tg(CHAT, "وضعیت");
assert.match(lastSaid(CHAT), /هنوز سیستمی/);
assert.equal(pc.ws.take().length, 0);

// Pair through the deep link, then approve on the PC.
await pc.msg({ t: "code" });
const { code, ttl } = pc.ws.take()[0];
assert.equal(ttl, 600);
await tg(CHAT, "/start " + code);
assert.deepEqual(pc.ws.take(), [{ t: "pair", chat: CHAT, name: "Amir", username: "" }]);
assert.match(lastSaid(CHAT), /اجازه بده/);
await tg(CHAT, "وضعیت");
assert.equal(pc.ws.take().length, 0, "nothing is forwarded before the PC approves");
await tg(STRANGER, "/start " + code);
assert.match(lastSaid(STRANGER), /درست نیست یا تموم شده/, "codes are one-time");
await pc.msg({ t: "send", chat: CHAT, text: "too early" });
assert.deepEqual(pc.ws.take(), [{ t: "error", error: "not-linked" }]);
await pc.msg({ t: "pair-ok", chat: CHAT, text: "Connected ✅", keyboard: '{"keyboard":[["وضعیت"]]}' });
assert.deepEqual(pc.ws.take(), [{ t: "ready", bot: "ShabpaBot", chats: [CHAT] }]);
assert.equal(lastSaid(CHAT), "Connected ✅");
assert.deepEqual(sent.at(-1).body.reply_markup, { keyboard: [["وضعیت"]] });

// Commands flow to the PC, replies flow back.
await tg(CHAT, "وضعیت");
const [forwarded] = pc.ws.take();
assert.equal(forwarded.t, "msg");
assert.equal(forwarded.text, "وضعیت");
assert.equal(forwarded.chat, CHAT);
await pc.msg({ t: "send", chat: CHAT, text: "CPU 12%" });
assert.equal(lastSaid(CHAT), "CPU 12%");
await pc.msg({ t: "send", chat: STRANGER, text: "spam" });
assert.deepEqual(pc.ws.take(), [{ t: "error", error: "not-linked" }], "a device cannot write to chats that did not link it");
await pc.msg({ t: "notify", text: "Game report" });
assert.equal(lastSaid(CHAT), "Game report");

// A wrong code and a rejected pairing.
await tg(STRANGER, "AAAA-2222");
assert.match(lastSaid(STRANGER), /درست نیست/);
await pc.msg({ t: "code" });
const code2 = pc.ws.take()[0].code;
await tg(STRANGER, code2);
pc.ws.take();
await pc.msg({ t: "pair-no", chat: STRANGER, text: "Rejected on the PC" });
assert.equal(lastSaid(STRANGER), "Rejected on the PC");
await tg(STRANGER, "وضعیت");
assert.equal(pc.ws.take().length, 0);

// Brute force is capped per chat.
for (let i = 0; i < 12; i++) await tg(7003, "ZZZZ-222" + (i % 8 + 2));
assert.match(lastSaid(7003), /تلاش زیادی/);

// Offline PC: immediate answer, nothing queued.
pc.ws.close();
await tg(CHAT, "خاموش");
assert.match(lastSaid(CHAT), /آنلاین نیست/);
const pc2 = await connect(device, secret);
assert.equal(pc2.ws.take().length, 0, "the missed command is not replayed");

// A second PC on the same chat: notifications are labelled, /devices switches.
const second = await (await call("POST", "/v1/devices")).json();
const laptop = await connect(second.device, second.secret);
await laptop.msg({ t: "hello", label: "Laptop" });
await laptop.msg({ t: "code" });
const code3 = laptop.ws.take().at(-1).code;
await tg(CHAT, code3);
await laptop.msg({ t: "pair-ok", chat: CHAT });
laptop.ws.take();
await tg(CHAT, "وضعیت");
assert.equal(laptop.ws.take()[0].text, "وضعیت", "a new link becomes the active PC");
await laptop.msg({ t: "notify", text: "hot" });
assert.equal(lastSaid(CHAT), "[Laptop] hot");
await call("POST", "/tg/webhook", { update_id: updateId++, callback_query: { id: "q", from: { language_code: "en" }, message: { chat: { id: CHAT } }, data: "dev:" + device } },
  { "x-telegram-bot-api-secret-token": "hook-secret" });
await tg(CHAT, "وضعیت");
assert.equal(pc2.ws.take()[0].text, "وضعیت");

// /unlink from Telegram tells the PC; forget wipes the device.
await tg(CHAT, "/unlink");
assert.deepEqual(pc2.ws.take(), [{ t: "unlinked", chat: CHAT }]);
await pc2.msg({ t: "forget" });
assert.deepEqual(pc2.ws.take(), [{ t: "forgotten" }]);
assert.equal(db.prepare("SELECT COUNT(*) AS n FROM devices WHERE id = ?").get(device).n, 0);
assert.equal((await call("GET", "/v1/connect", null, { upgrade: "websocket", authorization: `Bearer ${device}.${secret}` })).status, 401);

// Registration is capped per IP; setup needs the admin key.
for (let i = 0; i < 8; i++) await call("POST", "/v1/devices");
assert.equal((await call("POST", "/v1/devices")).status, 429);
assert.equal((await call("POST", "/admin/setup", {})).status, 403);
assert.equal((await call("POST", "/admin/setup", {}, { "x-admin-key": "admin-key" })).status, 200);
assert.equal(sent.find((c) => c.method === "setWebhook").body.secret_token, "hook-secret");

await cleanup(env);
console.log("relay ok");
