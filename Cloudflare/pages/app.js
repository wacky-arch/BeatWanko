(() => {
  const MODE_RULES = {
    unconditional: '☠️ 無条件ランキング：フィルタなしの完全ランダム抽選（全モディファイア OFF 固定・Fail で終了）',
    stepup: '⭐️ SS⭐️ステップアップ：ScoreSaber ★0.00〜1.99〜★14台へ1曲ずつ昇格（全モディファイア OFF 固定・14曲完走で終了）',
    endurance: '💪 耐久ランキング：NPS 8.0 以下のみ出題＋曲中ポーズ完全禁止（全モディファイア OFF 固定・Fail で終了）',
  };

  const MODE_LABELS = {
    unconditional: '無条件ランキング',
    stepup: 'SS⭐️ステップアップ',
    endurance: '耐久ランキング',
  };

  const DEMO_RUNS = {
    unconditional: {
      entries: [
        {
          run_id: 101,
          rank: 1,
          player_id: '76561198000000001',
          display_name: 'PlayerA (Demo)',
          avatar_url: '',
          profile_source: 'scoresaber',
          profile_url: '',
          mode: 'unconditional',
          played_at: '2026-11-22T20:10:00Z',
          total_play_seconds: 1245,
          end_reason: 'fail',
          cleared_count: 8,
          total_score: 7124890,
          run_count: 3,
          songs: [
            { song_order: 1, song_hash: '2FDDB', song_name: 'Moonlit / seatrus', difficulty: 'Expert', characteristic: 'Standard', score: 942110, accuracy_percent: 96.2, cleared: 1, stars: 5.81, step_band: 0 },
            { song_order: 2, song_hash: '1AABB', song_name: 'Sample Track #2', difficulty: 'ExpertPlus', characteristic: 'Standard', score: 1088400, accuracy_percent: 93.8, cleared: 1, stars: 8.42, step_band: 0 },
            { song_order: 3, song_hash: '3CCDD', song_name: 'Boss Map (Failed)', difficulty: 'ExpertPlus', characteristic: 'Standard', score: 312000, accuracy_percent: 78.4, cleared: 0, stars: 11.2, step_band: 0 },
          ],
        },
        {
          run_id: 102,
          rank: 2,
          player_id: '76561198000000002',
          display_name: 'PlayerB (Demo)',
          avatar_url: '',
          profile_source: 'beatleader',
          profile_url: '',
          mode: 'unconditional',
          played_at: '2026-11-22T19:45:00Z',
          total_play_seconds: 980,
          end_reason: 'close_lid',
          cleared_count: 6,
          total_score: 5412300,
          run_count: 1,
          songs: [
            { song_order: 1, song_hash: '4EEFF', song_name: 'Demo Clear #1', difficulty: 'Hard', characteristic: 'Standard', score: 780100, accuracy_percent: 94.5, cleared: 1, stars: 3.9, step_band: 0 },
          ],
        },
      ],
    },
    stepup: {
      entries: [
        {
          run_id: 201,
          rank: 1,
          player_id: '76561198000000001',
          display_name: 'PlayerA (Demo)',
          avatar_url: '',
          profile_source: 'scoresaber',
          profile_url: '',
          mode: 'stepup',
          played_at: '2026-11-22T20:25:00Z',
          total_play_seconds: 2130,
          end_reason: 'complete',
          cleared_count: 14,
          total_score: 12940500,
          run_count: 2,
          songs: [
            { song_order: 1, song_hash: 'AA111', song_name: 'Step 1 Map', difficulty: 'Normal', characteristic: 'Standard', score: 520100, accuracy_percent: 98.1, cleared: 1, stars: 1.45, step_band: 1 },
            { song_order: 2, song_hash: 'BB222', song_name: 'Step 14 Final Boss', difficulty: 'ExpertPlus', characteristic: 'Standard', score: 1340500, accuracy_percent: 87.3, cleared: 1, stars: 14.32, step_band: 14 },
          ],
        },
      ],
    },
    endurance: {
      entries: [
        {
          run_id: 301,
          rank: 1,
          player_id: '76561198000000002',
          display_name: 'PlayerB (Demo)',
          avatar_url: '',
          profile_source: 'beatleader',
          profile_url: '',
          mode: 'endurance',
          played_at: '2026-11-22T18:30:00Z',
          total_play_seconds: 3840,
          end_reason: 'fail',
          cleared_count: 18,
          total_score: 15820400,
          run_count: 1,
          songs: [
            { song_order: 1, song_hash: 'CC333', song_name: 'Endurance Groove #1', difficulty: 'Expert', characteristic: 'Standard', score: 891200, accuracy_percent: 95.6, cleared: 1, stars: 0, step_band: 0 },
            { song_order: 2, song_hash: 'DD444', song_name: 'Endurance Final (Failed)', difficulty: 'Expert', characteristic: 'Standard', score: 410000, accuracy_percent: 81.2, cleared: 0, stars: 0, step_band: 0 },
          ],
        },
      ],
    },
  };

  const urlParams = new URLSearchParams(window.location.search);
  const configuredApi = (urlParams.get('api') || window.BEATWANKO_CONFIG?.API_BASE_URL || '').trim().replace(/\/+$/, '');

  let currentMode = ['unconditional', 'stepup', 'endurance'].includes(urlParams.get('mode'))
    ? urlParams.get('mode')
    : 'unconditional';
  let useDemo = urlParams.get('demo') === '1' || !configuredApi;
  let activeRunForDownload = null;

  const els = {
    body: document.getElementById('leaderboard-body'),
    ruleText: document.getElementById('mode-rule-text'),
    statPlayers: document.getElementById('stat-players'),
    statTopCups: document.getElementById('stat-top-cups'),
    statusBox: document.getElementById('status-box'),
    demoPill: document.getElementById('demo-pill'),
    btnToggleDemo: document.getElementById('btn-toggle-demo'),
    btnRefresh: document.getElementById('btn-refresh'),
    runModal: document.getElementById('run-modal'),
    runModalTitle: document.getElementById('run-modal-title'),
    runModalSub: document.getElementById('run-modal-sub'),
    runSummaryBar: document.getElementById('run-summary-bar'),
    runSongsBody: document.getElementById('run-songs-body'),
    btnDownloadBplist: document.getElementById('btn-download-bplist'),
    historyModal: document.getElementById('history-modal'),
    historyModalTitle: document.getElementById('history-modal-title'),
    historyModalSub: document.getElementById('history-modal-sub'),
    historyList: document.getElementById('history-list'),
  };

  function escapeHtml(value) {
    return String(value ?? '')
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#39;');
  }

  function formatDuration(sec) {
    const total = Math.max(0, Math.floor(Number(sec) || 0));
    const h = Math.floor(total / 3600);
    const m = Math.floor((total % 3600) / 60);
    const s = total % 60;
    if (h > 0) return `${h}時間${String(m).padStart(2, '0')}分${String(s).padStart(2, '0')}秒`;
    return `${m}分${String(s).padStart(2, '0')}秒`;
  }

  function formatDate(iso) {
    const d = new Date(iso);
    if (Number.isNaN(d.getTime())) return String(iso || '');
    return d.toLocaleString('ja-JP', {
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
    });
  }

  function formatEndReasonBadge(reason, mode, clearedCount) {
    if (reason === 'complete' || (mode === 'stepup' && Number(clearedCount) >= 14)) {
      return '<span class="badge badge-complete">👑 14曲完走</span>';
    }
    if (reason === 'close_lid') {
      return '<span class="badge badge-lid">蓋閉じ記録</span>';
    }
    return '<span class="badge badge-fail">Fail終了</span>';
  }

  function sourceLabel(src) {
    if (src === 'scoresaber') return 'ScoreSaber';
    if (src === 'beatleader') return 'BeatLeader';
    return 'Steam/Platform';
  }

  function setStatus(message) {
    if (!message) {
      els.statusBox.classList.add('hidden');
      els.statusBox.textContent = '';
      return;
    }
    els.statusBox.textContent = message;
    els.statusBox.classList.remove('hidden');
  }

  function syncUiHeader() {
    document.querySelectorAll('.tab-btn').forEach((btn) => {
      btn.classList.toggle('active', btn.dataset.mode === currentMode);
    });
    els.ruleText.textContent = MODE_RULES[currentMode] || '';
    els.demoPill.classList.toggle('hidden', !useDemo);
    els.btnToggleDemo.classList.toggle('hidden', !configuredApi);
    els.btnToggleDemo.textContent = useDemo ? '本番APIに戻る' : 'サンプルデータで確認';
  }

  async function fetchJson(path) {
    const res = await fetch(`${configuredApi}${path}`, { cache: 'no-store' });
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    return await res.json();
  }

  function renderLeaderboard(entries) {
    els.statPlayers.textContent = String(entries.length);
    const maxCups = entries.reduce((m, e) => Math.max(m, Number(e.cleared_count) || 0), 0);
    els.statTopCups.textContent = String(maxCups);

    if (!entries.length) {
      els.body.innerHTML = '<tr><td colspan="7" class="empty-cell">まだこのモードの記録がありません。最初の挑戦者になろう！</td></tr>';
      return;
    }

    els.body.innerHTML = entries
      .map((entry) => {
        const rankCls = entry.rank <= 3 ? `rank-${entry.rank}` : 'rank-other';
        const avatarHtml = entry.avatar_url
          ? `<img class="avatar" src="${escapeHtml(entry.avatar_url)}" alt="" loading="lazy" />`
          : `<span class="avatar-placeholder">${escapeHtml((entry.display_name || '?').slice(0, 1).toUpperCase())}</span>`;
        const nameHtml = entry.profile_url
          ? `<a class="player-name" href="${escapeHtml(entry.profile_url)}" target="_blank" rel="noopener noreferrer">${escapeHtml(entry.display_name)}</a>`
          : `<span class="player-name">${escapeHtml(entry.display_name)}</span>`;
        const runCountText = Number(entry.run_count) > 1 ? `・全${entry.run_count}件` : '';

        return `
          <tr>
            <td><span class="rank-badge ${rankCls}">${entry.rank}</span></td>
            <td>
              <div class="player-cell">
                ${avatarHtml}
                <div class="player-name-wrap">
                  ${nameHtml}
                  <span class="player-meta">${escapeHtml(sourceLabel(entry.profile_source))}${escapeHtml(runCountText)}</span>
                </div>
              </div>
            </td>
            <td>
              <span class="cups-main">${Number(entry.cleared_count)}杯</span>
              ${formatEndReasonBadge(entry.end_reason, entry.mode, entry.cleared_count)}
            </td>
            <td class="score-cell">${Number(entry.total_score).toLocaleString('ja-JP')}</td>
            <td>${escapeHtml(formatDuration(entry.total_play_seconds))}</td>
            <td>${escapeHtml(formatDate(entry.played_at))}</td>
            <td>
              <div class="action-group">
                <button class="btn btn-sm btn-primary js-open-run" data-run-id="${entry.run_id}" type="button">譜面 / .bplist</button>
                <button class="btn btn-sm btn-secondary js-open-history" data-player-id="${escapeHtml(entry.player_id)}" data-player-name="${escapeHtml(entry.display_name)}" type="button">過去履歴</button>
              </div>
            </td>
          </tr>
        `;
      })
      .join('');
  }

  async function loadLeaderboard() {
    syncUiHeader();
    els.body.innerHTML = '<tr><td colspan="7" class="empty-cell">読み込み中...</td></tr>';

    if (useDemo) {
      if (!configuredApi) {
        setStatus('API URL 未設定のためサンプルデータを表示しています（Cloudflare Workers デプロイ後に pages/config.js の API_BASE_URL を設定してください）。');
      } else {
        setStatus('');
      }
      renderLeaderboard(DEMO_RUNS[currentMode]?.entries || []);
      return;
    }

    setStatus('');
    try {
      const data = await fetchJson(`/api/leaderboard?mode=${encodeURIComponent(currentMode)}&limit=100`);
      renderLeaderboard(data.entries || []);
    } catch (err) {
      setStatus(`ランキングの取得に失敗しました（${err.message}）。`);
      els.body.innerHTML = '<tr><td colspan="7" class="empty-cell">データを読み込めませんでした。</td></tr>';
    }
  }

  function findDemoRunById(runId) {
    for (const mode of Object.keys(DEMO_RUNS)) {
      const hit = DEMO_RUNS[mode].entries.find((e) => Number(e.run_id) === Number(runId));
      if (hit) return hit;
    }
    return null;
  }

  function openModal(id) {
    document.getElementById(id).classList.remove('hidden');
  }

  function closeModal(id) {
    document.getElementById(id).classList.add('hidden');
  }

  async function openRunModal(runId) {
    openModal('run-modal');
    els.runSongsBody.innerHTML = '<tr><td colspan="7" class="empty-cell">読み込み中...</td></tr>';

    let run;
    let songs;
    if (useDemo) {
      run = findDemoRunById(runId);
      songs = run?.songs || [];
    } else {
      try {
        const res = await fetchJson(`/api/runs/${encodeURIComponent(runId)}`);
        run = res.run;
        songs = res.songs || [];
      } catch (err) {
        els.runSongsBody.innerHTML = `<tr><td colspan="7" class="empty-cell">取得失敗：${escapeHtml(err.message)}</td></tr>`;
        return;
      }
    }

    if (!run) {
      els.runSongsBody.innerHTML = '<tr><td colspan="7" class="empty-cell">ランが見つかりませんでした。</td></tr>';
      return;
    }

    activeRunForDownload = { run, songs };
    els.runModalTitle.textContent = `${run.display_name} — ${MODE_LABELS[run.mode] || run.mode}`;
    els.runModalSub.textContent = `${formatDate(run.played_at)} ／ プレイ時間：${formatDuration(run.total_play_seconds)}`;
    els.runSummaryBar.innerHTML = `
      <span>クリア曲数：<strong>${Number(run.cleared_count)}杯</strong> ${formatEndReasonBadge(run.end_reason, run.mode, run.cleared_count)}</span>
      <span>合計スコア：<strong>${Number(run.total_score).toLocaleString('ja-JP')}</strong></span>
      <span>プレイ曲数（Fail含む）：<strong>${songs.length}曲</strong></span>
    `;

    els.runSongsBody.innerHTML = songs
      .map((s) => {
        const starText = Number(s.stars) > 0 ? `★${Number(s.stars).toFixed(2)}` : '-';
        const statusBadge = s.cleared
          ? '<span class="badge badge-clear">CLEAR</span>'
          : '<span class="badge badge-fail">FAIL</span>';
        return `
          <tr>
            <td>${Number(s.song_order)}</td>
            <td>${escapeHtml(s.song_name)}</td>
            <td>${escapeHtml(s.difficulty)}</td>
            <td>${escapeHtml(starText)}</td>
            <td class="score-cell">${Number(s.score).toLocaleString('ja-JP')}</td>
            <td>${Number(s.accuracy_percent).toFixed(1)}%</td>
            <td>${statusBadge}</td>
          </tr>
        `;
      })
      .join('');
  }

  async function openPlayerHistory(playerId, playerName) {
    openModal('history-modal');
    els.historyModalTitle.textContent = `${playerName} の過去ラン履歴`;
    els.historyModalSub.textContent = `モード：${MODE_LABELS[currentMode] || currentMode}`;
    els.historyList.innerHTML = '<div class="empty-cell">読み込み中...</div>';

    let runs;
    if (useDemo) {
      runs = (DEMO_RUNS[currentMode]?.entries || []).filter((e) => e.player_id === playerId);
    } else {
      try {
        const res = await fetchJson(`/api/players/${encodeURIComponent(playerId)}/runs?mode=${encodeURIComponent(currentMode)}&limit=50`);
        runs = res.runs || [];
      } catch (err) {
        els.historyList.innerHTML = `<div class="empty-cell">取得失敗：${escapeHtml(err.message)}</div>`;
        return;
      }
    }

    if (!runs.length) {
      els.historyList.innerHTML = '<div class="empty-cell">保存された過去ランはありません。</div>';
      return;
    }

    els.historyList.innerHTML = runs
      .map((r, idx) => `
        <div class="history-item">
          <div>
            <div class="history-item-title">
              ${Number(r.cleared_count)}杯 / 合計 ${Number(r.total_score).toLocaleString('ja-JP')} pt
              ${idx === 0 ? '<span class="badge badge-complete">自己ベスト</span>' : ''}
              ${formatEndReasonBadge(r.end_reason, r.mode, r.cleared_count)}
            </div>
            <div class="history-item-meta">${escapeHtml(formatDate(r.played_at))} ／ 時間：${escapeHtml(formatDuration(r.total_play_seconds))}</div>
          </div>
          <button class="btn btn-sm btn-primary js-open-run" data-run-id="${r.run_id || r.id}" type="button">譜面詳細 / .bplist</button>
        </div>
      `)
      .join('');
  }

  function downloadActivePlaylist() {
    if (!activeRunForDownload) return;
    const { run, songs } = activeRunForDownload;
    if (!useDemo && configuredApi) {
      window.location.href = `${configuredApi}/api/runs/${encodeURIComponent(run.id || run.run_id)}/playlist.bplist`;
      return;
    }
    const seen = new Set();
    const bplistSongs = [];
    for (const s of songs) {
      const hash = String(s.song_hash || '').toUpperCase();
      if (!hash || seen.has(hash)) continue;
      seen.add(hash);
      bplistSongs.push({
        hash,
        songName: s.song_name,
        difficulties: [{ characteristic: s.characteristic || 'Standard', name: s.difficulty }],
      });
    }
    const payload = {
      playlistTitle: `BeatWanko [${MODE_LABELS[run.mode] || run.mode}] - ${run.display_name} (${run.cleared_count}杯)`,
      playlistAuthor: 'BeatWanko Leaderboard',
      playlistDescription: `Played on ${run.played_at} | Cleared: ${run.cleared_count} | Total Score: ${run.total_score}`,
      songs: bplistSongs,
    };
    const blob = new Blob([JSON.stringify(payload, null, 2)], { type: 'application/octet-stream' });
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = `beatwanko_${run.mode}_run${run.id || run.run_id}.bplist`;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    setTimeout(() => URL.revokeObjectURL(a.href), 1000);
  }

  document.querySelectorAll('.tab-btn').forEach((btn) => {
    btn.addEventListener('click', () => {
      currentMode = btn.dataset.mode;
      loadLeaderboard();
    });
  });

  els.btnRefresh.addEventListener('click', loadLeaderboard);
  els.btnToggleDemo.addEventListener('click', () => {
    useDemo = !useDemo;
    loadLeaderboard();
  });
  els.btnDownloadBplist.addEventListener('click', downloadActivePlaylist);

  document.addEventListener('click', (ev) => {
    const closeTarget = ev.target.closest('[data-close-modal]');
    if (closeTarget) {
      closeModal(closeTarget.dataset.closeModal);
      return;
    }
    const runBtn = ev.target.closest('.js-open-run');
    if (runBtn) {
      closeModal('history-modal');
      openRunModal(runBtn.dataset.runId);
      return;
    }
    const histBtn = ev.target.closest('.js-open-history');
    if (histBtn) {
      openPlayerHistory(histBtn.dataset.playerId, histBtn.dataset.playerName);
    }
  });

  loadLeaderboard();
})();
