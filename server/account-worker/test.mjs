import worker from "./worker.js";
import assert from "node:assert/strict";

const store = new Map();
const sent = [];
globalThis.fetch = async (url, init) => { sent.push({ url: String(url), body: JSON.parse(init.body) }); return new Response("{}"); };
const env = {
  BOT_USERNAME: "ShabPa_Bot", TELEGRAM_BOT_TOKEN: "123:test", WEBHOOK_SECRET: "hook-secret", PEPPER: "pepper-secret",
  ACCOUNTS: {
    async get(key, type) { const v = store.get(key); return v === undefined ? null : type === "json" ? JSON.parse(v) : v; },
    async put(key, value) { store.set(key, value); },
    async delete(key) { store.delete(key); },
  },
};
const call = (method, path, body, headers = {}) =>
  worker.fetch(new Request("https://x.test" + path, { method, headers: { "content-type": "application/json", "cf-connecting-ip": "1.2.3.4", ...headers }, body: body ? JSON.stringify(body) : undefined }), env);

// 1. start a login, nothing is confirmed yet
let r = await call("POST", "/v1/login/start");
assert.equal(r.status, 200);
const { code, url } = await r.json();
assert.match(url, /^https:\/\/t\.me\/ShabPa_Bot\?start=login_/);
r = await call("GET", "/v1/login/poll?code=" + code);
assert.equal(r.status, 202);
assert.equal((await call("GET", "/v1/login/poll?code=short")).status, 400);

// 2. only a call with the webhook secret in the path counts
const tg = (id) => ({ message: { text: "/start login_" + code, from: { id }, chat: { id } } });
assert.equal((await call("POST", "/telegram/wrong", tg(777))).status, 401);
r = await call("GET", "/v1/login/poll?code=" + code);
assert.equal(r.status, 202, "a wrong secret confirms nothing");
assert.equal((await call("POST", "/telegram/hook-secret", tg(777))).status, 200);
assert.ok(sent.some((s) => s.body.chat_id === 777), "the person gets a confirmation in Telegram");

// 3. the app collects its token once
r = await call("GET", "/v1/login/poll?code=" + code);
assert.equal(r.status, 200);
const { token } = await r.json();
assert.ok(token.length >= 32);
assert.equal((await call("GET", "/v1/login/poll?code=" + code)).status, 410, "a login code works once");
assert.ok(![...store.keys()].some((k) => k.includes(token)), "the token itself is never stored, only its hash");
assert.ok(![...store.values()].some((v) => v.includes("777")), "the Telegram id is not stored in the clear");

// 4. the save: first write, read back, conflict when another PC wrote first
const auth = { authorization: "Bearer " + token };
assert.equal((await call("GET", "/v1/save")).status, 401);
assert.equal((await call("GET", "/v1/save", null, auth)).status, 404);
r = await call("PUT", "/v1/save", { rev: 0, blob: '{"feathers":120}' }, auth);
assert.equal(r.status, 200);
assert.equal((await r.json()).rev, 1);
r = await call("GET", "/v1/save", null, auth);
assert.equal((await r.json()).blob, '{"feathers":120}');
r = await call("PUT", "/v1/save", { rev: 0, blob: '{"feathers":5}' }, auth);
assert.equal(r.status, 409, "an out-of-date writer is told so instead of overwriting");
assert.equal((await r.json()).blob, '{"feathers":120}');
assert.equal((await call("PUT", "/v1/save", { rev: 1, blob: "x".repeat(70000) }, auth)).status, 413);
assert.equal((await call("PUT", "/v1/save", { rev: "1", blob: "x" }, auth)).status, 400);

// 5. a second login of the same Telegram account reaches the same save
r = await call("POST", "/v1/login/start"); const second = await r.json();
await call("POST", "/telegram/hook-secret", { message: { text: "/start login_" + second.code, from: { id: 777 }, chat: { id: 777 } } });
const t2 = (await (await call("GET", "/v1/login/poll?code=" + second.code)).json()).token;
r = await call("GET", "/v1/save", null, { authorization: "Bearer " + t2 });
assert.equal((await r.json()).rev, 1);
// and another person does not
r = await call("POST", "/v1/login/start"); const third = await r.json();
await call("POST", "/telegram/hook-secret", { message: { text: "/start login_" + third.code, from: { id: 888 }, chat: { id: 888 } } });
const t3 = (await (await call("GET", "/v1/login/poll?code=" + third.code)).json()).token;
assert.equal((await call("GET", "/v1/save", null, { authorization: "Bearer " + t3 })).status, 404);

// 6. logout kills one token; deleting the account removes everything
assert.equal((await call("POST", "/v1/logout", null, { authorization: "Bearer " + t2 })).status, 200);
assert.equal((await call("GET", "/v1/save", null, { authorization: "Bearer " + t2 })).status, 401);
assert.equal((await call("DELETE", "/v1/account", null, auth)).status, 200);
assert.equal((await call("GET", "/v1/save", null, auth)).status, 401);
assert.ok(![...store.keys()].some((k) => k.startsWith("save:") && JSON.parse(store.get(k)).blob.includes("120")), "the save is gone");

// 7. the login start is rate limited
let last;
for (let i = 0; i < 25; i++) last = await call("POST", "/v1/login/start");
assert.equal(last.status, 429);
console.log("account worker: all good");
