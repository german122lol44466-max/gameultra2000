// Скачивание аддонов Garry's Mod из Steam Workshop анонимно (как steamcmd), без установки GMod.
//   npm install steam-user@5
//   node Tools/SourceImport/workshop_download.js <папка> 3658522900 3468429737 183549197 ...
// Старые предметы с прямой ссылкой (file_url) качаются по HTTP, новые — из CDN Steam по манифесту.
const SteamUser = require('steam-user');
const fs = require('fs');
const path = require('path');
const https = require('https');

const out = process.argv[2];
const ids = process.argv.slice(3);
if (!out || ids.length === 0) { console.log('node workshop_download.js <папка> <id> [id...]'); process.exit(1); }

function get(url, file) {
  return new Promise((ok, fail) => {
    https.get(url, res => {
      if (res.statusCode >= 300 && res.statusCode < 400 && res.headers.location) return get(res.headers.location, file).then(ok, fail);
      const w = fs.createWriteStream(file);
      res.pipe(w);
      w.on('finish', () => w.close(ok));
    }).on('error', fail);
  });
}

const opts = { webCompatibilityMode: true, protocol: SteamUser.EConnectionProtocol.WebSocket };
if (process.env.HTTPS_PROXY) opts.httpProxy = process.env.HTTPS_PROXY;
const c = new SteamUser(opts);
c.on('error', e => { console.error('Ошибка Steam:', e.message); process.exit(1); });
c.on('loggedOn', async () => {
  console.log('Вход в Steam (анонимно) выполнен');
  for (const id of ids) {
    const r = await c.getPublishedFileDetails([id]);
    const f = r.files[id];
    const dir = path.join(out, id);
    fs.mkdirSync(dir, { recursive: true });
    console.log(`${id}: ${f.title} (${(f.file_size / 1048576).toFixed(0)} МБ)`);
    try {
      if (f.file_url) { await get(f.file_url, path.join(dir, 'addon.gma')); continue; }
      const m = await c.getManifest(f.consumer_appid || 4000, f.consumer_appid || 4000, f.hcontent_file, 'public');
      for (const file of m.manifest.files) {
        if (file.flags & 64) continue;       // папка
        const p = path.join(dir, file.filename.replace(/\\/g, '/'));
        fs.mkdirSync(path.dirname(p), { recursive: true });
        await c.downloadFile(f.consumer_appid || 4000, f.consumer_appid || 4000, file, p);
      }
    } catch (e) { console.log('  не удалось:', e.message); }
  }
  process.exit(0);
});
c.logOn({ anonymous: true });
