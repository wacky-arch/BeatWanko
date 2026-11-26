const API_VERSION = '1.1.7';
const VALID_MODES = new Set(['unconditional', 'stepup', 'endurance']);
const VALID_END_REASONS = new Set(['fail', 'close_lid', 'complete']);
const PROFILE_TTL_MS = 6 * 60 * 60 * 1000;

const MODE_LABELS = {
  unconditional: '無条件ランキング',
  stepup: 'SS⭐️ステップアップ',
  endurance: '耐久ランキング',
};

const CORS_HEADERS = {
  'Access-Control-Allow-Origin': '*',
  'Access-Control-Allow-Methods': 'GET, POST, DELETE, OPTIONS',
  'Access-Control-Allow-Headers': 'Content-Type, Authorization',
};

function jsonResponse(data, status = 200, extraHeaders = {}) {
  return new Response(JSON.stringify(data), {
    status,
    headers: {
      'Content-Type': 'application/json; charset=utf-8',
      'Cache-Control': 'no-store',
      ...CORS_HEADERS,
      ...extraHeaders,
    },
  });
}

function normalizeDiffName(diff) {
  const s = String(diff || '').trim();
  if (s === 'Expert+' || s === 'ExpertPlus') return 'ExpertPlus';
  if (['Easy', 'Normal', 'Hard', 'Expert'].includes(s)) return s;
  return 'Expert';
}

function normalizeCharacteristic(ch) {
  const s = String(ch || 'Standard').trim();
  return s.length > 0 ? s.slice(0, 32) : 'Standard';
}

function isBetterRun(clearedCount, totalScore, existingBest) {
  if (!existingBest) return true;
  if (clearedCount !== existingBest.cleared_count) {
    return clearedCount > existingBest.cleared_count;
  }
  return totalScore > existingBest.total_score;
}

async function computeIpHash(request, env) {
  const ip = (request.headers.get('CF-Connecting-IP') || request.headers.get('x-real-ip') || 'unknown').trim();
  const salt = env.IP_HASH_SALT || env.ADMIN_TOKEN || 'beatwanko-v1';
  const buf = new TextEncoder().encode(`${ip}:${salt}`);
  const digest = new Uint8Array(await crypto.subtle.digest('SHA-256', buf));
  return Array.from(digest).map((b) => b.toString(16).padStart(2, '0')).join('').slice(0, 32);
}

async function isAuthorizedAdmin(request, env) {
  const expected = String(env.ADMIN_TOKEN || '').trim();
  if (!expected) return false;
  const auth = request.headers.get('Authorization') || '';
  const m = auth.match(/^Bearer\s+(.+)$/i);
  if (!m) return false;
  const provided = m[1].trim();
  const enc = new TextEncoder();
  const a = new Uint8Array(await crypto.subtle.digest('SHA-256', enc.encode(expected)));
  const b = new Uint8Array(await crypto.subtle.digest('SHA-256', enc.encode(provided)));
  let diff = 0;
  for (let i = 0; i < a.length; i++) {
    diff |= a[i] ^ b[i];
  }
  return diff === 0;
}

async function fetchJsonWithTimeout(url, timeoutMs = 3500) {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);
  try {
    const res = await fetch(url, {
      signal: controller.signal,
      headers: {
        'User-Agent': `BeatWanko-Leaderboard/${API_VERSION} (+https://github.com/wacky-arch/BeatWanko)`,
        Accept: 'application/json',
      },
    });
    if (!res.ok) return null;
    return await res.json();
  } catch {
    return null;
  } finally {
    clearTimeout(timer);
  }
}

async function resolvePlayerProfile(env, playerId, platformUserName) {
  const existing = await env.DB.prepare(
    'SELECT player_id, display_name, avatar_url, profile_source, profile_url, platform_user_name, profile_updated_at FROM players WHERE player_id = ?'
  )
    .bind(playerId)
    .first();

  if (existing && existing.profile_updated_at) {
    const ageMs = Date.now() - Date.parse(existing.profile_updated_at);
    if (Number.isFinite(ageMs) && ageMs >= 0 && ageMs < PROFILE_TTL_MS) {
      if (existing.profile_source === 'platform' && platformUserName && platformUserName !== existing.display_name) {
        return {
          display_name: platformUserName,
          avatar_url: '',
          profile_source: 'platform',
          profile_url: '',
        };
      }
      return existing;
    }
  }

  // 1. ScoreSaber API (works for both SteamID64 and Meta/Oculus 64-bit User ID)
  const ss = await fetchJsonWithTimeout(`https://scoresaber.com/api/player/${encodeURIComponent(playerId)}/basic`);
  if (ss && ss.name && !ss.errorMessage) {
    return {
      display_name: String(ss.name).slice(0, 64),
      avatar_url: String(ss.profilePicture || ''),
      profile_source: 'scoresaber',
      profile_url: `https://scoresaber.com/u/${encodeURIComponent(playerId)}`,
    };
  }

  // 2. BeatLeader API
  const bl = await fetchJsonWithTimeout(`https://api.beatleader.xyz/player/${encodeURIComponent(playerId)}?stats=false`);
  if (bl && bl.name) {
    return {
      display_name: String(bl.name).slice(0, 64),
      avatar_url: String(bl.avatar || ''),
      profile_source: 'beatleader',
      profile_url: `https://beatleader.com/u/${encodeURIComponent(playerId)}`,
    };
  }

  // 3. Steam / Meta Platform Name fallback
  const fallbackName = String(platformUserName || existing?.display_name || playerId).slice(0, 64);
  return {
    display_name: fallbackName,
    avatar_url: '',
    profile_source: 'platform',
    profile_url: '',
  };
}

async function getPlayerModeRank(env, mode, playerId) {
  const row = await env.DB.prepare(`
    WITH best_runs AS (
      SELECT
        r.player_id,
        r.cleared_count,
        r.total_score,
        r.played_at,
        ROW_NUMBER() OVER (
          PARTITION BY r.player_id
          ORDER BY r.cleared_count DESC, r.total_score DESC, r.played_at ASC
        ) AS rn
      FROM runs r
      WHERE r.mode = ?
        AND COALESCE(r.hidden, 0) = 0
        AND r.player_id NOT IN (SELECT player_id FROM banned_players)
    ),
    ranked AS (
      SELECT
        player_id,
        ROW_NUMBER() OVER (
          ORDER BY cleared_count DESC, total_score DESC, played_at ASC
        ) AS rank_pos,
        COUNT(*) OVER () AS total_players
      FROM best_runs
      WHERE rn = 1
    )
    SELECT rank_pos, total_players FROM ranked WHERE player_id = ?
  `)
    .bind(mode, playerId)
    .first();

  if (row) {
    return { rank: Number(row.rank_pos) || 0, total_players: Number(row.total_players) || 0 };
  }
  const totalRow = await env.DB.prepare(`
    SELECT COUNT(DISTINCT r.player_id) AS cnt
    FROM runs r
    WHERE r.mode = ?
      AND COALESCE(r.hidden, 0) = 0
      AND r.player_id NOT IN (SELECT player_id FROM banned_players)
  `)
    .bind(mode)
    .first();
  return { rank: 0, total_players: Number(totalRow?.cnt) || 0 };
}

function validateAndNormalizePayload(body) {
  if (!body || typeof body !== 'object') {
    return { error: 'Invalid JSON payload' };
  }
  const playerId = String(body.player_id || '').trim();
  if (!/^[1-9][0-9]{9,20}$/.test(playerId)) {
    return { error: 'Invalid player_id (must be a 10-21 digit numeric platform ID)' };
  }
  const mode = String(body.mode || '').trim();
  if (!VALID_MODES.has(mode)) {
    return { error: 'Invalid mode' };
  }
  const endReason = String(body.end_reason || '').trim();
  if (!VALID_END_REASONS.has(endReason)) {
    return { error: 'Invalid end_reason' };
  }
  const maxSongs = mode === 'stepup' ? 14 : 200;
  if (!Array.isArray(body.songs) || body.songs.length === 0 || body.songs.length > maxSongs) {
    return { error: `songs must be a non-empty array (1..${maxSongs})` };
  }

  const parsedDate = Date.parse(String(body.played_at || ''));
  const playedAt = Number.isFinite(parsedDate) ? new Date(parsedDate).toISOString() : new Date().toISOString();
  const rawTotalPlaySeconds = Number(body.total_play_seconds);
  if (!Number.isFinite(rawTotalPlaySeconds) || rawTotalPlaySeconds < 0.5 || rawTotalPlaySeconds > 86400) {
    return { error: 'total_play_seconds must be between 0.5 and 86400' };
  }
  const totalPlaySeconds = Math.round(rawTotalPlaySeconds * 100) / 100;
  const platformUserName = String(body.platform_user_name || '').trim().slice(0, 64);
  const modVersion = String(body.mod_version || API_VERSION).trim().slice(0, 24);
  const gameVersion = String(body.game_version || '1.40.8').trim().slice(0, 24);

  const songs = [];
  const seenHashes = new Set();
  let clearedCount = 0;
  let totalScore = 0;

  for (let i = 0; i < body.songs.length; i++) {
    const s = body.songs[i];
    if (!s || typeof s !== 'object') {
      return { error: `Invalid song entry at index ${i}` };
    }
    const songHash = String(s.song_hash || '').trim().toUpperCase();
    if (!/^[0-9A-F]{40}$/.test(songHash)) {
      return { error: `Invalid song_hash at index ${i}` };
    }
    if (seenHashes.has(songHash)) {
      return { error: `Duplicate song_hash in single run at index ${i}` };
    }
    seenHashes.add(songHash);

    const songName = String(s.song_name || 'Unknown').trim().slice(0, 200) || 'Unknown';
    const difficulty = normalizeDiffName(s.difficulty);
    const characteristic = normalizeCharacteristic(s.characteristic);

    const rawScore = Number(s.score);
    if (!Number.isFinite(rawScore) || rawScore < 0 || rawScore > 15000000) {
      return { error: `Score out of range at index ${i}` };
    }
    const score = Math.floor(rawScore);

    const rawAcc = Number(s.accuracy_percent);
    if (!Number.isFinite(rawAcc) || rawAcc < 0 || rawAcc > 100) {
      return { error: `accuracy_percent out of range at index ${i}` };
    }
    const accuracyPercent = Math.round(rawAcc * 100) / 100;
    if (score >= 10000 && accuracyPercent <= 0) {
      return { error: `Inconsistent score/accuracy at index ${i}` };
    }

    const cleared = Boolean(s.cleared);
    if (endReason === 'fail') {
      if (i < body.songs.length - 1 && !cleared) {
        return { error: `Non-final song must be cleared when end_reason=fail (index ${i})` };
      }
      if (i === body.songs.length - 1 && cleared) {
        return { error: 'Final song must not be cleared when end_reason=fail' };
      }
    } else if (!cleared) {
      return { error: `All songs must be cleared when end_reason=${endReason} (index ${i})` };
    }

    const rawStars = Number(s.stars || 0);
    if (!Number.isFinite(rawStars) || rawStars < 0 || rawStars > 99) {
      return { error: `stars out of range at index ${i}` };
    }
    const stars = Math.round(rawStars * 100) / 100;
    const stepBand = Math.max(0, Math.min(30, Math.floor(Number(s.step_band) || 0)));

    if (mode === 'stepup') {
      const expectedStep = i + 1;
      if (stepBand !== expectedStep) {
        return { error: `Invalid step_band progression at index ${i} (expected ${expectedStep}, got ${stepBand})` };
      }
      if (expectedStep === 1) {
        if (!(stars > 0 && stars < 2.02)) {
          return { error: `Invalid StepUp step 1 stars (${stars})` };
        }
      } else if (!(stars >= expectedStep - 0.02 && stars < expectedStep + 1.02)) {
        return { error: `Invalid StepUp step ${expectedStep} stars (${stars})` };
      }
    }

    if (cleared) clearedCount++;
    totalScore += score;
    songs.push({
      song_order: i + 1,
      song_hash: songHash,
      song_name: songName,
      difficulty,
      characteristic,
      score,
      accuracy_percent: accuracyPercent,
      cleared: cleared ? 1 : 0,
      stars,
      step_band: stepBand,
    });
  }

  if (endReason === 'complete' && (mode !== 'stepup' || clearedCount !== 14 || songs.length !== 14)) {
    return { error: 'end_reason=complete requires 14 cleared songs in stepup mode' };
  }

  const minSecondsPerCleared = mode === 'stepup' ? 20 : 8;
  if (totalPlaySeconds < clearedCount * minSecondsPerCleared) {
    return { error: 'total_play_seconds too short for cleared_count' };
  }

  return {
    data: {
      playerId,
      platformUserName,
      mode,
      playedAt,
      totalPlaySeconds,
      endReason,
      clearedCount,
      totalScore,
      modVersion,
      gameVersion,
      songs,
    },
  };
}

async function handleSubmitRun(request, env) {
  let body;
  try {
    body = await request.json();
  } catch {
    return jsonResponse({ ok: false, error: 'Malformed JSON body' }, 400);
  }

  const validated = validateAndNormalizePayload(body);
  if (validated.error) {
    return jsonResponse({ ok: false, error: validated.error }, 400);
  }
  const run = validated.data;
  const ipHash = await computeIpHash(request, env);

  // Best-effort cleanup of stale rate-limit windows
  await env.DB.prepare("DELETE FROM rate_limit_events WHERE created_at < strftime('%Y-%m-%dT%H:%M:%fZ', 'now', '-2 hours')").run();

  // IP ban check (silent drop)
  const bannedIp = await env.DB.prepare('SELECT ip_hash FROM banned_ips WHERE ip_hash = ? LIMIT 1').bind(ipHash).first();
  if (bannedIp) {
    return jsonResponse({
      ok: true,
      saved: false,
      is_new_best: false,
      reason: 'ignored',
      display_name: run.platformUserName || run.playerId,
      rank: 0,
      total_players: 0,
    });
  }

  // Player ban check
  const bannedPlayer = await env.DB.prepare('SELECT shadowban FROM banned_players WHERE player_id = ? LIMIT 1')
    .bind(run.playerId)
    .first();
  if (bannedPlayer) {
    if (Number(bannedPlayer.shadowban) === 1) {
      return jsonResponse({
        ok: true,
        saved: false,
        is_new_best: false,
        reason: 'saved_history',
        display_name: run.platformUserName || run.playerId,
        rank: 0,
        total_players: 0,
      });
    }
    return jsonResponse({ ok: false, error: 'Player is banned' }, 403);
  }

  // Duplicate guard
  const dup = await env.DB.prepare(
    'SELECT id FROM runs WHERE player_id = ? AND mode = ? AND played_at = ? AND cleared_count = ? AND total_score = ? AND COALESCE(hidden, 0) = 0 LIMIT 1'
  )
    .bind(run.playerId, run.mode, run.playedAt, run.clearedCount, run.totalScore)
    .first();
  if (dup) {
    const rankInfo = await getPlayerModeRank(env, run.mode, run.playerId);
    const player = await env.DB.prepare('SELECT display_name FROM players WHERE player_id = ?').bind(run.playerId).first();
    return jsonResponse({
      ok: true,
      saved: false,
      is_new_best: false,
      reason: 'duplicate_run',
      run_id: dup.id,
      display_name: player?.display_name || run.platformUserName,
      ...rankInfo,
    });
  }

  // IP rate limit: max 15 uploads per 10 minutes per IP
  const recentIpRow = await env.DB.prepare(
    "SELECT COUNT(*) AS cnt FROM rate_limit_events WHERE ip_hash = ? AND created_at >= strftime('%Y-%m-%dT%H:%M:%fZ', 'now', '-10 minutes')"
  )
    .bind(ipHash)
    .first();
  if (Number(recentIpRow?.cnt || 0) >= 15) {
    return jsonResponse({ ok: false, error: 'IP rate limit exceeded; please try again shortly.' }, 429);
  }

  // Multi-ID spam limit: max 3 other player_ids from the same IP in 60 minutes
  const distinctIdRow = await env.DB.prepare(
    "SELECT COUNT(DISTINCT player_id) AS cnt FROM rate_limit_events WHERE ip_hash = ? AND player_id != ? AND created_at >= strftime('%Y-%m-%dT%H:%M:%fZ', 'now', '-60 minutes')"
  )
    .bind(ipHash, run.playerId)
    .first();
  if (Number(distinctIdRow?.cnt || 0) >= 3) {
    return jsonResponse({ ok: false, error: 'Too many distinct player IDs from this IP.' }, 429);
  }

  // Player rate limit: max 15 uploads per 10 minutes per player_id
  const recentPlayerRow = await env.DB.prepare(
    "SELECT COUNT(*) AS cnt FROM runs WHERE player_id = ? AND created_at >= strftime('%Y-%m-%dT%H:%M:%fZ', 'now', '-10 minutes')"
  )
    .bind(run.playerId)
    .first();
  if (Number(recentPlayerRow?.cnt || 0) >= 15) {
    return jsonResponse({ ok: false, error: 'Rate limit exceeded; please try again shortly.' }, 429);
  }

  await env.DB.prepare('INSERT INTO rate_limit_events (ip_hash, player_id) VALUES (?, ?)').bind(ipHash, run.playerId).run();

  const profile = await resolvePlayerProfile(env, run.playerId, run.platformUserName);
  const nowIso = new Date().toISOString();

  await env.DB.prepare(`
    INSERT INTO players (player_id, display_name, avatar_url, profile_source, profile_url, platform_user_name, profile_updated_at)
    VALUES (?, ?, ?, ?, ?, ?, ?)
    ON CONFLICT(player_id) DO UPDATE SET
      display_name = excluded.display_name,
      avatar_url = excluded.avatar_url,
      profile_source = excluded.profile_source,
      profile_url = excluded.profile_url,
      platform_user_name = excluded.platform_user_name,
      profile_updated_at = excluded.profile_updated_at
  `)
    .bind(
      run.playerId,
      profile.display_name,
      profile.avatar_url || '',
      profile.profile_source || 'platform',
      profile.profile_url || '',
      run.platformUserName,
      nowIso
    )
    .run();

  const existingBest = await env.DB.prepare(
    'SELECT id, cleared_count, total_score FROM runs WHERE player_id = ? AND mode = ? AND COALESCE(hidden, 0) = 0 ORDER BY cleared_count DESC, total_score DESC, played_at ASC LIMIT 1'
  )
    .bind(run.playerId, run.mode)
    .first();

  const isNewBest = isBetterRun(run.clearedCount, run.totalScore, existingBest);

  if (run.endReason === 'close_lid' && !isNewBest) {
    const rankInfo = await getPlayerModeRank(env, run.mode, run.playerId);
    return jsonResponse({
      ok: true,
      saved: false,
      is_new_best: false,
      reason: 'skipped_not_new_best',
      display_name: profile.display_name,
      ...rankInfo,
    });
  }

  const inserted = await env.DB.prepare(`
    INSERT INTO runs (player_id, mode, played_at, total_play_seconds, end_reason, cleared_count, total_score, mod_version, game_version, hidden, submit_ip_hash)
    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, 0, ?)
    RETURNING id
  `)
    .bind(
      run.playerId,
      run.mode,
      run.playedAt,
      run.totalPlaySeconds,
      run.endReason,
      run.clearedCount,
      run.totalScore,
      run.modVersion,
      run.gameVersion,
      ipHash
    )
    .first();

  const runId = inserted?.id;
  if (!runId) {
    return jsonResponse({ ok: false, error: 'Failed to insert run' }, 500);
  }

  const songInsertStmt = env.DB.prepare(`
    INSERT INTO run_songs (run_id, song_order, song_hash, song_name, difficulty, characteristic, score, accuracy_percent, cleared, stars, step_band)
    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
  `);
  const songBatch = run.songs.map((s) =>
    songInsertStmt.bind(
      runId,
      s.song_order,
      s.song_hash,
      s.song_name,
      s.difficulty,
      s.characteristic,
      s.score,
      s.accuracy_percent,
      s.cleared,
      s.stars,
      s.step_band
    )
  );
  await env.DB.batch(songBatch);

  const rankInfo = await getPlayerModeRank(env, run.mode, run.playerId);
  return jsonResponse({
    ok: true,
    saved: true,
    run_id: runId,
    is_new_best: isNewBest,
    reason: isNewBest ? 'new_best' : 'saved_history',
    display_name: profile.display_name,
    ...rankInfo,
  });
}

async function handleGetLeaderboard(url, env) {
  const mode = String(url.searchParams.get('mode') || 'unconditional').trim();
  if (!VALID_MODES.has(mode)) {
    return jsonResponse({ ok: false, error: 'Invalid mode' }, 400);
  }
  const limit = Math.max(1, Math.min(200, Math.floor(Number(url.searchParams.get('limit')) || 100)));

  const res = await env.DB.prepare(`
    WITH scoped AS (
      SELECT
        r.*,
        COUNT(*) OVER (PARTITION BY r.player_id) AS run_count,
        ROW_NUMBER() OVER (
          PARTITION BY r.player_id
          ORDER BY r.cleared_count DESC, r.total_score DESC, r.played_at ASC
        ) AS rn
      FROM runs r
      WHERE r.mode = ?
        AND COALESCE(r.hidden, 0) = 0
        AND r.player_id NOT IN (SELECT player_id FROM banned_players)
    )
    SELECT
      s.id AS run_id,
      s.player_id,
      p.display_name,
      p.avatar_url,
      p.profile_source,
      p.profile_url,
      s.mode,
      s.played_at,
      s.total_play_seconds,
      s.end_reason,
      s.cleared_count,
      s.total_score,
      s.run_count,
      (SELECT COUNT(*) FROM run_songs rs WHERE rs.run_id = s.id) AS total_songs_count
    FROM scoped s
    JOIN players p ON p.player_id = s.player_id
    WHERE s.rn = 1
    ORDER BY s.cleared_count DESC, s.total_score DESC, s.played_at ASC
    LIMIT ?
  `)
    .bind(mode, limit)
    .all();

  const entries = (res.results || []).map((row, index) => ({
    rank: index + 1,
    ...row,
  }));

  return jsonResponse({
    ok: true,
    mode,
    mode_label: MODE_LABELS[mode] || mode,
    total_players: entries.length,
    entries,
  });
}

async function handleGetRunDetail(runId, env) {
  const id = Math.floor(Number(runId));
  if (!Number.isFinite(id) || id <= 0) {
    return jsonResponse({ ok: false, error: 'Invalid run id' }, 400);
  }

  const run = await env.DB.prepare(`
    SELECT
      r.id, r.player_id, r.mode, r.played_at, r.total_play_seconds, r.end_reason, r.cleared_count, r.total_score, r.mod_version, r.game_version, r.created_at,
      p.display_name,
      p.avatar_url,
      p.profile_source,
      p.profile_url
    FROM runs r
    JOIN players p ON p.player_id = r.player_id
    WHERE r.id = ?
      AND COALESCE(r.hidden, 0) = 0
      AND r.player_id NOT IN (SELECT player_id FROM banned_players)
  `)
    .bind(id)
    .first();

  if (!run) {
    return jsonResponse({ ok: false, error: 'Run not found' }, 404);
  }

  const songsRes = await env.DB.prepare(
    'SELECT song_order, song_hash, song_name, difficulty, characteristic, score, accuracy_percent, cleared, stars, step_band FROM run_songs WHERE run_id = ? ORDER BY song_order ASC'
  )
    .bind(id)
    .all();

  return jsonResponse({
    ok: true,
    run,
    songs: songsRes.results || [],
  });
}

async function handleGetPlayerRuns(playerId, url, env) {
  const pid = String(playerId || '').trim();
  const mode = String(url.searchParams.get('mode') || '').trim();
  if (!pid) {
    return jsonResponse({ ok: false, error: 'Missing playerId' }, 400);
  }
  const limit = Math.max(1, Math.min(100, Math.floor(Number(url.searchParams.get('limit')) || 50)));

  const banned = await env.DB.prepare('SELECT player_id FROM banned_players WHERE player_id = ?').bind(pid).first();
  if (banned) {
    return jsonResponse({ ok: false, error: 'Player not found' }, 404);
  }

  const player = await env.DB.prepare(
    'SELECT player_id, display_name, avatar_url, profile_source, profile_url FROM players WHERE player_id = ?'
  )
    .bind(pid)
    .first();
  if (!player) {
    return jsonResponse({ ok: false, error: 'Player not found' }, 404);
  }

  const sql = VALID_MODES.has(mode)
    ? `SELECT r.id, r.player_id, r.mode, r.played_at, r.total_play_seconds, r.end_reason, r.cleared_count, r.total_score, r.mod_version, r.game_version, r.created_at,
       (SELECT COUNT(*) FROM run_songs rs WHERE rs.run_id = r.id) AS total_songs_count
       FROM runs r WHERE r.player_id = ? AND r.mode = ? AND COALESCE(r.hidden, 0) = 0 ORDER BY r.cleared_count DESC, r.total_score DESC, r.played_at DESC LIMIT ?`
    : `SELECT r.id, r.player_id, r.mode, r.played_at, r.total_play_seconds, r.end_reason, r.cleared_count, r.total_score, r.mod_version, r.game_version, r.created_at,
       (SELECT COUNT(*) FROM run_songs rs WHERE rs.run_id = r.id) AS total_songs_count
       FROM runs r WHERE r.player_id = ? AND COALESCE(r.hidden, 0) = 0 ORDER BY r.played_at DESC LIMIT ?`;

  const runsRes = VALID_MODES.has(mode)
    ? await env.DB.prepare(sql).bind(pid, mode, limit).all()
    : await env.DB.prepare(sql).bind(pid, limit).all();

  return jsonResponse({
    ok: true,
    player,
    mode: VALID_MODES.has(mode) ? mode : 'all',
    runs: runsRes.results || [],
  });
}

async function handleGetRunPlaylist(runId, env) {
  const id = Math.floor(Number(runId));
  if (!Number.isFinite(id) || id <= 0) {
    return jsonResponse({ ok: false, error: 'Invalid run id' }, 400);
  }

  const run = await env.DB.prepare(`
    SELECT r.id, r.mode, r.played_at, r.cleared_count, r.total_score, p.display_name
    FROM runs r
    JOIN players p ON p.player_id = r.player_id
    WHERE r.id = ?
      AND COALESCE(r.hidden, 0) = 0
      AND r.player_id NOT IN (SELECT player_id FROM banned_players)
  `)
    .bind(id)
    .first();

  if (!run) {
    return jsonResponse({ ok: false, error: 'Run not found' }, 404);
  }

  const songsRes = await env.DB.prepare(
    'SELECT song_order, song_hash, song_name, difficulty, characteristic, cleared FROM run_songs WHERE run_id = ? ORDER BY song_order ASC'
  )
    .bind(id)
    .all();

  const seen = new Set();
  const songs = [];
  for (const s of songsRes.results || []) {
    const hash = String(s.song_hash || '').trim().toUpperCase();
    if (!hash || seen.has(hash)) continue;
    seen.add(hash);
    songs.push({
      hash,
      songName: s.song_name,
      difficulties: [
        {
          characteristic: normalizeCharacteristic(s.characteristic),
          name: normalizeDiffName(s.difficulty),
        },
      ],
    });
  }

  const modeLabel = MODE_LABELS[run.mode] || run.mode;
  const playlist = {
    playlistTitle: `BeatWanko [${modeLabel}] - ${run.display_name} (${run.cleared_count}杯)`,
    playlistAuthor: 'BeatWanko Leaderboard',
    playlistDescription: `Played on ${run.played_at} | Cleared: ${run.cleared_count} | Total Score: ${run.total_score}`,
    songs,
  };

  const fileName = `beatwanko_${run.mode}_run${run.id}.bplist`;
  return new Response(JSON.stringify(playlist, null, 2), {
    status: 200,
    headers: {
      'Content-Type': 'application/octet-stream',
      'Content-Disposition': `attachment; filename="${fileName}"`,
      'Cache-Control': 'public, max-age=300',
      ...CORS_HEADERS,
    },
  });
}

async function handleAdminSummary(env) {
  const runsRes = await env.DB.prepare(`
    SELECT r.id, r.player_id, p.display_name, r.mode, r.cleared_count, r.total_score, r.total_play_seconds, r.end_reason, COALESCE(r.hidden, 0) AS hidden, r.played_at
    FROM runs r
    LEFT JOIN players p ON p.player_id = r.player_id
    ORDER BY r.id DESC
    LIMIT 30
  `).all();
  const bannedPlayersRes = await env.DB.prepare('SELECT player_id, reason, shadowban, created_at FROM banned_players ORDER BY created_at DESC').all();
  const bannedIpsRes = await env.DB.prepare('SELECT ip_hash, reason, created_at FROM banned_ips ORDER BY created_at DESC').all();
  return jsonResponse({
    ok: true,
    recent_runs: runsRes.results || [],
    banned_players: bannedPlayersRes.results || [],
    banned_ips: bannedIpsRes.results || [],
  });
}

async function handleAdminDeleteRun(runId, env) {
  const id = Math.floor(Number(runId));
  if (!Number.isFinite(id) || id <= 0) {
    return jsonResponse({ ok: false, error: 'Invalid run id' }, 400);
  }
  await env.DB.prepare('DELETE FROM run_songs WHERE run_id = ?').bind(id).run();
  const res = await env.DB.prepare('DELETE FROM runs WHERE id = ?').bind(id).run();
  return jsonResponse({ ok: true, deleted_run_id: id, changes: res.meta?.changes || 0 });
}

async function handleAdminHideRun(runId, request, env) {
  const id = Math.floor(Number(runId));
  if (!Number.isFinite(id) || id <= 0) {
    return jsonResponse({ ok: false, error: 'Invalid run id' }, 400);
  }
  let body = {};
  try {
    body = await request.json();
  } catch {
    body = {};
  }
  const hiddenVal = body.hidden === false ? 0 : 1;
  const res = await env.DB.prepare('UPDATE runs SET hidden = ? WHERE id = ?').bind(hiddenVal, id).run();
  return jsonResponse({ ok: true, run_id: id, hidden: hiddenVal, changes: res.meta?.changes || 0 });
}

async function handleAdminBanPlayer(request, env) {
  let body;
  try {
    body = await request.json();
  } catch {
    return jsonResponse({ ok: false, error: 'Invalid JSON body' }, 400);
  }
  const playerId = String(body?.player_id || '').trim();
  if (!playerId) {
    return jsonResponse({ ok: false, error: 'player_id required' }, 400);
  }
  const reason = String(body?.reason || '').trim().slice(0, 200);
  const shadowban = body?.shadowban === false ? 0 : 1;
  const deleteRuns = Boolean(body?.delete_runs);

  await env.DB.prepare(`
    INSERT INTO banned_players (player_id, reason, shadowban)
    VALUES (?, ?, ?)
    ON CONFLICT(player_id) DO UPDATE SET
      reason = excluded.reason,
      shadowban = excluded.shadowban
  `)
    .bind(playerId, reason, shadowban)
    .run();

  let deletedRuns = 0;
  if (deleteRuns) {
    await env.DB.prepare('DELETE FROM run_songs WHERE run_id IN (SELECT id FROM runs WHERE player_id = ?)').bind(playerId).run();
    const del = await env.DB.prepare('DELETE FROM runs WHERE player_id = ?').bind(playerId).run();
    deletedRuns = del.meta?.changes || 0;
  }

  return jsonResponse({ ok: true, player_id: playerId, shadowban, deleted_runs: deletedRuns });
}

async function handleAdminUnbanPlayer(playerId, env) {
  const pid = String(playerId || '').trim();
  if (!pid) {
    return jsonResponse({ ok: false, error: 'player_id required' }, 400);
  }
  const res = await env.DB.prepare('DELETE FROM banned_players WHERE player_id = ?').bind(pid).run();
  return jsonResponse({ ok: true, unbanned_player_id: pid, changes: res.meta?.changes || 0 });
}

async function handleAdminBanIpFromRun(runId, request, env) {
  const id = Math.floor(Number(runId));
  if (!Number.isFinite(id) || id <= 0) {
    return jsonResponse({ ok: false, error: 'Invalid run id' }, 400);
  }
  let body = {};
  try {
    body = await request.json();
  } catch {
    body = {};
  }
  const row = await env.DB.prepare('SELECT submit_ip_hash FROM runs WHERE id = ?').bind(id).first();
  if (!row || !row.submit_ip_hash) {
    return jsonResponse({ ok: false, error: 'Run or submit_ip_hash not found' }, 404);
  }
  const reason = String(body?.reason || `Run #${id}`).trim().slice(0, 200);
  await env.DB.prepare(`
    INSERT INTO banned_ips (ip_hash, reason)
    VALUES (?, ?)
    ON CONFLICT(ip_hash) DO UPDATE SET reason = excluded.reason
  `)
    .bind(row.submit_ip_hash, reason)
    .run();

  if (body?.delete_run) {
    await env.DB.prepare('DELETE FROM run_songs WHERE run_id = ?').bind(id).run();
    await env.DB.prepare('DELETE FROM runs WHERE id = ?').bind(id).run();
  } else if (body?.hide_run !== false) {
    await env.DB.prepare('UPDATE runs SET hidden = 1 WHERE id = ?').bind(id).run();
  }

  return jsonResponse({ ok: true, banned_ip_hash: row.submit_ip_hash, run_id: id });
}

export default {
  async fetch(request, env) {
    if (request.method === 'OPTIONS') {
      return new Response(null, { status: 204, headers: CORS_HEADERS });
    }

    const url = new URL(request.url);
    const path = url.pathname.replace(/\/+$/, '') || '/';

    try {
      if (request.method === 'GET' && path === '/api/health') {
        await env.DB.prepare('SELECT 1').first();
        return jsonResponse({ ok: true, service: 'beatwanko-api', version: API_VERSION });
      }

      if (path.startsWith('/api/admin/')) {
        if (!(await isAuthorizedAdmin(request, env))) {
          return jsonResponse({ ok: false, error: 'Unauthorized' }, 401);
        }
        if (request.method === 'GET' && path === '/api/admin/summary') {
          return await handleAdminSummary(env);
        }
        const delRunMatch = path.match(/^\/api\/admin\/runs\/(\d+)$/);
        if (request.method === 'DELETE' && delRunMatch) {
          return await handleAdminDeleteRun(delRunMatch[1], env);
        }
        const hideRunMatch = path.match(/^\/api\/admin\/runs\/(\d+)\/hide$/);
        if (request.method === 'POST' && hideRunMatch) {
          return await handleAdminHideRun(hideRunMatch[1], request, env);
        }
        if (request.method === 'POST' && path === '/api/admin/bans/player') {
          return await handleAdminBanPlayer(request, env);
        }
        const unbanPlayerMatch = path.match(/^\/api\/admin\/bans\/player\/([^/]+)$/);
        if (request.method === 'DELETE' && unbanPlayerMatch) {
          return await handleAdminUnbanPlayer(decodeURIComponent(unbanPlayerMatch[1]), env);
        }
        const banIpMatch = path.match(/^\/api\/admin\/bans\/ip-from-run\/(\d+)$/);
        if (request.method === 'POST' && banIpMatch) {
          return await handleAdminBanIpFromRun(banIpMatch[1], request, env);
        }
        return jsonResponse({ ok: false, error: 'Admin endpoint not found' }, 404);
      }

      if (request.method === 'POST' && path === '/api/runs') {
        return await handleSubmitRun(request, env);
      }

      if (request.method === 'GET' && path === '/api/leaderboard') {
        return await handleGetLeaderboard(url, env);
      }

      const playlistMatch = path.match(/^\/api\/runs\/(\d+)\/playlist\.bplist$/);
      if (request.method === 'GET' && playlistMatch) {
        return await handleGetRunPlaylist(playlistMatch[1], env);
      }

      const runMatch = path.match(/^\/api\/runs\/(\d+)$/);
      if (request.method === 'GET' && runMatch) {
        return await handleGetRunDetail(runMatch[1], env);
      }

      const playerRunsMatch = path.match(/^\/api\/players\/([^/]+)\/runs$/);
      if (request.method === 'GET' && playerRunsMatch) {
        return await handleGetPlayerRuns(decodeURIComponent(playerRunsMatch[1]), url, env);
      }

      return jsonResponse({ ok: false, error: 'Not Found' }, 404);
    } catch (err) {
      return jsonResponse({ ok: false, error: err?.message || 'Internal Server Error' }, 500);
    }
  },
};
