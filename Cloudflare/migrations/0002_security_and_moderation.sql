-- BeatWanko Security, IP Rate Limiting & Moderation Migration (Non-destructive)

CREATE TABLE IF NOT EXISTS banned_players (
  player_id TEXT PRIMARY KEY,
  reason TEXT NOT NULL DEFAULT '',
  shadowban INTEGER NOT NULL DEFAULT 1,
  created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now'))
);

CREATE TABLE IF NOT EXISTS banned_ips (
  ip_hash TEXT PRIMARY KEY,
  reason TEXT NOT NULL DEFAULT '',
  created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now'))
);

CREATE TABLE IF NOT EXISTS rate_limit_events (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  ip_hash TEXT NOT NULL,
  player_id TEXT NOT NULL,
  created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now'))
);

CREATE INDEX IF NOT EXISTS idx_rate_limit_ip_time
  ON rate_limit_events (ip_hash, created_at DESC);

ALTER TABLE runs ADD COLUMN hidden INTEGER NOT NULL DEFAULT 0;
ALTER TABLE runs ADD COLUMN submit_ip_hash TEXT NOT NULL DEFAULT '';
