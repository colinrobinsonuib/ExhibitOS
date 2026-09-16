# Pulse & Telemetry: Living System

**ExhibitOS Test Artwork: Node.js Backend Web**

A real-time, interactive exhibition artwork featuring a biomorphic synaptic node network powered by a native Node.js HTTP & Server-Sent Events (SSE) server.

## Characteristics
- **Zero npm Dependencies**: Written strictly with Node.js built-ins (`http`, `fs`, `path`, `os`, `url`). Runs directly on ExhibitOS's bundled portable Node.js runtime without requiring internet access or `npm install`.
- **Dynamic Port & Host Binding**: Reads `process.env.PORT` and `process.env.HOST` supplied by ExhibitOS at runtime, binding gracefully to the dynamically assigned loopback port.
- **Readiness Probing**: Serves HTTP 200 on `GET /` immediately, satisfying ExhibitOS's watchdog startup readiness probe.
- **Real-Time Telemetry**:
  - `GET /api/status`: JSON endpoint providing system telemetry (uptime, RAM/heap usage, CPU cores, active connections, interaction history).
  - `GET /api/stream`: Server-Sent Events (SSE) broadcasting continuous heartbeats (every 1.5s) and interaction events.
  - `POST /api/interact`: Receives visitor interactions from the kiosk, logs them to `data/interactions.json`, and broadcasts to all connected displays.
- **Biomorphic Visualizer**: Interactive HTML5 Canvas showing synaptic neural connections that react to visitor touches and real-time backend heartbeats.

## Manual Testing (CLI)
You can run the server directly using Node:
```powershell
node server.js
```
Then open `http://127.0.0.1:3000/` in any browser.

To test with custom port/host (as ExhibitOS does):
```powershell
$env:PORT="8085"
$env:HOST="127.0.0.1"
node server.js
```

## ExhibitOS Integration
1. In **ExhibitOS Manager**, select **Website with a Node.js server**.
2. Set the Artwork Directory to this folder (`test-artworks/node-app/`).
3. Set the Entry Point to `server.js`.
4. ExhibitOS will launch Node inside a dedicated Job Object, probe for readiness on the allocated loopback port, and display the kiosk frontend in Microsoft Edge.
