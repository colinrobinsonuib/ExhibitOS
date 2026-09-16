# ExhibitOS (Artwork PC Manager)

## 1. Goal

Build a Windows application that turns an ordinary Windows PC or mini-PC into a reliable, unattended **artwork computer** for exhibitions.

The intended users are exhibition producers, artists, and technicians who may not be Windows experts.

The target workflow is:

**Run application → choose artwork → configure exhibition → test → configure PC → reboot.**

After configuration, the computer should behave like an appliance:

* automatically enter the artwork environment after boot via a dedicated restricted user (`ArtworkUser`)
* run the artwork fullscreen
* prevent visitors from accessing Windows (no Explorer, no Start menu, no taskbar, no desktop shortcuts)
* follow exhibition opening/closing hours and overnight power policies
* recover from common failures via a supervised watchdog and Windows Job Objects
* require minimal daily intervention

The application is implemented as a self-contained **WinUI 3** executable (`ExhibitOSManager.exe`) paired with a lightweight .NET runtime watchdog (`ExhibitWatchdog.exe`).

Keep the application deliberately focused. This is not intended to become a general-purpose kiosk-management or fleet-management platform.

---

## 2. Artwork Types

Support three artwork types initially.

### Video Folder

The user selects a folder containing one or more videos.

The system should:

* discover supported video files
* play them in filename order
* play fullscreen without player UI
* loop continuously
* hide the mouse cursor
* support audio output
* automatically restart playback if the player exits unexpectedly

A single-video artwork is simply a folder containing one video.

**Player implementation**: Use a bundled, standalone **`mpv`** binary (`runtime/bin/mpv/mpv.exe`). It is self-contained, requires no system installer, and provides zero-chrome exhibition playback via command-line flags (`--fs --no-osc --loop-playlist=inf --cursor-autohide=always`).

### Web Artwork

The user selects a folder containing a browser-based artwork.

There are two forms of Web Artwork.

#### Static Web Artwork

For ordinary HTML/JavaScript artworks with no backend requirements.

ExhibitOS should:

* validate the artwork folder (verifying `index.html` or designated entry point)
* provide an integrated local static HTTP server using the bundled Node.js runtime
* serve the artwork from localhost
* open it automatically in **Microsoft Edge** in fullscreen kiosk mode (`--kiosk http://localhost:<port> --edge-kiosk-type=fullscreen --no-first-run --overscroll-history-navigation=0 --disable-pinch`)
* monitor the required runtime components
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

**Node.js Runtime Specification**:
* ExhibitOS ships a known, pinned **Node.js LTS** runtime in its local runtime directory (`runtime/bin/node/node.exe`).
* Do **not** globally install Node.js.
* Do **not** modify the system `PATH`.
* Do **not** rely on whatever version of Node happens to exist on the host machine.
* Do **not** run `npm install` or download dependencies on the exhibition computer. Backend artworks must arrive pre-packaged and ready to run with all their dependencies (`node_modules`).

Conceptually:

```text
Artwork/
    package.json
    node_modules/
    server.js
    public/
        index.html
        app.js
        assets/
```

ExhibitOS should:

* identify a backend-enabled artwork
* start its backend using the bundled Node.js runtime with the appropriate working directory
* **Readiness probe**: poll the configured localhost URL/endpoint (with exponential backoff and timeout) until the backend responds before launching the browser, preventing "This site can't be reached" errors
* open its localhost URL in **Microsoft Edge** in fullscreen kiosk mode
* monitor the backend process tree within a Windows Job Object
* restart it if it unexpectedly exits
* stop it cleanly when the exhibition closes

The artwork's Node server may serve both its frontend and its artwork-specific API. ExhibitOS should **not need to understand the API endpoints or functionality provided by an artwork backend**.

### Application

The user selects an artwork folder and executable.

This supports interactive works such as Unity applications, Unreal builds, OpenFrameworks, or TouchDesigner executables.

ExhibitOS should:

* launch the executable automatically
* assign it to a Windows Job Object
* run it as the primary exhibition interface
* monitor it
* relaunch it if it unexpectedly exits

---

## 3. Managed Artwork & Filesystem Layout

When configuring a PC, copy the selected artwork into an application-managed location on the local machine (`C:\ExhibitOS\artwork`).

Do not depend on the original USB drive, Downloads folder, or network share remaining available.

Application layout:

```text
C:\ExhibitOS\
    artwork/
        [copied artwork files and node_modules]
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

### Custom User Shell

Configure the user shell specifically for `ArtworkUser` via the registry:

```text
HKU\<ArtworkUser_SID>\Software\Microsoft\Windows NT\CurrentVersion\Winlogon\Shell = "C:\ExhibitOS\runtime\ExhibitWatchdog.exe"
```

* When `ArtworkUser` logs on, **`explorer.exe` is never launched**.
* Without Explorer, there is no desktop, no taskbar, no Start menu, no system notifications, and no standard shell hotkeys (`Win+E`, `Win+R`, `Win+X`).
* The watchdog (`ExhibitWatchdog.exe`) is the shell: it starts up immediately, establishes the display/power state, enforces the schedule, and supervises the artwork process tree.
* Protection against visitor escape: visitors cannot access Windows Explorer, the command prompt, or settings.

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

* Allow inbound and outbound traffic within the local subnet and private IP ranges (`10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`).
* Block all outbound traffic to public internet addresses.
* Supports multi-machine installations, networked sensor arrays, OSC controllers, and local media servers without internet exposure.

### Internet Enabled

* Normal networking rules apply.
* Used exclusively by artworks that require live external internet connectivity.

---

## 7. Windows Exhibition Configuration

Windows configuration is applied idempotently by `ExhibitOSManager.exe` running elevated:

* creation of the passwordless `ArtworkUser` account
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

Artwork PC Manager assumes installations may operate unattended for weeks.

### Windows Job Objects

`ExhibitWatchdog.exe` runs inside the `ArtworkUser` session as the custom shell. All supervised artwork child processes (`mpv.exe`, `node.exe`, `msedge.exe`, or custom application executables) are assigned to a **Windows Job Object** configured with:

```csharp
JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
```

When an artwork exits, needs restarting, or closes at the end of the day, terminating the Job Object guarantees atomic destruction of the entire process tree, preventing orphaned background processes from lingering or locking resources.

### Supervision Architecture

```text
Static Web Artwork:
    Job Object
    ├── node.exe (bundled static server on localhost)
    └── msedge.exe (kiosk browser pointing to localhost)

Backend Web Artwork:
    Job Object
    ├── node.exe (artwork server.js)
    └── [Readiness check: HTTP poll until responsive]
        └── msedge.exe (kiosk browser)

Video Folder:
    Job Object
    └── mpv.exe (standalone player)

Application:
    Job Object
    └── artwork executable
```

### Recovery & Health Checks

* If a supervised artwork process exits unexpectedly during exhibition hours, the watchdog attempts to restart it.
* Exponential backoff and maximum retry thresholds prevent unrecoverable crash loops.
* For Web Artworks, the watchdog verifies the HTTP server is responsive before launching or reloading the browser.
* All lifecycle events, process starts, exits, and crash attempts are logged to `C:\ExhibitOS\logs\watchdog.log`.

---

## 9. Setup Interface (WinUI 3)

The setup interface (`ExhibitOSManager.exe`) is a simple, modern wizard built with **WinUI 3** designed for non-experts.

Implementation terminology (Winlogon keys, Job Objects, firewall rules, WFP filters) is hidden behind clear, user-focused language.

### Step 1 — Artwork

Ask: **What should this computer run?**

Options:
* **Video Folder**
* **Web Artwork**
* **Application**

Select artwork folder. Automatically inspect and provide immediate feedback:
* `✓ Found 4 video files (mp4, mkv)`
* `✓ Web artwork detected (Static)`
* `✓ Web artwork with Node backend detected`
* `✓ Executable found: ExhibitionWork.exe`
* `✗ No video files found in selected folder`
* `✗ No index.html or server.js found`

### Step 2 — Exhibition

Configure:
* **Opening time** (default `07:00`)
* **Closing time** (default `20:00`)
* **Morning reboot time** (default `06:45`)
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
* **Display Selection**: Automatic (primary) or specific connected display
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

Copies artwork files, writes `C:\ExhibitOS\config\exhibition.json`, provisions `ArtworkUser`, configures custom shell, creates Task Scheduler jobs, and configures firewall rules.

After completion, prompt: **Restart and Enter Exhibition Mode**.

---

## 10. Maintenance Interface

When `ExhibitOSManager.exe` is launched on an already-configured PC by an Administrator, display the **Maintenance Dashboard** instead of the setup wizard:

```text
EXHIBIT OS

● READY FOR EXHIBITION

Artwork: Bergen Rain (Web Artwork with Backend)
Status: Active (within exhibition hours)
Schedule: 07:00 – 20:00
Overnight: Sleep (Missed-reboot fallback active)
Network: Offline Exhibition
Audio: Line Out (Realtek High Definition Audio)

[ Launch / Test Artwork ]
[ Stop Artwork ]
[ Run System Diagnostic ]

[ Edit Configuration ]
[ Restore PC to Normal Use ]
```

Technicians can immediately check operational status, trigger tests, or edit configuration.

---

## 11. Exhibition Readiness Test

The **Run System Diagnostic** button checks:

* Artwork files present in `C:\ExhibitOS\artwork`
* Bundled runtimes present (`mpv.exe`, `node.exe`)
* Backend entry point and `node_modules` present (where applicable)
* Backend readiness probe succeeds
* `ArtworkUser` account exists and passwordless logon is configured
* Custom shell registry key is correctly pointing to `ExhibitWatchdog.exe`
* Windows Firewall rules correctly enforce the selected networking mode
* Selected audio playback device is connected and available
* Scheduled reboot task is registered in Task Scheduler
* Log files are writable

Result displayed clearly:
**READY FOR EXHIBITION** or **PROBLEMS DETECTED** (with clear repair instructions).

---

## 12. Reconfiguration and Removal

Provide an administrator action: **Restore PC to Normal Use**.

This cleanly reverses all modifications:
* Restores default Windows shell (`explorer.exe`) for all users
* Removes custom firewall rules
* Disables `AutoAdminLogon`
* Removes scheduled Task Scheduler reboot jobs
* Optionally deletes or disables the `ArtworkUser` account
* Restores normal Windows power and notification settings

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
   • Register Custom Shell                    • Winlogon Shell:
   • Task Scheduler Reboot                        ExhibitWatchdog.exe
   • WFP / Firewall Rules                               │
                                                        ▼
                                               Windows Job Object
                                             ┌──────────┼──────────┐
                                             ▼          ▼          ▼
                                          mpv.exe   node.exe   App.exe
                                                        │
                                                 [HTTP Ready?]
                                                        │
                                                        ▼
                                                   msedge.exe
```

### Component Details

1. **`ExhibitOSManager.exe`**:
   * Windows desktop app built with **C# / .NET 8 or 9** and **WinUI 3**.
   * Packaged as a self-contained executable.
   * Runs elevated with administrator privileges for setup, testing, and maintenance.
2. **`ExhibitWatchdog.exe`**:
   * Lightweight, headless C# executable running in the `ArtworkUser` session.
   * Configured as the custom Winlogon shell.
   * Manages `SetThreadExecutionState`, enforces exhibition hours, controls Job Objects, and monitors artwork health.
3. **`exhibition.json`**:
   * Declarative desired state configuration file stored in `C:\ExhibitOS\config\exhibition.json`.
4. **Bundled Runtimes**:
   * Pinned Node.js LTS portable build in `runtime/bin/node/`.
   * Pinned `mpv` standalone build in `runtime/bin/mpv/`.
   * System **Microsoft Edge** in kiosk mode for web rendering.

---

## 14. Development and Testing

* Primary testing in disposable **Windows 11 virtual machines** with clean snapshots.
* Verify:
  1. Automated setup and idempotent re-provisioning
  2. Auto-login into `ArtworkUser` without password prompt
  3. Custom shell launches watchdog without `explorer.exe` (no taskbar, no start menu)
  4. `Ctrl+Alt+Delete` allows switching back to Admin
  5. Process tree termination via Job Objects (killing watchdog terminates Edge + Node cleanly)
  6. Missed morning reboot fallback after manual wake from sleep
  7. Offline firewall rules block internet while preserving `localhost`
  8. Full restoration to normal PC state
* Physical mini-PC hardware verification for HDMI audio routing, projector blackout behavior, and sleep/wake timers.
