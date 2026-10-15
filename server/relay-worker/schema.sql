-- Shabpa relay: everything the server keeps. No message text, no Telegram names, no IP addresses.
-- Apply once with: wrangler d1 execute shabpa-relay --remote --file=schema.sql

-- One row per installed app. The secret itself never reaches the server's storage, only its SHA-256.
CREATE TABLE IF NOT EXISTS devices (
  id TEXT PRIMARY KEY,
  secret_hash TEXT NOT NULL,
  label TEXT NOT NULL DEFAULT '',
  created INTEGER NOT NULL,
  last_seen INTEGER NOT NULL
);

-- Which Telegram chat may control which device. confirmed = 0 until the user approves it on the PC.
CREATE TABLE IF NOT EXISTS links (
  chat_id INTEGER NOT NULL,
  device_id TEXT NOT NULL,
  confirmed INTEGER NOT NULL DEFAULT 0,
  created INTEGER NOT NULL,
  PRIMARY KEY (chat_id, device_id)
);
CREATE INDEX IF NOT EXISTS links_device ON links (device_id);

-- The device a chat with several PCs is currently talking to.
CREATE TABLE IF NOT EXISTS chats (
  chat_id INTEGER PRIMARY KEY,
  active_device TEXT
);

-- One-time pairing codes, stored hashed, valid for ten minutes.
CREATE TABLE IF NOT EXISTS codes (
  hash TEXT PRIMARY KEY,
  device_id TEXT NOT NULL,
  expires INTEGER NOT NULL
);

-- Rate limit counters; keys are already bucketed by minute or hour and expire on their own.
CREATE TABLE IF NOT EXISTS hits (
  key TEXT PRIMARY KEY,
  n INTEGER NOT NULL,
  expires INTEGER NOT NULL
);
