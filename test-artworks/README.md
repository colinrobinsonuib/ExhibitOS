# ExhibitOS Development Test Artworks

This directory contains test artworks designed strictly for local development and VM testing.

> [!NOTE]
> These test artworks are **not** bundled into the production distribution installer (`dist/` or `dist-installer/ExhibitOSSetup.exe`). They provide ready-to-test assets for each supported artwork type.

---

## Directory Structure

```text
test-artworks/
├── video/          # For video playback testing with bundled mpv
├── static-web/     # Static HTML/CSS/JS generative artwork ("ChromaFlow")
└── node-app/       # Node.js backend-enabled artwork with API & SSE ("Pulse & Telemetry")
```

---

## Testing Each Artwork Type

### 1. Video Folder Artwork (`test-artworks/video/`)
- Place one or more video files (`.mp4`, `.mkv`, `.mov`, `.avi`, `.webm`) in the `video/` folder.
- In **ExhibitOS Manager**, select **Videos** and point the Artwork Directory to this `video/` folder.
- When launched, ExhibitOS runs the bundled `mpv.exe` in fullscreen, looping all videos in alphabetical order with zero window chrome and hidden cursor.

### 2. Static Web Artwork (`test-artworks/static-web/`)
- Contains **"ChromaFlow: Generative Field"** (an interactive canvas-based generative particle artwork).
- No backend required; runs completely offline without any external dependencies or CDN links.
- In **ExhibitOS Manager**, select **Website** and point the Artwork Directory to `test-artworks/static-web/`.
- ExhibitOS automatically launches its bundled lightweight static web server on an ephemeral loopback port and opens the artwork in Microsoft Edge fullscreen kiosk mode.

### 3. Node.js Backend Artwork (`test-artworks/node-app/`)
- Contains **"Pulse & Telemetry: Living System"** (a real-time interactive piece with Node.js server, Server-Sent Events, and dynamic interaction logging).
- Pure standard library implementation using Node.js built-ins (`http`, `fs`, `path`, `os`, `url`) so it runs out-of-the-box with ExhibitOS's bundled `node.exe` without requiring `npm install`.
- Respects `PORT` and `HOST` environment variables assigned dynamically by ExhibitOS.
- In **ExhibitOS Manager**, select **Website with a Node.js server**, point to `test-artworks/node-app/`, with entry point `server.js`.
- ExhibitOS launches the Node.js backend inside a dedicated Job Object, runs readiness probes against the HTTP endpoint, and opens Microsoft Edge kiosk mode once healthy.
