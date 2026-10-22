import worker from "./worker.js";
import assert from "node:assert/strict";

const store = new Map();
const env = {
  ADMIN_KEY: "secret-admin-key",
  TICKETS: {
    async get(key, type) { const v = store.get(key); return v === undefined ? null : type === "json" ? JSON.parse(v) : v; },
    async put(key, value) { store.set(key, value); },
    async delete(key) { store.delete(key); },
  },
};
const call = (method, path, body, headers = {}) =>
  worker.fetch(new Request("https://x.test" + path, { method, headers: { "content-type": "application/json", ...headers }, body: body ? JSON.stringify(body) : undefined }), env);
const device = "a".repeat(32);

let r = await call("POST", "/tickets", { device, title: "Overlay is empty", body: "FPS shows a dash in game", category: "bug", diag: "v2.8.0" });
assert.equal(r.status, 200);
const { id } = await r.json();

r = await call("GET", "/tickets?device=" + device);
let mine = (await r.json()).tickets;
assert.equal(mine.length, 1);
assert.equal(mine[0].diag, undefined, "diagnostics are not echoed to the user list");

assert.equal((await call("GET", "/admin/tickets")).status, 403);
assert.equal((await call("GET", "/admin/tickets", null, { "x-admin-key": "wrong" })).status, 403);
r = await call("GET", "/admin/tickets", null, { "x-admin-key": env.ADMIN_KEY });
assert.equal((await r.json()).tickets[0].diag, "v2.8.0");

r = await call("POST", "/admin/tickets/" + id + "/reply", { text: "Run as administrator", status: "answered" }, { "x-admin-key": env.ADMIN_KEY });
assert.equal(r.status, 200);
mine = (await (await call("GET", "/tickets?device=" + device)).json()).tickets;
assert.equal(mine[0].status, "answered");
assert.equal(mine[0].replies[0].text, "Run as administrator");

assert.equal((await call("POST", "/tickets", { device: "bad", title: "abc", body: "hello world" })).status, 400);
assert.equal((await call("POST", "/tickets", { device, title: "a", body: "b" })).status, 400);
for (let i = 0; i < 4; i++) assert.equal((await call("POST", "/tickets", { device, title: "again " + i, body: "again again" })).status, 200);
assert.equal((await call("POST", "/tickets", { device, title: "too many", body: "too many" })).status, 429);

// close and delete: only with the ticket's own device id
r = await call("POST", "/tickets", { device: "b".repeat(32), title: "Close me", body: "please close this one", category: "game" });
const own = (await r.json()).id;
assert.equal((await call("POST", "/tickets/" + own + "/close?device=" + device)).status, 404, "another device cannot close it");
assert.equal((await call("POST", "/tickets/" + own + "/close?device=" + "b".repeat(32))).status, 200);
let theirs = (await (await call("GET", "/tickets?device=" + "b".repeat(32))).json()).tickets;
assert.equal(theirs[0].status, "closed");
assert.equal(theirs[0].category, "game");
assert.equal((await call("DELETE", "/tickets/" + own + "?device=" + device)).status, 404);
assert.equal((await call("DELETE", "/tickets/" + own + "?device=" + "b".repeat(32))).status, 200);
theirs = (await (await call("GET", "/tickets?device=" + "b".repeat(32))).json()).tickets;
assert.equal(theirs.length, 0);
assert.equal((await call("DELETE", "/admin/tickets/" + id)).status, 403);
assert.equal((await call("DELETE", "/admin/tickets/" + id, null, { "x-admin-key": env.ADMIN_KEY })).status, 200);
const left = (await (await call("GET", "/admin/tickets", null, { "x-admin-key": env.ADMIN_KEY })).json()).tickets;
assert.ok(!left.some((t) => t.id === id));
console.log("worker ok");
