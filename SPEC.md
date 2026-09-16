# ExhibitOS (Artwork PC Manager)

## 1. Goal

Build a Windows application that turns an ordinary Windows PC or mini-PC into a reliable, unattended **artwork computer** for exhibitions.

The intended users are exhibition producers, artists, and technicians who may not be Windows experts.

The target workflow is:

**Run application → choose artwork type → place artwork files → configure exhibition → test → configure PC → reboot.**

After configuration, the computer should behave like an appliance:

* automatically enter the artwork environment after boot via a dedicated restricted user (`ArtworkUser`)
* run the artwork fullscreen
* prevent visitors from obtaining a general-purpose Windows UI (no Explorer, no Start menu, no taskbar, no Task Manager, no shell-escape hotkeys)
* follow exhibition opening/closing hours and overnight power policies
* recover from common failures via a supervised watchdog and Windows Job Objects
* require minimal daily intervention

The application is implemented as a self-contained **WinUI 3** executable (`ExhibitOSManager.exe`) paired with a lightweight .NET runtime watchdog (`ExhibitWatchdog.exe`), distributed as a conventional installer (`ExhibitOSSetup.exe`).

Keep the application deliberately focused. This is not intended to become a general-purpose kiosk-management or fleet-management platform.

---

## 2. Artwork Types

Support three artwork types initially.

### Video Folder

The user places one or more videos into the artwork directory.

The system should:

* discover supported video files
* play them in filename order
* play fullscreen without player UI
* loop continuously
* hide the mouse cursor
* support audio output, routed to the configured audio device
* automatically restart playback if the player exits unexpectedly

A single-video artwork is simply a folder containing one video.

**Player implementation**: Use a bundled, standalone **`mpv`** binary (`runtime/bin/mpv/mpv.exe`). It is self-contained, requires no system installer, and provides zero-chrome exhibition playback via command-line flags (`--fs --no-osc --loop-playlist=inf --cursor-autohide=always`). When a specific audio endpoint is configured, pass `--audio-device=` to route audio directly through mpv.

### Web Artwork

The user places a browser-based artwork into the artwork directory.

There are two forms of Web Artwork.

#### Static Web Artwork

For ordinary HTML/JavaScript artworks with no backend requirements.

ExhibitOS should:

* validate the artwork folder (verifying `index.html` or designated entry point)
* provide an integrated local static HTTP server using the bundled Node.js runtime
* bind the static server to `127.0.0.1` on an OS-assigned free port (no fixed port reservation)
* the static server reports its assigned port to the watchdog, which then launches Edge at the corresponding `http://127.0.0.1:<port>` URL
* open it automatically in **Microsoft Edge** in fullscreen kiosk mode (`--kiosk http://127.0.0.1:<port> --edge-kiosk-type=fullscreen --no-first-run --overscroll-history-navigation=0 --disable-pinch`)
* use a dedicated `--user-data-dir` for the ExhibitOS Edge instance, isolating it from any other Edge profiles or sessions
* monitor the required runtime components independently
* recover if the browser or static server exits unexpectedly

The local server must continue functioning when external networking is disabled.

#### Backend Web Artwork

Some browser artworks require artwork-specific backend functionality.

Examples might include:

* silent printing
* communicating with hardware (serial, USB, DMX, microcontrollers)
* filesystem operations
* network requests
* invoking system functionality
* processing or generating content

**ExhibitOS must not implement APIs for these behaviors.**

The backend belongs to the individual artwork. ExhibitOS's responsibility is only to provide and supervise the environment in which that backend runs.

**Entry Point Convention**: The presence of `server.js` in the artwork directory identifies a backend-enabled artwork. ExhibitOS runs `server.js` with the bundled Node.js runtime. The artwork's server serves both its frontend and any artwork-specific API on the same port.

**Port Assignment**: The backend server binds to `127.0.0.1` on an OS-assigned free port, using the same dynamic port strategy as the static server. ExhibitOS communicates the required port to the backend (e.g., via environment variable) and the backend reports its listening port back to the watchdog.

**Node.js Runtime Specification**:
* ExhibitOS ships a known, pinned **Node.js LTS** runtime in its local runtime directory (`runtime/bin/node/node.exe`).
* Do **not** globally install Node.js.
* Do **not** modify the system `PATH`.
* Do **not** rely on whatever version of Node happens to exist on the host machine.
* Do **not** run `npm install` or download dependencies on the exhibition computer. Backend artworks must arrive pre-packaged and ready to run with all their dependencies (`node_modules`).

Conceptually:

```text
Artwork/
    server.js
    node_modules/
    public/
        index.html
        app.js
        assets/
```

ExhibitOS should:

* identify a backend-enabled artwork by the presence of `server.js`
* start its backend using the bundled Node.js runtime with the appropriate working directory
* **Readiness probe**: poll the configured localhost URL/endpoint (with exponential backoff and timeout) until the backend responds before launching the browser, preventing "This site can't be reached" errors
* open its localhost URL in **Microsoft Edge** in fullscreen kiosk mode with a dedicated `--user-data-dir`
* monitor the backend process and browser independently within separate component Job Objects
* restart the appropriate component if it unexpectedly exits
* stop it cleanly when the exhibition closes

The artwork's Node server serves both its frontend and its artwork-specific API. ExhibitOS should **not need to understand the API endpoints or functionality provided by an artwork backend**.

### Application

The user places an artwork executable (or launch script) into the artwork directory and specifies the entry point.

This supports interactive works such as Unity applications, Unreal builds, OpenFrameworks, or TouchDesigner executables.

ExhibitOS should:

* launch the executable automatically
* assign it to a Windows Job Object
* run it as the primary exhibition interface
* monitor it
* relaunch it if it unexpectedly exits

Artists who need custom launch parameters (e.g., Unity's `-screen-fullscreen 1 -screen-width 1920`) should provide a batch script or wrapper as their launch executable rather than the artwork binary directly. ExhibitOS does not manage command-line arguments for artwork executables.

Note: A batch wrapper may launch the real artwork process and immediately exit. The watchdog must determine component liveness based on the **supervised process tree within the Job Object**, not the original launcher PID. The component is considered alive as long as any process remains in its Job Object, and dead only when the Job Object is empty.

---

## 3. Artwork Directory & Filesystem Layout

ExhibitOS creates a managed directory structure on the local machine. **ExhibitOS does not copy artwork files.** The operator copies artwork files into the artwork directory manually using whichever method is appropriate (USB drive, network share, download, etc.).

The setup wizard creates the directory structure and provides an **Open Folder** button that opens `C:\ExhibitOS\artwork` in a standard Windows Explorer window, allowing the operator to populate it.

Application layout:

```text
C:\ExhibitOS\
    artwork/
        [operator places artwork files here]
    config/
        exhibition.json
    logs/
        exhibit.log
        watchdog.log
    runtime/
        ExhibitWatchdog.exe
        bin/
            mpv/
                mpv.exe
            node/
                node.exe
                static-server.js
    ExhibitOSManager.exe
```

The configuration file `C:\ExhibitOS\config\exhibition.json` is human-readable JSON containing the complete desired state.

---

## 4. Exhibition / Restricted User Environment

Rather than relying on Windows Assigned Access / Shell Launcher v2 (which have strict Windows Enterprise/IoT edition limits and fragile UWP requirements), ExhibitOS uses a **dedicated restricted local user account with a custom shell**.

### Account Configuration

* Create a dedicated standard local Windows user account named **`ArtworkUser`**.
* The `ArtworkUser` account is created **without a password**.
* Configure Windows `AutoAdminLogon` so the system automatically logs into `ArtworkUser` after boot.

### Account Security Hardening

Although Windows default security policy prevents blank-password local accounts from remote interactive and network logons (usable only at the physical console), ExhibitOS must not depend on machine defaults. ExhibitOS explicitly hardens the `ArtworkUser` account:

* **Verify and enforce** the "Limit local account use of blank passwords to console logon only" security policy.
* **Deny log on through Remote Desktop Services** for `ArtworkUser` via local security policy user rights assignment.
* **Deny network logon** for `ArtworkUser` via local security policy user rights assignment.

These restrictions do not prevent artwork processes (running under `ArtworkUser`) from making outbound LAN or Internet connections.

### Custom User Shell

Configure the user shell specifically for `ArtworkUser` via the registry:

```text
HKU\<ArtworkUser_SID>\Software\Microsoft\Windows NT\CurrentVersion\Winlogon\Shell = "C:\ExhibitOS\runtime\ExhibitWatchdog.exe"
```

* When `ArtworkUser` logs on, **`explorer.exe` is never launched**.
* Without Explorer, there is no desktop, no taskbar, no Start menu, no system notifications, and no standard shell hotkeys (`Win+E`, `Win+R`, `Win+X`).
* The watchdog (`ExhibitWatchdog.exe`) is the shell: it starts up immediately, establishes the display/power state, enforces the schedule, and supervises the artwork process tree.

### Visitor Escape Prevention

The core requirement is: **a visitor at the physical machine must not be able to obtain a general-purpose Windows UI**. Replacing Explorer with a custom shell removes a large amount of Windows UI surface, but does not by itself constitute a complete lockdown. ExhibitOS must explicitly close remaining escape routes for `ArtworkUser`:

* **Task Manager**: Disable Task Manager for `ArtworkUser` (registry: `DisableTaskMgr`). Without this, `Ctrl+Alt+Delete → Task Manager` or `Ctrl+Shift+Esc` allows launching arbitrary processes via "Run new task."
* **Shell hotkeys**: Disable or suppress `Win` key, `Win+R`, `Win+E`, `Win+X`, `Alt+Tab`, `Ctrl+Shift+Esc`, and other shell-escape key combinations for the `ArtworkUser` session.
* **Alt+F4**: Suppress `Alt+F4` on the artwork window to prevent visitors from closing the artwork and reaching a bare desktop (which, without Explorer, is an empty screen — but still a potential stepping stone).
* **Edge kiosk dialogs**: When Edge is used for Web Artworks, configure it to suppress file download prompts, "Open file" dialogs, and other UI that could provide filesystem access. The dedicated `--user-data-dir` and kiosk mode flags already restrict most of this, but verify and harden as needed.
* **Switch User**: Preserve `Ctrl+Alt+Delete` access to the Windows security screen, and specifically preserve the **Switch User** option so technicians can reach the Administrator account. The security screen itself does not provide a general-purpose UI.

### Technician Maintenance Access

* **`Ctrl+Alt+Delete`** remains the standard technician escape route to the Windows security screen.
* From the Windows security screen, a technician can switch user or sign in to the separate **Administrator** account for maintenance.
* The Administrator account retains the normal Windows shell (`explorer.exe`) and full system access.
* Applying system configuration will require running `ExhibitOSManager.exe` as Administrator (elevated).

---

## 5. Exhibition Schedule & Power Management

Allow configuration of daily:

* opening time (e.g. `07:00`)
* closing time (e.g. `20:00`)
* morning reboot time (e.g. `06:45`)
* overnight power mode

Schedule times use the machine's local timezone. The setup wizard displays the PC's current timezone alongside the schedule configuration (e.g., `Europe/Oslo`) so a misconfigured Windows timezone is visible before committing.

### Daytime Operation

During exhibition hours:

* artwork is running fullscreen
* display remains active
* screensaver and automatic sleep are prevented using the Win32 API:
  `SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED)`
* no global Windows power plan is permanently corrupted; execution flags are maintained continuously by the watchdog process

### Overnight Power Modes

Support three configurable overnight power policies:

1. **Sleep with Wake Timers (Default)**:
   * At closing time, the PC enters sleep. A scheduled wake timer is registered to wake the machine shortly before the scheduled morning reboot.
   * **Missed Reboot Fallback**: Because hardware wake timers on mini-PCs can occasionally fail, on-site technicians commonly press the power button in the morning when unlocking gallery doors. If the PC is manually woken from sleep **after its scheduled morning reboot time** (e.g., scheduled reboot was `06:45`, tech woke the PC at `08:00`), the watchdog immediately detects the missed morning reboot and triggers a clean system reboot before launching the artwork.
2. **Shutdown**:
   * At closing time, the PC performs a clean shutdown.
   * Designed for installations where sleep is unreliable and on-site technicians reliably turn on the PCs in the morning via power buttons or master breaker switches.
3. **Idle**:
   * At closing time, the PC remains powered on overnight, stopping artwork processes and applying the configured overnight display behavior (signal off or blackout).
   * In the morning at the scheduled reboot time, the PC performs a clean reboot and starts the artwork.
   * Guarantees 100% unattended morning startup without relying on sleep/wake hardware support.

### Scheduled Morning Reboot

A daily morning reboot is scheduled via Windows Task Scheduler (running as `SYSTEM` with `shutdown /r /t 0 /f`) before opening hours, ensuring the PC starts from a fresh state and eliminating memory leaks or driver degradation.

---

## 6. Networking

Provide three networking modes configured via the **Windows Filtering Platform (WFP) / Windows Firewall**, rather than disabling network adapters. This avoids driver re-enumeration, maintains device stability, and guarantees loopback networking (`127.0.0.1`, `::1`).

### Offline Exhibition (Default & Recommended)

* Block all outbound and inbound traffic via Windows Firewall rules, **except loopback** (`127.0.0.1`, `::1`).
* Retains complete local inter-process communication (browser to localhost Node server) while preventing internet access, external probing, and disruptive background updates.

### Local Network Only

* Allow inbound and outbound traffic within the local subnet and LAN over both **IPv4 and IPv6**; deny non-local Internet traffic.
* The firewall implementation determines the actual address ranges (e.g., RFC 1918 for IPv4, link-local and ULA for IPv6).
* Supports multi-machine installations, networked sensor arrays, OSC controllers, and local media servers without internet exposure.

### Internet Enabled

* Normal networking rules apply.
* Used exclusively by artworks that require live external internet connectivity.

---

## 7. Windows Exhibition Configuration

Windows configuration is applied idempotently by `ExhibitOSManager.exe` running elevated:

* creation of the passwordless `ArtworkUser` account
* hardening of `ArtworkUser` security: enforce blank-password console-only policy, deny Remote Desktop logon, deny network logon
* visitor lockdown for `ArtworkUser`: disable Task Manager, suppress shell-escape hotkeys, suppress `Alt+F4`
* configuration of `AutoAdminLogon` for `ArtworkUser`
* setting the custom user shell (`ExhibitWatchdog.exe`) for `ArtworkUser`
* creation of Windows Task Scheduler tasks for scheduled reboot
* application of Windows Firewall rules matching the selected networking mode
* suppression of Windows Error Reporting dialogs and disruptive notifications
* disabling Windows Update restart interruptions during exhibition hours
* setting display power and screensaver policies

Windows-specific provisioning code is strictly isolated from application logic. Reapplying configuration converges on the desired state without creating duplicates.

---

## 8. Runtime, Reliability & Process Supervision

ExhibitOS assumes installations may operate unattended for weeks.

### Component Job Objects

`ExhibitWatchdog.exe` runs inside the `ArtworkUser` session as the custom shell. Rather than placing all artwork processes into a single Job Object, the watchdog creates **separate component Job Objects** for independently supervised components. This enables precise failure detection — for example, "Edge died but the Node backend is fine" versus "Node crashed but Edge is still displaying an error page."

Each component Job Object is configured with:

```csharp
JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
```

Terminating a component's Job Object guarantees atomic destruction of that component's entire process tree, preventing orphaned background processes from lingering or locking resources.

### Edge Browser Ownership

ExhibitOS must reliably own, detect failure of, and terminate the entire dedicated kiosk Edge instance. To achieve this:

* Use a dedicated `--user-data-dir` for the ExhibitOS Edge instance, preventing interference with other Edge profiles or sessions.
* Before launching, ensure no stale ExhibitOS Edge instance exists from a previous run or crash.
* Assign the Edge process to its own component Job Object. Windows child processes normally inherit their parent's job membership unless breakaway is permitted, which captures the Chromium process tree.

### Supervision Architecture

```text
Static Web Artwork:
    Node Job Object
    └── node.exe (bundled static server on 127.0.0.1:<dynamic port>)
    Edge Job Object
    └── msedge.exe (kiosk browser pointing to 127.0.0.1:<port>)

Backend Web Artwork:
    Node Job Object
    └── node.exe (artwork server.js on 127.0.0.1:<dynamic port>)
    [Readiness check: HTTP poll until responsive]
    Edge Job Object
    └── msedge.exe (kiosk browser)

Video Folder:
    mpv Job Object
    └── mpv.exe (standalone player)

Application:
    App Job Object
    └── artwork executable
```

### Recovery & Health Checks

* If a supervised component exits unexpectedly during exhibition hours, the watchdog attempts to restart the appropriate component independently.
* Exponential backoff and maximum retry thresholds prevent unrecoverable crash loops.
* **Post-exhaustion recovery**: After a component exhausts its retry threshold, the watchdog does not give up permanently. Instead, it performs a bounded whole-machine recovery — e.g., one system reboot — then resumes normal retry policy after startup. For unattended exhibitions, "give up forever" is not acceptable. Exact retry counts and backoff parameters are implementation details.
* For Web Artworks, the watchdog verifies the HTTP server is responsive before launching or reloading the browser (**startup readiness probe**).
* **Periodic HTTP liveness checks**: During exhibition operation, the watchdog performs low-frequency HTTP health checks against the static or backend server. A Node process can remain alive while its event loop or backend is wedged. Several consecutive liveness failures trigger a backend/static-server restart even if `node.exe` is still running.
* Component liveness for all artwork types is determined by the **supervised process tree within the Job Object**, not merely the original launcher PID. A component is alive as long as any process remains in its Job Object, and dead only when the Job Object is empty.
* All lifecycle events, process starts, exits, and restart attempts are logged to `C:\ExhibitOS\logs\watchdog.log`.

### Logging

ExhibitOS logs must be rotated and size/time bounded so an unattended PC cannot fill its disk. The specific rotation policy (days retained, maximum size) is an implementation detail. Log files are stored in `C:\ExhibitOS\logs\`.

ExhibitOS does not collect crash dumps from artwork processes in v1. Artwork crash diagnostics are the responsibility of the artwork developer.

---

## 9. Setup Interface (WinUI 3)

The setup interface (`ExhibitOSManager.exe`) is a simple, modern wizard built with **WinUI 3** designed for non-experts. It embeds a `requireAdministrator` manifest and prompts for UAC elevation on launch.

Implementation terminology (Winlogon keys, Job Objects, firewall rules, WFP filters) is hidden behind clear, user-focused language.

### Step 1 — Artwork

Ask: **What should this computer run?**

Options:
* **Video Folder**
* **Web Artwork**
* **Application**

After selection, display the artwork directory path (`C:\ExhibitOS\artwork`) with an **Open Folder** button that opens it in Windows Explorer. The operator copies their artwork files into this directory manually.

Once artwork files are present, automatically inspect and provide immediate feedback:
* `✓ Found 4 video files (mp4, mkv)`
* `✓ Web artwork detected (Static — index.html found)`
* `✓ Web artwork with Node backend detected (server.js found)`
* `✓ Executable found: ExhibitionWork.exe`
* `✗ No video files found in artwork folder`
* `✗ No index.html or server.js found`

For Web Artworks, verify that **Microsoft Edge** is installed and can launch. If Edge is not found, display:
* `✗ Microsoft Edge is required for Web Artwork — install Edge and retry`

### Step 2 — Exhibition

Configure:
* **Opening time** (default `07:00`)
* **Closing time** (default `20:00`)
* **Morning reboot time** (default `06:45`)
* Display the PC's current timezone alongside the schedule (e.g., `Schedule times use this PC's timezone: Europe/Oslo`)
* **Overnight Power Mode**:
  * *Sleep with Wake Timers* (Recommended)
  * *Shutdown*
  * *Idle*
* **Networking Mode**:
  * *Offline Exhibition* (Default)
  * *Local Network Only*
  * *Internet Enabled*

### Step 3 — Display & Sound

Configure:
* **Audio Output Device**: Enumerate available audio endpoints (HDMI, 3.5mm, USB audio) and allow explicit device selection so audio is not lost after reboot
* **Cursor Visibility**: Hide or Show
* **Overnight Display Behavior**:
  * *Signal Off (DPMS)*: Cuts display output via `WM_SYSCOMMAND / SC_MONITORPOWER`
  * *Blackout Screen*: Renders a fullscreen borderless pure black window and mutes audio, keeping the HDMI signal active so gallery projectors and monitors do not shut off or show "No Signal" banners

### Step 4 — Test

Provide **Test Artwork** before committing system-level configuration:
* Launches the artwork in a windowed or temporary fullscreen test environment
* For Video: verifies player launch, video decoding, and audio playback
* For Static Web: starts local static server and opens Edge
* For Backend Web: starts backend with bundled Node, waits for readiness probe, and opens Edge
* For Application: launches executable and monitors exit code
* Displays clear diagnostic results with actionable error messages

### Step 5 — Configure

Show a clear summary:
* Selected artwork and type
* Schedule and power mode
* Networking and audio configuration

Action: **Configure This PC for Exhibition**

Writes `C:\ExhibitOS\config\exhibition.json`, provisions `ArtworkUser` (with security hardening), configures custom shell, creates Task Scheduler jobs, and configures firewall rules.

After completion, prompt: **Restart and Enter Exhibition Mode**.

---

## 10. Maintenance Interface

When `ExhibitOSManager.exe` is launched on an already-configured PC by an Administrator, display the **Maintenance Dashboard** instead of the setup wizard:

```text
EXHIBIT OS

● READY FOR EXHIBITION

Artwork: Bergen Rain (Web Artwork with Backend)
Schedule: 07:00 – 20:00 (Europe/Oslo)
Overnight: Sleep (Missed-reboot fallback active)
Network: Offline Exhibition
Audio: Line Out (Realtek High Definition Audio)

[ Launch / Test Artwork ]
[ Stop Artwork ]
[ Run System Diagnostic ]
[ View Logs ]

[ Edit Configuration ]
[ Restore PC to Normal Use ]
```

Technicians can immediately check operational status, trigger tests, view recent log entries and warnings, or edit configuration.

Configuration changes take effect after reboot.

---

## 11. Exhibition Readiness Test

The **Run System Diagnostic** button checks:

* Artwork files present in `C:\ExhibitOS\artwork`
* Bundled runtimes present (`mpv.exe`, `node.exe`)
* Microsoft Edge installed and launchable (for Web Artworks)
* Backend entry point (`server.js`) present (where applicable)
* Backend readiness probe succeeds
* `ArtworkUser` account exists and passwordless logon is configured
* `ArtworkUser` security hardening is in place (Remote Desktop denied, network logon denied)
* Visitor lockdown policies active for `ArtworkUser` (Task Manager disabled, shell-escape hotkeys suppressed)
* Custom shell registry key is correctly pointing to `ExhibitWatchdog.exe`
* Windows Firewall rules correctly enforce the selected networking mode
* Selected audio playback device is connected and available; warn if the configured device is missing
* Scheduled reboot task is registered in Task Scheduler
* Log files are writable

Result displayed clearly:
**READY FOR EXHIBITION** or **PROBLEMS DETECTED** (with clear repair instructions).

---

## 12. Reconfiguration, Removal & Uninstall

### Restore PC to Normal Use

Provide an administrator action: **Restore PC to Normal Use**.

This cleanly reverses all exhibition modifications:
* Removes the custom shell registry key for `ArtworkUser`, restoring the default Windows shell for that account
* Removes visitor lockdown policies (Task Manager restriction, hotkey suppression)
* Removes custom firewall rules
* Disables `AutoAdminLogon`
* Removes scheduled Task Scheduler reboot jobs
* Optionally deletes or disables the `ArtworkUser` account
* Restores normal Windows power and notification settings

Other user accounts are never modified during restoration.

### Uninstall

The installer registers a standard Windows uninstaller. Uninstalling ExhibitOS:

* Runs the "Restore PC to Normal Use" process (reverses all system modifications)
* Deletes the `ArtworkUser` account
* Removes `C:\ExhibitOS` runtime, configuration, and logs
* **Preserves `C:\ExhibitOS\artwork` by default** — the operator manually placed these files and they may be the only local copy of large artwork assets
* Offers an explicit opt-in checkbox: **"Also delete artwork files"** with a destructive confirmation, for operators who want a complete cleanup
* Removes the uninstaller registry entry

---

## 13. Architecture & Tech Stack

```text
                         ExhibitOSManager.exe
                        (WinUI 3 / C# Admin App)
                                   │
                                   ▼
                       C:\ExhibitOS\config\exhibition.json
                                   │
              ┌────────────────────┴────────────────────┐
              ▼                                         ▼
   Windows Provisioning (Admin)               Artwork Session (ArtworkUser)
   • Create ArtworkUser (no pwd)              • AutoAdminLogon
   • Harden ArtworkUser security              • Winlogon Shell:
   • Register Custom Shell                        ExhibitWatchdog.exe
   • Task Scheduler Reboot                              │
   • WFP / Firewall Rules                               ▼
                                            Component Job Objects
                                          ┌──────────┼──────────┐
                                          ▼          ▼          ▼
                                       mpv.exe   node.exe   App.exe
                                                    │
                                             [HTTP Ready?]
                                                    │
                                                    ▼
                                               msedge.exe
                                            (own Job Object)
```

### Component Details

1. **`ExhibitOSSetup.exe`**:
   * Conventional Windows installer that installs the Manager, Watchdog, bundled Node.js runtime, mpv, and static server into `C:\ExhibitOS`, then launches the Manager.
   * Registers a standard Windows uninstaller.
2. **`ExhibitOSManager.exe`**:
   * Windows desktop app built with **C# / .NET 10** and **WinUI 3**.
   * Packaged as a self-contained executable with embedded `requireAdministrator` manifest.
   * Runs elevated with administrator privileges for setup, testing, and maintenance.
3. **`ExhibitWatchdog.exe`**:
   * Lightweight, headless C# executable running in the `ArtworkUser` session.
   * Configured as the custom Winlogon shell.
   * Manages `SetThreadExecutionState`, enforces exhibition hours, controls component Job Objects, monitors artwork health, and persists/re-resolves the configured audio endpoint at startup.
   * If the configured audio device is missing at startup, the watchdog logs a warning rather than silently proceeding.
4. **`exhibition.json`**:
   * Declarative desired state configuration file stored in `C:\ExhibitOS\config\exhibition.json`.
   * Configuration changes require a reboot to take effect.
5. **Bundled Runtimes**:
   * Pinned Node.js LTS portable build in `runtime/bin/node/`.
   * Pinned `mpv` standalone build in `runtime/bin/mpv/`.
   * System **Microsoft Edge** in kiosk mode for web rendering (verified present during setup and diagnostics).

### Supported Platform

* **Windows 11 Home** or higher. ExhibitOS avoids features restricted to Pro/Enterprise editions.

---

## 14. Development and Testing

* Primary testing in disposable **Windows 11 virtual machines** with clean snapshots.
* Verify:
  1. Automated setup and idempotent re-provisioning
  2. Auto-login into `ArtworkUser` without password prompt
  3. Custom shell launches watchdog without `explorer.exe` (no taskbar, no start menu)
  4. `ArtworkUser` security hardening (RDP denied, network logon denied, console-only blank password)
  5. `Ctrl+Alt+Delete` allows switching back to Admin
  6. Component Job Object isolation (killing Edge Job does not kill Node; killing Node Job does not kill Edge)
  7. Edge process tree captured in Job Object via dedicated `--user-data-dir`
  8. Batch/script launcher exits but artwork process tree remains alive in Job Object (no spurious relaunch)
  9. Missed morning reboot fallback after manual wake from sleep
  10. Offline firewall rules block internet while preserving `localhost`
  11. Local Network Only rules apply to both IPv4 and IPv6
  12. Dynamic port assignment for static and backend servers
  13. Periodic HTTP liveness check detects wedged Node process and restarts it
  14. Post-exhaustion recovery triggers system reboot and resumes retry policy
  15. Full restoration to normal PC state (only ArtworkUser shell modified, other accounts untouched)
  16. Uninstall preserves `C:\ExhibitOS\artwork` by default; opt-in deletes it
  17. Complete uninstall including `ArtworkUser` deletion
* **Visitor Escape Test Matrix** — verify each route is blocked for `ArtworkUser`:

  | Input | Expected Result |
  |---|---|
  | `Windows` key | Suppressed, no Start menu |
  | `Win+R` | Suppressed, no Run dialog |
  | `Win+E` | Suppressed, no Explorer |
  | `Win+X` | Suppressed, no power-user menu |
  | `Ctrl+Shift+Esc` | Suppressed, no Task Manager |
  | `Alt+Tab` | Suppressed, no task switcher |
  | `Alt+F4` | Suppressed on artwork window |
  | `Ctrl+Alt+Delete` | Security screen appears; **Switch User available**; **Task Manager disabled** |
  | Edge download/open dialog | Suppressed or non-functional in kiosk mode |

* Physical mini-PC hardware verification for HDMI audio routing, projector blackout behavior, and sleep/wake timers.
