/**
 * Pulse & Telemetry: Living System
 * ExhibitOS Node.js Backend Artwork
 * Pure standard library (http, fs, path, os, url) — zero external npm dependencies.
 */

const http = require('http');
const fs = require('fs');
const path = require('path');
const os = require('os');
const url = require('url');

// Configuration from ExhibitOS or defaults
const PORT = parseInt(process.env.PORT, 10) || 3000;
const HOST = process.env.HOST || '127.0.0.1';
const PUBLIC_DIR = path.join(__dirname, 'public');
const DATA_DIR = path.join(__dirname, 'data');
const DATA_FILE = path.join(DATA_DIR, 'interactions.json');

// Ensure data directory exists
if (!fs.existsSync(DATA_DIR)) {
  fs.mkdirSync(DATA_DIR, { recursive: true });
}

// In-memory interaction buffer & persistence state
let interactions = [];
if (fs.existsSync(DATA_FILE)) {
  try {
    interactions = JSON.parse(fs.readFileSync(DATA_FILE, 'utf8'));
    if (!Array.isArray(interactions)) interactions = [];
  } catch (err) {
    console.error('Failed to parse existing interactions file, initializing fresh:', err.message);
    interactions = [];
  }
}

function saveInteractions() {
  try {
    // Keep the most recent 100 interactions
    if (interactions.length > 100) {
      interactions = interactions.slice(-100);
    }
    fs.writeFileSync(DATA_FILE, JSON.stringify(interactions, null, 2), 'utf8');
  } catch (err) {
    console.error('Failed to save interactions:', err.message);
  }
}

// Connected SSE clients
const sseClients = new Set();

function broadcastEvent(type, payload) {
  const message = `data: ${JSON.stringify({ type, payload, timestamp: Date.now() })}\n\n`;
  for (const res of sseClients) {
    try {
      res.write(message);
    } catch {
      sseClients.delete(res);
    }
  }
}

// MIME types dictionary for static files
const MIME_TYPES = {
  '.html': 'text/html; charset=utf-8',
  '.htm': 'text/html; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.png': 'image/png',
  '.jpg': 'image/jpeg',
  '.jpeg': 'image/jpeg',
  '.svg': 'image/svg+xml',
  '.ico': 'image/x-icon',
  '.txt': 'text/plain; charset=utf-8'
};

const server = http.createServer((req, res) => {
  const parsedUrl = url.parse(req.url, true);
  const pathname = parsedUrl.pathname;

  // CORS headers for local kiosk access
  res.setHeader('Access-Control-Allow-Origin', '*');
  res.setHeader('Access-Control-Allow-Methods', 'GET, POST, OPTIONS');
  res.setHeader('Access-Control-Allow-Headers', 'Content-Type');

  if (req.method === 'OPTIONS') {
    res.writeHead(204);
    res.end();
    return;
  }

  // API: Health / Telemetry status (serves readiness probe and client telemetry)
  if (pathname === '/api/status' && req.method === 'GET') {
    const mem = process.memoryUsage();
    const statusData = {
      status: 'ok',
      artwork: 'Pulse & Telemetry',
      version: '1.0.0',
      uptimeSeconds: Math.floor(process.uptime()),
      system: {
        hostname: os.hostname(),
        platform: os.platform(),
        arch: os.arch(),
        cpuCount: os.cpus().length,
        freeMemMB: Math.round(os.freemem() / (1024 * 1024)),
        totalMemMB: Math.round(os.totalmem() / (1024 * 1024))
      },
      process: {
        pid: process.pid,
        nodeVersion: process.version,
        rssMB: Math.round(mem.rss / (1024 * 1024)),
        heapUsedMB: Math.round(mem.heapUsed / (1024 * 1024)),
        port: PORT,
        host: HOST
      },
      stats: {
        activeSseClients: sseClients.size,
        totalInteractions: interactions.length
      },
      recentInteractions: interactions.slice(-10)
    };

    res.writeHead(200, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify(statusData, null, 2));
    return;
  }

  // API: Server-Sent Events (SSE) Stream
  if (pathname === '/api/stream' && req.method === 'GET') {
    res.writeHead(200, {
      'Content-Type': 'text/event-stream',
      'Cache-Control': 'no-cache',
      'Connection': 'keep-alive'
    });

    sseClients.add(res);
    res.write(`data: ${JSON.stringify({ type: 'connected', clients: sseClients.size })}\n\n`);

    req.on('close', () => {
      sseClients.delete(res);
    });
    return;
  }

  // API: Submit Visitor Interaction
  if (pathname === '/api/interact' && req.method === 'POST') {
    let body = '';
    req.on('data', chunk => {
      body += chunk;
      // Prevent oversized payloads
      if (body.length > 10000) {
        req.destroy();
      }
    });

    req.on('end', () => {
      try {
        const payload = JSON.parse(body || '{}');
        const interactionRecord = {
          id: Date.now() + '-' + Math.random().toString(36).slice(2, 7),
          type: payload.type || 'vortex',
          x: payload.x ?? 0.5,
          y: payload.y ?? 0.5,
          color: payload.color || '#38bdf8',
          note: (payload.note || '').slice(0, 100),
          createdAt: new Date().toISOString()
        };

        interactions.push(interactionRecord);
        saveInteractions();
        broadcastEvent('interaction', interactionRecord);

        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({ success: true, record: interactionRecord }));
      } catch (err) {
        res.writeHead(400, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({ error: 'Invalid JSON payload' }));
      }
    });
    return;
  }

  // Serve static files from public/
  let filePath = path.join(PUBLIC_DIR, pathname === '/' ? 'index.html' : pathname);

  // Directory traversal protection
  const relPath = path.relative(PUBLIC_DIR, filePath);
  if (relPath.startsWith('..' + path.sep) || path.isAbsolute(relPath)) {
    res.writeHead(403, { 'Content-Type': 'text/plain' });
    res.end('403 Forbidden');
    return;
  }

  fs.stat(filePath, (err, stats) => {
    if (err || !stats.isFile()) {
      // Fallback to index.html for SPA-like navigation or 404
      if (pathname !== '/' && fs.existsSync(path.join(PUBLIC_DIR, 'index.html'))) {
        filePath = path.join(PUBLIC_DIR, 'index.html');
      } else {
        res.writeHead(404, { 'Content-Type': 'text/plain' });
        res.end('404 Not Found');
        return;
      }
    }

    const ext = path.extname(filePath).toLowerCase();
    const contentType = MIME_TYPES[ext] || 'application/octet-stream';

    fs.readFile(filePath, (readErr, content) => {
      if (readErr) {
        res.writeHead(500, { 'Content-Type': 'text/plain' });
        res.end('500 Internal Server Error');
        return;
      }

      res.writeHead(200, {
        'Content-Type': contentType,
        'Cache-Control': 'no-cache'
      });
      res.end(content);
    });
  });
});

// Broadcast periodic server heartbeats
setInterval(() => {
  if (sseClients.size > 0) {
    const mem = process.memoryUsage();
    broadcastEvent('heartbeat', {
      uptime: Math.floor(process.uptime()),
      heapMB: Math.round(mem.heapUsed / (1024 * 1024)),
      rssMB: Math.round(mem.rss / (1024 * 1024)),
      freeMemMB: Math.round(os.freemem() / (1024 * 1024))
    });
  }
}, 1500);

server.listen(PORT, HOST, () => {
  console.log(`[ExhibitOS Backend] Pulse & Telemetry listening on http://${HOST}:${PORT}`);
  console.log(`[ExhibitOS Backend] PID: ${process.pid}, Static dir: ${PUBLIC_DIR}`);
});

// Clean shutdown signals
process.on('SIGINT', () => {
  console.log('[ExhibitOS Backend] Received SIGINT. Shutting down cleanly...');
  server.close(() => process.exit(0));
});

process.on('SIGTERM', () => {
  console.log('[ExhibitOS Backend] Received SIGTERM. Shutting down cleanly...');
  server.close(() => process.exit(0));
});
