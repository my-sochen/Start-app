const https = require("https");
const fs = require("fs");
const path = require("path");
const ROOT = process.env.SITE_ROOT || require("path").join(__dirname, "site");
const CERT_DIR = process.env.CERT_DIR || require("path").join(__dirname, "certs");
const PORT = Number(process.env.PORT || 8443);
const MIME = { ".html": "text/html; charset=utf-8", ".css": "text/css", ".js": "text/javascript", ".json": "application/json", ".png": "image/png", ".jpg": "image/jpeg", ".webp": "image/webp", ".svg": "image/svg+xml" };
https.createServer({ key: fs.readFileSync(require("path").join(CERT_DIR, "key.pem")), cert: fs.readFileSync(require("path").join(CERT_DIR, "cert.pem")) }, (req, res) => {
  let p = decodeURIComponent(req.url.split("?")[0]);
  if (p === "/") p = "/index.html";
  const f = path.join(ROOT, path.normalize(p).replace(/^(\.\.[/\\])+/, ""));
  fs.readFile(f, (err, data) => {
    if (err) { res.writeHead(404); res.end("not found"); return; }
    res.writeHead(200, { "content-type": MIME[path.extname(f)] || "application/octet-stream", "cache-control": "no-store" });
    res.end(data);
  });
}).listen(PORT, "127.0.0.1", () => console.log(`https fixture server on ${PORT}, root ${ROOT}`));
