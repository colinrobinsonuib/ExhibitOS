/**
 * Pulse & Telemetry: Living Biomorphic System
 * ExhibitOS Node.js Backend Frontend Controller
 */

(function () {
  'use strict';

  const canvas = document.getElementById('stage');
  const ctx = canvas.getContext('2d');

  // DOM Elements
  const telEndpoint = document.getElementById('tel-endpoint');
  const telUptime = document.getElementById('tel-uptime');
  const telMemory = document.getElementById('tel-memory');
  const telLatency = document.getElementById('tel-latency');
  const logStream = document.getElementById('log-stream');
  const logCountEl = document.getElementById('log-count');

  const btnPulse = document.getElementById('btn-pulse');
  const btnInvert = document.getElementById('btn-invert');
  const btnNote = document.getElementById('btn-note');

  let width = 0;
  let height = 0;
  let dpr = 1;
  let totalEvents = 0;

  // Biomorphic Graph Network
  const NODE_COUNT = 45;
  const nodes = [];
  const pulses = [];
  let geometryMode = 0; // 0 = organic organic network, 1 = crystalline constellation

  class NetworkNode {
    constructor(x, y, isStatic = false) {
      this.x = x ?? Math.random() * width;
      this.y = y ?? Math.random() * height;
      this.isStatic = isStatic;
      this.baseRadius = Math.random() * 4 + 2;
      this.radius = this.baseRadius;
      this.vx = (Math.random() - 0.5) * 0.8;
      this.vy = (Math.random() - 0.5) * 0.8;
      this.color = '#38bdf8';
      this.excitation = 0;
    }

    update() {
      if (!this.isStatic) {
        this.x += this.vx;
        this.y += this.vy;

        if (this.x < 30 || this.x > width - 30) this.vx *= -1;
        if (this.y < 30 || this.y > height - 30) this.vy *= -1;
      }

      if (this.excitation > 0) {
        this.excitation *= 0.94;
        if (this.excitation < 0.01) this.excitation = 0;
      }

      this.radius = this.baseRadius + this.excitation * 8;
    }

    draw(targetCtx) {
      targetCtx.beginPath();
      targetCtx.arc(this.x, this.y, this.radius, 0, Math.PI * 2);
      targetCtx.fillStyle = this.excitation > 0 ? '#f43f5e' : this.color;
      targetCtx.shadowColor = this.color;
      targetCtx.shadowBlur = this.excitation * 20;
      targetCtx.fill();
      targetCtx.shadowBlur = 0;
    }
  }

  function resize() {
    dpr = Math.min(window.devicePixelRatio || 1, 2);
    width = window.innerWidth;
    height = window.innerHeight;

    canvas.width = Math.floor(width * dpr);
    canvas.height = Math.floor(height * dpr);
    ctx.scale(dpr, dpr);
  }

  function initNetwork() {
    nodes.length = 0;
    for (let i = 0; i < NODE_COUNT; i++) {
      nodes.push(new NetworkNode());
    }
  }

  function triggerGlobalPulse(color = '#38bdf8', sourceNode = null) {
    const origin = sourceNode || nodes[Math.floor(Math.random() * nodes.length)];
    if (!origin) return;

    origin.excitation = 1.0;

    // Dispatch pulse along nearest neighbors
    nodes.forEach(target => {
      if (target === origin) return;
      const d = Math.hypot(target.x - origin.x, target.y - origin.y);
      if (d < 220) {
        pulses.push({
          x1: origin.x,
          y1: origin.y,
          x2: target.x,
          y2: target.y,
          progress: 0,
          speed: 0.045,
          color: color,
          targetNode: target
        });
      }
    });
  }

  // Draw & Update Loop
  function animate() {
    requestAnimationFrame(animate);

    ctx.fillStyle = 'rgba(6, 9, 19, 0.22)';
    ctx.fillRect(0, 0, width, height);

    // Update nodes
    nodes.forEach(n => n.update());

    // Draw connections
    ctx.lineWidth = 1;
    for (let i = 0; i < nodes.length; i++) {
      for (let j = i + 1; j < nodes.length; j++) {
        const dx = nodes[i].x - nodes[j].x;
        const dy = nodes[i].y - nodes[j].y;
        const dist = Math.hypot(dx, dy);
        const maxDist = geometryMode === 0 ? 170 : 250;

        if (dist < maxDist) {
          const alpha = (1 - dist / maxDist) * 0.35;
          ctx.beginPath();
          ctx.moveTo(nodes[i].x, nodes[i].y);
          ctx.lineTo(nodes[j].x, nodes[j].y);
          ctx.strokeStyle = `rgba(56, 189, 248, ${alpha})`;
          ctx.stroke();
        }
      }
    }

    // Update & draw pulses
    for (let i = pulses.length - 1; i >= 0; i--) {
      const p = pulses[i];
      p.progress += p.speed;

      const px = p.x1 + (p.x2 - p.x1) * p.progress;
      const py = p.y1 + (p.y2 - p.y1) * p.progress;

      ctx.beginPath();
      ctx.arc(px, py, 3, 0, Math.PI * 2);
      ctx.fillStyle = p.color;
      ctx.shadowColor = p.color;
      ctx.shadowBlur = 10;
      ctx.fill();
      ctx.shadowBlur = 0;

      if (p.progress >= 1) {
        if (p.targetNode) p.targetNode.excitation = 0.8;
        pulses.splice(i, 1);
      }
    }

    // Draw nodes
    nodes.forEach(n => n.draw(ctx));
  }

  // Logging Helper
  function appendLog(tag, tagClass, text) {
    totalEvents++;
    logCountEl.textContent = `${totalEvents} events`;

    const entry = document.createElement('div');
    entry.className = 'log-entry';

    const now = new Date();
    const timeStr = `${String(now.getHours()).padStart(2, '0')}:${String(now.getMinutes()).padStart(2, '0')}:${String(now.getSeconds()).padStart(2, '0')}`;

    entry.innerHTML = `
      <span class="log-time">${timeStr}</span>
      <span class="log-tag ${tagClass}">${tag}</span>
      <span class="log-msg">${text}</span>
    `;

    logStream.insertBefore(entry, logStream.firstChild);
    while (logStream.children.length > 8) {
      logStream.removeChild(logStream.lastChild);
    }
  }

  // Telemetry Formatters
  function formatUptime(seconds) {
    const hrs = String(Math.floor(seconds / 3600)).padStart(2, '0');
    const mins = String(Math.floor((seconds % 3600) / 60)).padStart(2, '0');
    const secs = String(seconds % 60).padStart(2, '0');
    return `${hrs}:${mins}:${secs}`;
  }

  // SSE & API Communication
  let pingStart = Date.now();

  function connectSse() {
    const evtSource = new EventSource('/api/stream');

    evtSource.onmessage = (event) => {
      try {
        const data = JSON.parse(event.data);
        if (data.type === 'heartbeat') {
          const payload = data.payload;
          telUptime.textContent = formatUptime(payload.uptime);
          telMemory.textContent = `${payload.heapMB} MB`;
          telLatency.textContent = `${Math.max(1, Date.now() - pingStart)} ms`;
          pingStart = Date.now();
        } else if (data.type === 'interaction') {
          const p = data.payload;
          appendLog(p.type.toUpperCase(), 'interact', p.note || `Interaction at (${Math.round(p.x * 100)}%, ${Math.round(p.y * 100)}%)`);
          triggerGlobalPulse(p.color || '#c084fc');
        } else if (data.type === 'connected') {
          appendLog('INIT', 'pulse', `Connected to backend stream`);
        }
      } catch (err) {
        console.error('SSE parse error', err);
      }
    };

    evtSource.onerror = () => {
      telLatency.textContent = 'reconnecting';
    };
  }

  async function fetchInitialStatus() {
    try {
      const res = await fetch('/api/status');
      if (res.ok) {
        const data = await res.json();
        telEndpoint.textContent = `${data.process.host}:${data.process.port}`;
        telUptime.textContent = formatUptime(data.uptimeSeconds);
        telMemory.textContent = `${data.process.heapUsedMB} MB`;
        appendLog('ONLINE', 'pulse', `Telemetry link established (PID: ${data.process.pid})`);

        if (Array.isArray(data.recentInteractions)) {
          data.recentInteractions.slice(-4).forEach(item => {
            appendLog(item.type.toUpperCase(), 'interact', item.note || 'Historical interaction restored');
          });
        }
      }
    } catch (err) {
      console.warn('Could not fetch status immediately:', err);
    }
  }

  async function postInteraction(type, extra = {}) {
    const payload = {
      type: type,
      x: extra.x ?? 0.5,
      y: extra.y ?? 0.5,
      color: extra.color || '#38bdf8',
      note: extra.note || ''
    };

    try {
      await fetch('/api/interact', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
      });
    } catch (err) {
      console.error('Failed to send interaction:', err);
    }
  }

  // Interactive Event Handlers
  canvas.addEventListener('pointerdown', (e) => {
    const normX = e.clientX / width;
    const normY = e.clientY / height;
    const newNode = new NetworkNode(e.clientX, e.clientY);
    newNode.excitation = 1.0;
    nodes.push(newNode);
    if (nodes.length > NODE_COUNT + 15) {
      nodes.shift();
    }

    triggerGlobalPulse('#38bdf8', newNode);
    postInteraction('node_spawn', { x: normX, y: normY, note: `Node spawned at [${Math.round(e.clientX)}, ${Math.round(e.clientY)}]` });
  });

  btnPulse.addEventListener('click', () => {
    triggerGlobalPulse('#38bdf8');
    postInteraction('pulse', { color: '#38bdf8', note: 'Manual Neural Pulse emitted' });
  });

  btnInvert.addEventListener('click', () => {
    geometryMode = geometryMode === 0 ? 1 : 0;
    appendLog('GEOMETRY', 'geo', `Topology switched to ${geometryMode === 0 ? 'Organic Network' : 'Crystalline Constellation'}`);
    triggerGlobalPulse('#4ade80');
  });

  const notesList = [
    'Resonant harmonic tone (432 Hz)',
    'Low-frequency synth oscillation',
    'Chime frequency delta (528 Hz)',
    'Dynamic ambient chord sequence'
  ];

  btnNote.addEventListener('click', () => {
    const pickedNote = notesList[Math.floor(Math.random() * notesList.length)];
    triggerGlobalPulse('#e879f9');
    postInteraction('resonance', { color: '#e879f9', note: pickedNote });
  });

  // Init
  resize();
  initNetwork();
  window.addEventListener('resize', resize);
  fetchInitialStatus();
  connectSse();
  animate();
})();
