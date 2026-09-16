/**
 * ChromaFlow: Harmonic Generative Vector Field
 * ExhibitOS Static Web Test Artwork
 * Pure HTML5 Canvas / JavaScript — Zero external dependencies.
 */

(function () {
  'use strict';

  const canvas = document.getElementById('canvas');
  const ctx = canvas.getContext('2d');

  const fpsEl = document.getElementById('fps');
  const particlesCountEl = document.getElementById('particles-count');
  const paletteNameEl = document.getElementById('palette-name');
  const uptimeEl = document.getElementById('uptime');
  const hudEl = document.getElementById('hud');

  let width = 0;
  let height = 0;
  let dpr = 1;

  // Color Palettes
  const PALETTES = [
    {
      name: 'Deep Nebula',
      bg: '#05060f',
      colors: ['#38bdf8', '#818cf8', '#c084fc', '#f472b6', '#34d399']
    },
    {
      name: 'Solar Flare',
      bg: '#0c0502',
      colors: ['#f97316', '#fbbf24', '#ef4444', '#f43f5e', '#facc15']
    },
    {
      name: 'Abyssal Bioluminescence',
      bg: '#020b0a',
      colors: ['#06b6d4', '#10b981', '#2dd4bf', '#3b82f6', '#a7f3d0']
    },
    {
      name: 'Cyberpunk Aurora',
      bg: '#090314',
      colors: ['#e879f9', '#a855f7', '#06b6d4', '#ec4899', '#6366f1']
    }
  ];

  let currentPaletteIndex = 0;
  let activePalette = PALETTES[currentPaletteIndex];

  // Particle System Parameters
  const PARTICLE_COUNT = 900;
  const particles = [];
  let flowFieldAngleOffset = 0;

  // Interaction State
  const mouse = {
    x: -1000,
    y: -1000,
    isDown: false,
    radius: 180,
    force: 0.08
  };

  const ripples = [];

  class Particle {
    constructor() {
      this.reset(true);
    }

    reset(initial = false) {
      this.x = initial ? Math.random() * width : (Math.random() < 0.5 ? 0 : width);
      this.y = initial ? Math.random() * height : Math.random() * height;
      this.prevX = this.x;
      this.prevY = this.y;
      this.speed = Math.random() * 1.8 + 1.2;
      this.color = activePalette.colors[Math.floor(Math.random() * activePalette.colors.length)];
      this.alpha = Math.random() * 0.7 + 0.3;
      this.size = Math.random() * 1.8 + 0.8;
      this.life = Math.floor(Math.random() * 300) + 120;
      this.maxLife = this.life;
    }

    update() {
      this.prevX = this.x;
      this.prevY = this.y;

      // Calculate vector field angle at current coordinate
      const scale = 0.0035;
      const angle = (
        Math.sin(this.x * scale + flowFieldAngleOffset * 0.4) *
        Math.cos(this.y * scale + flowFieldAngleOffset * 0.3) * Math.PI * 4 +
        Math.sin((this.x + this.y) * scale * 0.5) * Math.PI
      );

      let vx = Math.cos(angle) * this.speed;
      let vy = Math.sin(angle) * this.speed;

      // Mouse gravitational influence
      const dx = mouse.x - this.x;
      const dy = mouse.y - this.y;
      const dist = Math.hypot(dx, dy);

      if (dist < mouse.radius && dist > 2) {
        const factor = (1 - dist / mouse.radius);
        if (mouse.isDown) {
          // Attractor vortex
          vx += (dx / dist) * factor * 5;
          vy += (dy / dist) * factor * 5;
        } else {
          // Gentle tangential swirl
          vx += (-dy / dist) * factor * 2;
          vy += (dx / dist) * factor * 2;
        }
      }

      // Ripple shockwaves
      for (let i = ripples.length - 1; i >= 0; i--) {
        const r = ripples[i];
        const rdx = this.x - r.x;
        const rdy = this.y - r.y;
        const rdist = Math.hypot(rdx, rdy);
        const waveDist = Math.abs(rdist - r.radius);
        if (waveDist < 30) {
          const push = (1 - waveDist / 30) * r.strength;
          vx += (rdx / (rdist || 1)) * push;
          vy += (rdy / (rdist || 1)) * push;
        }
      }

      this.x += vx;
      this.y += vy;
      this.life--;

      if (this.life <= 0 || this.x < 0 || this.x > width || this.y < 0 || this.y > height) {
        this.reset();
      }
    }

    draw(targetCtx) {
      targetCtx.beginPath();
      targetCtx.moveTo(this.prevX, this.prevY);
      targetCtx.lineTo(this.x, this.y);
      targetCtx.strokeStyle = this.color;
      targetCtx.globalAlpha = (this.life / this.maxLife) * this.alpha;
      targetCtx.lineWidth = this.size;
      targetCtx.stroke();
    }
  }

  function resize() {
    dpr = Math.min(window.devicePixelRatio || 1, 2);
    width = window.innerWidth;
    height = window.innerHeight;

    canvas.width = Math.floor(width * dpr);
    canvas.height = Math.floor(height * dpr);
    ctx.scale(dpr, dpr);

    // Re-fill background on resize
    ctx.fillStyle = activePalette.bg;
    ctx.fillRect(0, 0, width, height);
  }

  function initParticles() {
    particles.length = 0;
    for (let i = 0; i < PARTICLE_COUNT; i++) {
      particles.push(new Particle());
    }
    particlesCountEl.textContent = PARTICLE_COUNT.toLocaleString();
  }

  function addRipple(x, y) {
    ripples.push({
      x: x,
      y: y,
      radius: 5,
      maxRadius: Math.max(width, height) * 0.4,
      strength: 7,
      speed: 6
    });
  }

  function cyclePalette() {
    currentPaletteIndex = (currentPaletteIndex + 1) % PALETTES.length;
    activePalette = PALETTES[currentPaletteIndex];
    paletteNameEl.textContent = activePalette.name;
    particles.forEach(p => {
      p.color = activePalette.colors[Math.floor(Math.random() * activePalette.colors.length)];
    });
  }

  // Animation Loop
  let lastTime = performance.now();
  let frameCount = 0;
  let fpsTimer = performance.now();
  const startTime = Date.now();
  let isPaused = false;

  function animate(now) {
    requestAnimationFrame(animate);

    if (isPaused) return;

    // Measure FPS
    frameCount++;
    if (now - fpsTimer >= 1000) {
      fpsEl.textContent = frameCount.toString();
      frameCount = 0;
      fpsTimer = now;
    }

    // Update Uptime
    const elapsedSec = Math.floor((Date.now() - startTime) / 1000);
    const hrs = String(Math.floor(elapsedSec / 3600)).padStart(2, '0');
    const mins = String(Math.floor((elapsedSec % 3600) / 60)).padStart(2, '0');
    const secs = String(elapsedSec % 60).padStart(2, '0');
    uptimeEl.textContent = `${hrs}:${mins}:${secs}`;

    // Slow evolution of vector field
    flowFieldAngleOffset += 0.003;

    // Semi-transparent fade background for trailing light effect
    ctx.fillStyle = activePalette.bg;
    ctx.globalAlpha = 0.09;
    ctx.fillRect(0, 0, width, height);

    // Update & draw ripples
    for (let i = ripples.length - 1; i >= 0; i--) {
      const r = ripples[i];
      r.radius += r.speed;
      r.strength *= 0.96;
      if (r.radius > r.maxRadius || r.strength < 0.2) {
        ripples.splice(i, 1);
      }
    }

    // Draw particles
    for (let i = 0; i < particles.length; i++) {
      particles[i].update();
      particles[i].draw(ctx);
    }
  }

  // Event Listeners
  window.addEventListener('resize', () => {
    resize();
  });

  window.addEventListener('pointermove', (e) => {
    mouse.x = e.clientX;
    mouse.y = e.clientY;
  });

  window.addEventListener('pointerdown', (e) => {
    mouse.x = e.clientX;
    mouse.y = e.clientY;
    mouse.isDown = true;
    addRipple(e.clientX, e.clientY);
  });

  window.addEventListener('pointerup', () => {
    mouse.isDown = false;
  });

  window.addEventListener('pointerleave', () => {
    mouse.x = -1000;
    mouse.y = -1000;
    mouse.isDown = false;
  });

  window.addEventListener('keydown', (e) => {
    if (e.code === 'KeyH') {
      hudEl.classList.toggle('hidden');
    } else if (e.code === 'KeyC') {
      cyclePalette();
    } else if (e.code === 'KeyR') {
      ctx.fillStyle = activePalette.bg;
      ctx.globalAlpha = 1;
      ctx.fillRect(0, 0, width, height);
      initParticles();
    } else if (e.code === 'Space') {
      isPaused = !isPaused;
    }
  });

  // Cycle palette automatically every 90 seconds for continuous ambient variation
  setInterval(cyclePalette, 90000);

  // Auto-fade HUD after 8 seconds of inactivity
  let hudTimeout = setTimeout(() => {
    hudEl.classList.add('hidden');
  }, 8000);

  window.addEventListener('pointermove', () => {
    if (hudEl.classList.contains('hidden')) {
      hudEl.classList.remove('hidden');
    }
    clearTimeout(hudTimeout);
    hudTimeout = setTimeout(() => {
      hudEl.classList.add('hidden');
    }, 8000);
  });

  // Initialize
  resize();
  initParticles();
  requestAnimationFrame(animate);
})();
