(() => {
  const MODE_RULES = {
    unconditional: '☠️ 無条件ランキング：フィルタなしの完全ランダム抽選（全モディファイア OFF 固定・Fail で終了）',
    stepup: '⭐️ SS⭐️ステップアップ：ScoreSaber ★0.00〜1.99〜★14台へ1曲ずつ昇格（全モディファイア OFF 固定・Fail または 14曲完走で終了）',
    endurance: '💪 耐久ランキング：NPS 8.0 以下のみ出題＋曲中ポーズ完全禁止（全モディファイア OFF 固定・Fail で終了）',
  };

  const MODE_LABELS = {
    unconditional: '無条件ランキング',
    stepup: 'SS⭐️ステップアップ',
    endurance: '耐久ランキング',
  };

  const urlParams = new URLSearchParams(window.location.search);
  const configuredApi = (urlParams.get('api') || window.BEATWANKO_CONFIG?.API_BASE_URL || '').trim().replace(/\/+$/, '');

  let currentMode = ['unconditional', 'stepup', 'endurance'].includes(urlParams.get('mode'))
    ? urlParams.get('mode')
    : 'unconditional';
  let activeRunForDownload = null;

  const els = {
    body: document.getElementById('leaderboard-body'),
    ruleText: document.getElementById('mode-rule-text'),
    statPlayers: document.getElementById('stat-players'),
    statTopCups: document.getElementById('stat-top-cups'),
    statusBox: document.getElementById('status-box'),
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

    if (!configuredApi) {
      setStatus('API URL が設定されていません。');
      els.body.innerHTML = '<tr><td colspan="7" class="empty-cell">データを読み込めませんでした。</td></tr>';
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
    try {
      const res = await fetchJson(`/api/runs/${encodeURIComponent(runId)}`);
      run = res.run;
      songs = res.songs || [];
    } catch (err) {
      els.runSongsBody.innerHTML = `<tr><td colspan="7" class="empty-cell">取得失敗：${escapeHtml(err.message)}</td></tr>`;
      return;
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
    try {
      const res = await fetchJson(`/api/players/${encodeURIComponent(playerId)}/runs?mode=${encodeURIComponent(currentMode)}&limit=50`);
      runs = res.runs || [];
    } catch (err) {
      els.historyList.innerHTML = `<div class="empty-cell">取得失敗：${escapeHtml(err.message)}</div>`;
      return;
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
    if (!activeRunForDownload || !configuredApi) return;
    const { run } = activeRunForDownload;
    window.location.href = `${configuredApi}/api/runs/${encodeURIComponent(run.id || run.run_id)}/playlist.bplist`;
  }

  document.querySelectorAll('.tab-btn').forEach((btn) => {
    btn.addEventListener('click', () => {
      currentMode = btn.dataset.mode;
      loadLeaderboard();
    });
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
