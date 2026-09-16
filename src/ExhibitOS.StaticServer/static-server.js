/**
 * ExhibitOS Bundled Static Web Server
 * Zero external dependencies, pure Node.js HTTP server.
 */

const http = require('http');
const fs = require('fs');
const path = require('path');
const url = require('url');

// Parse CLI arguments: --port <port> --dir <dir>
const args = process.argv.slice(2);
let port = 0;
let baseDir = process.cwd();

for (let i = 0; i < args.length; i++) {
  if (args[i] === '--port' && args[i + 1]) {
    port = parseInt(args[i + 1], 10);
    i++;
  } else if (args[i] === '--dir' && args[i + 1]) {
    baseDir = path.resolve(args[i + 1]);
    i++;
  }
}

const MIME_TYPES = {
  '.html': 'text/html; charset=utf-8',
  '.htm': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.mjs': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.png': 'image/png',
  '.jpg': 'image/jpeg',
  '.jpeg': 'image/jpeg',
  '.gif': 'image/gif',
  '.svg': 'image/svg+xml',
  '.webp': 'image/webp',
  '.ico': 'image/x-icon',
  '.wav': 'audio/wav',
  '.mp3': 'audio/mpeg',
  '.mp4': 'video/mp4',
  '.webm': 'video/webm',
  '.woff': 'font/woff',
  '.woff2': 'font/woff2',
  '.ttf': 'font/ttf',
  '.wasm': 'application/wasm'
};

const server = http.createServer((req, res) => {
  const parsedUrl = url.parse(req.url);
  let pathname;
  try {
    pathname = decodeURIComponent(parsedUrl.pathname);
  } catch {
    res.writeHead(400, { 'Content-Type': 'text/plain' });
    res.end('400 Bad Request');
    return;
  }

  // Normalize path to prevent directory traversal
  let safePath = path.resolve(baseDir, `.${pathname}`);
  const relativePath = path.relative(baseDir, safePath);
  if (relativePath.startsWith('..' + path.sep) || path.isAbsolute(relativePath)) {
    res.writeHead(403, { 'Content-Type': 'text/plain' });
    res.end('403 Forbidden');
    return;
  }

  // If path is a directory, look for index.html
  fs.stat(safePath, (err, stats) => {
    if (err) {
      res.writeHead(404, { 'Content-Type': 'text/plain' });
      res.end('404 Not Found');
      return;
    }

    if (stats.isDirectory()) {
      safePath = path.join(safePath, 'index.html');
    }

    fs.readFile(safePath, (readErr, data) => {
      if (readErr) {
        res.writeHead(404, { 'Content-Type': 'text/plain' });
        res.end('404 Not Found');
        return;
      }

      const ext = path.extname(safePath).toLowerCase();
      const contentType = MIME_TYPES[ext] || 'application/octet-stream';

      res.writeHead(200, {
        'Content-Type': contentType,
        'Cache-Control': 'no-cache',
        'Access-Control-Allow-Origin': '*'
      });
      res.end(data);
    });
  });
});

server.listen(port, '127.0.0.1', () => {
  const assignedPort = server.address().port;
  console.log(`EXHIBITOS_STATIC_SERVER_LISTENING:${assignedPort}`);
  console.log(`Serving ${baseDir} on http://127.0.0.1:${assignedPort}`);
});
