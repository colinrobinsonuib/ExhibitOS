# Agent Guidelines for ExhibitOS

> [!CAUTION]
> ### CRITICAL SAFETY WARNING: DEVELOPMENT PC CONSTRAINTS
> **DO NOT RUN ANY EXHIBITION SETTING CHANGES ON THIS PC.**
> 
> * **This host is the DEVELOPMENT PC, NOT the exhibition target PC.**
> * **DO NOT create local user accounts** (such as `ArtworkUser`) on this PC.
> * **DO NOT alter registry settings** on this PC (e.g., `Winlogon\Shell`, `AutoAdminLogon`, `DisableTaskMgr`, power policies, hotkey suppression).
> * **DO NOT apply firewall rules or WFP filters** on this PC (e.g., blocking non-loopback network traffic).
> * **DO NOT register scheduled tasks** for system reboots or power actions on this PC.
> * **DO NOT reboot, sleep, or shut down** this host PC.
> 
> The application and system provisioning logic will run on a **virtual machine** (or dedicated target hardware) that is provisioned for testing. Any local testing on this development machine must only compile code, run unit tests, or run UI components with simulated/mocked provisioning actions.

---

## Project Overview

ExhibitOS is an unattended Windows artwork PC manager for exhibitions. It turns a Windows 11 PC into an appliance running a restricted user environment (`ArtworkUser`) with a custom shell (`ExhibitWatchdog.exe`) supervising artwork processes (Video Folder via mpv, Static/Backend Web via Edge kiosk, or Applications via Job Objects).

Key specification documents:
- [`SPEC.md`](file:///c:/Users/Colin/Projects/ExhibitOS/SPEC.md): Full product specification, architecture, security policies, and test matrix.

---

## Architectural Principles

1. **Strict Separation of Provisioning Actions**:
   - All Windows-specific provisioning logic (user account creation, user rights assignments, registry changes, firewall modifications, task scheduling) must be isolated behind an abstraction layer / interface (e.g., `IWindowsProvisioningService`).
   - The default implementation on the development environment or in development/dry-run mode should simulate or log changes, or verify execution context before running destructive or modifying operations.
   - Destructive / system-altering operations must only be executed inside a verified target environment (such as the target VM or exhibition PC).

2. **Components**:
   - **`ExhibitOSManager`** (`WinUI 3` / .NET 10 / C#): Administrative setup wizard and maintenance dashboard.
   - **`ExhibitWatchdog`** (Headless .NET / C#): Custom Winlogon shell running in `ArtworkUser` session, managing Job Objects, health monitoring, schedule enforcement, and recovery.
   - **`ExhibitOS.Core`** / **Shared Libraries**: Data models (`exhibition.json`), process supervisors, Job Object wrappers, port allocators, health check probes.
   - **Bundled Runtimes**: Standalone `mpv.exe` and portable Node.js LTS with `static-server.js`.
   - **`ExhibitOSSetup`**: Installer for distributing the application and runtimes.

3. **Development Tooling**:
   - Ensure development prerequisites (e.g. .NET SDK) are used without compromising host configuration.
   - Use VirtualBox (`ExhibitOS` VM) for end-to-end testing of provisioning, custom shell execution, visitor lockdown, and restoration.
