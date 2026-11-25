# BeatWanko Cloudflare Workers + D1 + Pages デプロイ手順

このフォルダ一式だけで、BeatWanko リーダーボード用の API（Workers + D1）と Web 画面（Pages）を無料枠で構築できます。

## 構成
- `migrations/0001_init.sql`: D1 (SQLite) テーブル定義（`players`, `runs`, `run_songs`）
- `wrangler.toml`: Cloudflare Workers & D1 設定
- `worker/src/index.js`: API 実装（`/api/runs`, `/api/leaderboard`, `/api/runs/:id`, `/api/players/:id/runs`, `/api/runs/:id/playlist.bplist`）
- `pages/`: 静的リーダーボード Web サイト（ビルド不要でそのまま Cloudflare Pages にデプロイ可能）

---

## デプロイ手順

### 1. D1 データベース作成とマイグレーション
```powershell
cd C:\BSManager\BSInstances\1.40.8\ModSource\BeatWanko\Cloudflare
npx wrangler login
npx wrangler d1 create beatwanko-db
```
表示された `database_id` を `wrangler.toml` の `REPLACE_WITH_YOUR_D1_DATABASE_ID` に設定し、マイグレーションを適用します：
```powershell
npx wrangler d1 migrations apply beatwanko-db --remote
```

### 2. Worker API デプロイ
```powershell
npx wrangler deploy
```
発行された URL（例：`https://beatwanko-api.<アカウント>.workers.dev`）を確認します。
確認：`https://beatwanko-api.<アカウント>.workers.dev/api/health` が `{ "ok": true }` を返せばOKです。

### 3. Pages（リーダーボード Web UI）デプロイ
`pages/config.js` の `API_BASE_URL` に上記 Worker URL を設定してからデプロイします：
```powershell
npx wrangler pages project create beatwanko-board --production-branch main
npx wrangler pages deploy pages --project-name beatwanko-board
```

### 4. BeatWanko MOD 側の接続先設定
`Beat Saber\UserData\BeatWanko.json` の `"LeaderboardApiUrl"` に Worker URL（例：`"https://beatwanko-api.<アカウント>.workers.dev"`）を設定するか、`PluginConfig.cs` のデフォルト値に埋め込んでビルドしてください。
