# Artwork PC Manager

## 1. Goal

Build a Windows application that turns an ordinary Windows PC or mini-PC into a reliable, unattended **artwork computer** for exhibitions.

The intended users are exhibition producers, artists, and technicians who may not be Windows experts.

The target workflow is:

**Run application → choose artwork → configure exhibition → test → configure PC → reboot.**

After configuration, the computer should behave like an appliance:

* automatically enter the artwork environment after boot
* run the artwork fullscreen
* prevent visitors from accessing Windows
* follow exhibition opening/closing hours
* recover from common failures
* require minimal daily intervention

Keep the application deliberately focused. This is not intended to become a general-purpose kiosk-management or fleet-management platform.

---

# 2. Artwork Types

Support three artwork types initially.

## Video Folder

The user selects a folder containing one or more videos.

The system should:

* discover supported video files
* play them in filename order
* play fullscreen without player UI
* loop continuously
* hide the mouse cursor
* support audio
* automatically restart playback if the player exits unexpectedly

A single-video artwork is simply a folder containing one video.

Use a reliable video player suitable for unattended exhibition playback.

## Web Artwork

The user selects a folder containing a browser-based artwork.

There are two forms of Web Artwork.

### Static Web Artwork

For ordinary HTML/JavaScript artworks with no backend requirements.

Artwork PC Manager should:

* validate the artwork
* provide an integrated local static HTTP server
* serve the artwork from localhost
* open it automatically in a fullscreen kiosk browser
* monitor the required runtime components
* recover if the browser/server exits unexpectedly

The local server must continue functioning when external networking is disabled.

### Backend Web Artwork

Some browser artworks require artwork-specific backend functionality.

Examples might include:

* silent printing
* communicating with hardware
* filesystem operations
* network requests
* invoking system functionality
* processing or generating content

**Artwork PC Manager must not implement APIs for these behaviors.**

The backend belongs to the individual artwork.

Artwork PC Manager's responsibility is only to provide and supervise the environment in which that backend runs.

Use **Node.js as the supported backend runtime** for these artworks.

A backend-enabled artwork should contain its own Node application and all of the code defining its API and behavior.

Conceptually:

```text
Artwork/
    package.json
    server.js
    public/
        index.html
        app.js
        assets/
```

The exact artwork structure may evolve during implementation.

Artwork PC Manager should:

* provide/manage a known Node.js runtime
* identify a backend-enabled artwork
* start its backend with the appropriate working directory
* wait until the backend is ready
* open its localhost URL in the kiosk browser
* monitor the backend process
* restart it if it unexpectedly exits
* stop it appropriately when the exhibition closes

The artwork's Node server may serve both its frontend and its artwork-specific API.

Artwork PC Manager should **not need to understand the API endpoints or functionality provided by an artwork backend**.

For example:

```text
Browser
   │
   │ POST /print
   ▼
Artwork-specific Node backend
   │
   └── Windows printer
```

Another artwork could use completely different endpoints and native functionality without requiring any changes to Artwork PC Manager.

Backend artworks should arrive ready to run. Exhibition setup should not depend on running `npm install`, downloading dependencies, or otherwise accessing the Internet.

## Application

The user selects an artwork folder and executable.

This supports interactive works such as Unity applications.

Artwork PC Manager should:

* launch the executable automatically
* run it as the primary exhibition interface
* monitor it
* relaunch it if it unexpectedly exits

---

# 3. Managed Artwork

When configuring a PC, copy the selected artwork into an application-managed location on the local machine.

Do not depend on the original USB drive, Downloads folder, network share, etc. remaining available.

Conceptually:

```text
ArtworkPC/
    artwork/
    config/
    logs/
    runtime/
```

The exact filesystem structure is an implementation decision.

---

# 4. Exhibition / Kiosk Environment

Create a dedicated restricted Windows account for running the artwork.

Configure:

* automatic login to the artwork account after boot
* restricted access to Windows
* automatic artwork startup
* fullscreen presentation
* no useful access to the Windows desktop, Start menu, taskbar, Settings, File Explorer, etc.
* protection against ordinary attempts to exit the artwork

Prefer Windows' actual kiosk/restricted-user facilities, including Assigned Access and related Windows capabilities, rather than implementing a pseudo-kiosk entirely through keyboard interception.

Different artwork types may use different underlying Windows mechanisms where necessary.

Visitors may have access to keyboards and mice, so preventing escape from the artwork is a core requirement.

`Ctrl+Alt+Delete` should remain the standard technician escape route to the Windows security/sign-in interface.

A separate administrator account must remain available for maintenance.

Applying system configuration will normally require administrator privileges.

---

# 5. Exhibition Schedule

Allow configuration of daily:

* opening time
* closing time
* morning reboot time

Typical configuration:

```text
Restart: 06:45
Open:    07:00
Close:   20:00
```

During exhibition hours:

* artwork should be running
* display should remain active
* screensaver should not activate
* automatic sleep should not interrupt the work

Outside exhibition hours:

* artwork does not need to run
* display should be turned off and/or the computer put into an appropriate low-power state

Perform a scheduled reboot before opening each day so the exhibition starts from a clean state.

Choose a robust approach to overnight power management. Do not depend on sleep/wake behavior if it proves unreliable across typical mini-PC hardware.

Complicated calendars and date-range scheduling are not required initially.

---

# 6. Networking

Provide three modes.

## Offline Exhibition

Default and recommended.

Disable external networking during exhibition operation.

Disable Wi-Fi and Ethernet as appropriate while retaining localhost/loopback functionality.

## Internet Enabled

Leave networking available normally.

Used by artworks that require Internet connectivity.

## Local Network Only

Permit LAN communication while preventing Internet access.

This supports installations involving multiple local machines, OSC/networked devices, local servers, etc.

Networking restrictions are partly intended to reduce unwanted Windows/application updates and other Internet-dependent behavior during exhibitions.

---

# 7. Windows Exhibition Configuration

Configure Windows appropriately for unattended exhibition use.

This includes, where appropriate:

* artwork-account auto-login
* kiosk/restricted-user configuration
* automatic artwork startup
* disabling screensaver
* preventing sleep during exhibition hours
* suppressing disruptive notifications
* preventing unwanted Windows UI appearing over artwork
* preventing automatic update/restart behavior from interrupting exhibition hours
* appropriate display power behavior
* configured networking restrictions

Avoid unnecessary system modifications.

Keep Windows-specific provisioning isolated from general application logic.

---

# 8. Runtime and Reliability

Artwork PC Manager should assume installations may operate unattended for weeks.

Provide a runtime/watchdog responsible for maintaining the required artwork state.

For example:

```text
Static Web Artwork
    ├── static web server
    └── kiosk browser

Backend Web Artwork
    ├── artwork Node backend
    └── kiosk browser

Video Folder
    └── video player

Application
    └── artwork executable
```

If a required process exits unexpectedly, attempt to restart it.

For Web Artworks, ensure the server/backend is ready before launching or reloading the browser.

If repeated recovery attempts fail, rebooting the machine may be used as a last-resort recovery strategy.

Avoid creating unrecoverable restart loops.

Maintain useful logs.

---

# 9. Setup Interface

Use a simple wizard designed for non-experts.

Avoid exposing implementation terminology such as Assigned Access, Kestrel, Node processes, scheduled tasks, registry keys, etc. during normal use.

## Step 1 — Artwork

Ask:

**What should this computer run?**

Options:

* Video Folder
* Web Artwork
* Application

Select the artwork folder.

Validate it and provide simple feedback.

Examples:

```text
✓ Found 6 videos
```

```text
✓ Web artwork detected
```

```text
✓ Web artwork with backend detected
```

```text
✗ No web entry point could be found
```

Where practical, determine automatically whether a Web Artwork is static or backend-enabled rather than requiring the producer to understand the distinction.

For Application artworks, allow executable selection where necessary.

---

## Step 2 — Exhibition

Configure:

* opening time
* closing time
* morning reboot time
* networking mode

Default networking mode:

**Offline Exhibition**

Use sensible defaults.

---

## Step 3 — Display & Sound

Keep this simple.

At minimum consider:

* display selection, default Automatic
* volume
* fullscreen behavior
* cursor visibility

Artwork-specific settings should generally use sensible defaults.

Unusual options can live under **Advanced**.

---

## Step 4 — Test

Provide **Test Artwork** before committing system-level configuration.

Testing should run the artwork approximately as it will operate during the exhibition without first requiring the PC to enter the restricted kiosk environment.

Validate relevant components.

Examples:

### Video

* files readable
* video player launches
* playback starts
* display available
* audio device available where relevant

### Static Web

* local server starts
* artwork responds
* browser launches

### Backend Web

* Node runtime available
* backend launches
* backend reaches ready state
* configured localhost page responds
* browser launches

### Application

* executable exists
* application launches

Report failures in language useful to a non-expert, while retaining detailed logs for technicians/developers.

---

## Step 5 — Configure

Show a concise summary and:

**Configure This PC for Exhibition**

Apply the required:

* artwork installation
* runtime dependencies
* Windows configuration
* artwork account
* kiosk environment
* scheduling
* startup behavior
* networking configuration
* watchdog/runtime

After completion provide:

**Restart and Test**

After reboot, the machine should behave as it will during the actual exhibition.

---

# 10. Maintenance Interface

When Artwork PC Manager is opened by an administrator on an already-configured PC, show a simple maintenance/status screen rather than the initial setup wizard.

For example:

```text
ARTWORK PC

● READY

Artwork: Bergen Rain
Type: Web Artwork
Status: Running

Schedule: 07:00–20:00
Network: Offline
Next restart: 06:45 tomorrow

[ Launch Artwork ]
[ Stop Artwork ]
[ Restart Artwork ]

[ Edit Configuration ]
[ Run System Test ]
```

The exact UI is flexible.

The goal is that an exhibition technician can quickly understand whether the computer is correctly configured and operating.

---

# 11. Exhibition Readiness Test

Provide **Run System Test**.

Check whatever is relevant to the configured artwork, including:

* artwork files available
* required runtime available
* artwork launches
* browser/player/application available
* backend starts where applicable
* startup configuration exists
* artwork account exists
* kiosk configuration exists
* watchdog/runtime functioning
* schedule configured
* networking state correct
* screensaver/sleep configuration correct
* display detected
* audio device detected where relevant

Present a clear result:

**READY FOR EXHIBITION**

or:

**PROBLEMS FOUND**

Provide actionable descriptions of failures.

Detailed technical information can be available separately.

---

# 12. Reconfiguration and Removal

Support changing an existing installation.

For example:

* replace artwork
* change artwork type
* change exhibition hours
* change networking mode
* change display/sound settings

Provide an administrator action to:

**Restore PC to Normal Use**

Undo system modifications made by Artwork PC Manager where reasonably possible.

Do not assume uninstalling the application itself is sufficient.

---

# 13. Developer Mode

Most development and integration testing will happen in disposable Windows virtual machines.

Provide useful developer/testing capabilities such as:

* simulate exhibition opening
* simulate exhibition closing
* simulate morning startup
* start/stop runtime
* deliberately terminate artwork processes to test recovery
* inspect watchdog state
* inspect effective configuration
* inspect logs

Developer features should not clutter the normal interface.

---

# 14. Architecture

Separate **desired state** from **Windows provisioning**.

Conceptually:

```text
Artwork:
    type: web
    mode: backend
    path: ...

Schedule:
    open: 07:00
    close: 20:00
    reboot: 06:45

Network:
    mode: offline
```

The exact configuration format is an implementation decision.

Windows provisioning should make the computer conform to that desired state.

Prefer idempotent configuration operations where practical. Reapplying configuration should converge on the requested state rather than creating duplicate accounts, tasks, services, startup entries, etc.

Keep these responsibilities conceptually separate:

```text
Management UI
      │
      ▼
Configuration / Desired State
      │
      ├──────────────► Windows Provisioning
      │
      └──────────────► Artwork Runtime / Watchdog
                              │
             ┌────────────────┼────────────────┐
             ▼                ▼                ▼
         Video Player      Web Runtime     Application
                              │
                       ┌──────┴──────┐
                       ▼             ▼
                    Static       Artwork Node
                    Server         Backend
```

Exact process boundaries and implementation technologies are flexible.

---

# 15. Backend Web Artwork Boundary

This architectural boundary is important.

**Artwork PC Manager owns infrastructure and lifecycle.**

It is responsible for:

* providing the supported Node runtime
* launching the artwork backend
* stopping it
* monitoring it
* restarting it
* determining when it is ready
* opening the browser
* managing exhibition scheduling

**The artwork owns behavior.**

Its backend is responsible for whatever that artwork specifically needs:

* API endpoints
* printing logic
* native/system calls
* hardware communication
* network communication
* data processing
* filesystem operations
* other artwork-specific functionality

Do not add artwork-specific behavior to Artwork PC Manager.

Do not attempt to design a universal API covering anticipated artwork requirements.

If a future artwork requires unusual functionality, implement that functionality in that artwork's backend.

Because these backend modifications are prepared by exhibition technical staff rather than arbitrary visitors, we do not need to build a general-purpose sandbox/plugin system for backend artwork code.

---

# 16. Development and Testing

Use disposable Windows 11 virtual machines with clean snapshots/checkpoints as the primary integration environment.

Typical development cycle:

1. restore clean Windows VM
2. install/run Artwork PC Manager
3. configure artwork
4. reboot
5. verify auto-login
6. verify artwork startup
7. test visitor restrictions
8. test Ctrl+Alt+Delete technician escape
9. test process crashes/recovery
10. test schedule behavior
11. test networking modes
12. test restoration/removal
13. revert VM

Do not perform destructive Windows configuration testing against the developer's normal workstation unless explicitly intended.

Eventually validate on physical exhibition mini-PC hardware, particularly for:

* HDMI/projector behavior
* display detection
* audio devices
* GPU/video decoding
* USB peripherals
* printers
* hardware used by backend artworks
* BIOS behavior
* power-loss recovery
* display power management
* sleep/wake behavior

---

# 17. Scope

Prioritize:

1. reliability during exhibitions
2. preventing visitor access to Windows
3. ease of use for non-experts
4. automatic recovery
5. maintainability/testability
6. minimal complexity

The initial version does not need:

* configuration export/import
* cloud management
* fleet management
* remote dashboards
* complicated calendars
* application user accounts
* telemetry infrastructure
* plugin systems
* generic native APIs for browser artworks
* support for arbitrary backend runtimes

Do not add features simply because they might eventually be useful.

When this specification leaves an implementation detail open, investigate the available Windows mechanisms and choose the simplest robust solution.

Before beginning substantial implementation, review the requirements, investigate the relevant current Windows APIs/capabilities, and propose a concise architecture and implementation plan. Flag any requirements that conflict with Windows limitations or that would materially complicate reliability. Do not over-engineer around hypothetical future requirements.

The core objective remains:

> **Take a Windows mini-PC, point Artwork PC Manager at an artwork, and make that PC reliably behave like a dedicated exhibition appliance.**
