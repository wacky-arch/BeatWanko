-- BeatWanko D1 Schema (SQLite)

CREATE TABLE IF NOT EXISTS players (
  player_id TEXT PRIMARY KEY,
  display_name TEXT NOT NULL,
  avatar_url TEXT NOT NULL DEFAULT '',
  profile_source TEXT NOT NULL DEFAULT 'platform',
  profile_url TEXT NOT NULL DEFAULT '',
  platform_user_name TEXT NOT NULL DEFAULT '',
  profile_updated_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS runs (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  player_id TEXT NOT NULL,
  mode TEXT NOT NULL CHECK (mode IN ('unconditional', 'stepup', 'endurance')),
  played_at TEXT NOT NULL,
  total_play_seconds REAL NOT NULL DEFAULT 0,
  end_reason TEXT NOT NULL CHECK (end_reason IN ('fail', 'close_lid', 'complete')),
  cleared_count INTEGER NOT NULL DEFAULT 0,
  total_score INTEGER NOT NULL DEFAULT 0,
  mod_version TEXT NOT NULL DEFAULT '1.1.0',
  game_version TEXT NOT NULL DEFAULT '1.40.8',
  created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
  FOREIGN KEY (player_id) REFERENCES players(player_id)
);

CREATE INDEX IF NOT EXISTS idx_runs_mode_rank
  ON runs (mode, cleared_count DESC, total_score DESC, played_at ASC);

CREATE INDEX IF NOT EXISTS idx_runs_player_mode
  ON runs (player_id, mode, cleared_count DESC, total_score DESC, played_at DESC);

CREATE INDEX IF NOT EXISTS idx_runs_rate_limit
  ON runs (player_id, created_at DESC);

CREATE TABLE IF NOT EXISTS run_songs (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  run_id INTEGER NOT NULL,
  song_order INTEGER NOT NULL,
  song_hash TEXT NOT NULL,
  song_name TEXT NOT NULL,
  difficulty TEXT NOT NULL,
  characteristic TEXT NOT NULL DEFAULT 'Standard',
  score INTEGER NOT NULL DEFAULT 0,
  accuracy_percent REAL NOT NULL DEFAULT 0,
  cleared INTEGER NOT NULL DEFAULT 0,
  stars REAL NOT NULL DEFAULT 0,
  step_band INTEGER NOT NULL DEFAULT 0,
  FOREIGN KEY (run_id) REFERENCES runs(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_run_songs_run_id
  ON run_songs (run_id, song_order ASC);
