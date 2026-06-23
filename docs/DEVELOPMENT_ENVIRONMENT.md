# Development Environment

The repository supports a project-local Windows toolchain. ESP-IDF, the
cross compiler, CMake, Ninja, OpenOCD, esptool, and Python are stored below
`.toolchain/` and are excluded from Git.

## One-Time Setup

From the repository root:

```powershell
.\tools\setup_dev_environment.ps1
```

The setup script:

- enables Git long-path support
- clones the official ESP-IDF `v5.4.2` tag and its submodules
- installs only the ESP32-S3 tool set
- creates a project-local Python 3.11 runtime
- creates the ESP-IDF Python environment inside the project
- installs the Windows `subst` compatibility patch used by ESP-IDF 5.4.2
- validates the compiler, build tools, esptool, and CH343P control port

Official download sources:

- `https://github.com/espressif/esp-idf`
- `https://dl.espressif.com`
- `https://www.python.org`

## Environment Check

```powershell
.\tools\esp32s3\check_environment.ps1
```

Every ESP32-S3 helper automatically prefers:

```text
.toolchain/esp-idf-v5.4.2
.toolchain/espressif-tools
.toolchain/python-3.11.9
```

On Windows the helpers also map the repository to the first free drive from
`Y:`, `X:`, `W:`, `V:`, or `U:`. ESP-IDF otherwise expands the repository's
long handoff path until Ninja exceeds the Windows process command-line limit.
The mapping points at this repository; it does not copy source or build output
outside the project.

Fresh firmware outputs are kept below `work/b/`. Older handoff build
directories are left untouched so their logs and binaries remain available
for comparison.

An explicit `-IdfPath` can still select another ESP-IDF installation, but
the scripts reject versions other than 5.4.2 to keep firmware reproduction
deterministic.

## Build

Build all five packaged firmware profiles:

```powershell
.\tools\build_all.ps1
```

Build all profiles and regenerate the all-in-one Manager:

```powershell
.\tools\build_all.ps1 -Package
```

Individual entry points remain available:

```powershell
.\tools\esp32s3\build.ps1
.\tools\esp32s3\build_v5_5_dualsense_identity.ps1 -Profile hid_audio_uac1_4ch_ds5like
.\tools\package_v5_9_manager.ps1 -SkipFirmwareBuild
```

## Hardware Ports

- CH343P USB: firmware flashing, serial diagnostics, and Manager commands
- ESP32-S3 native USB/OTG: emulated controller and audio device

Run `tools/esp32s3/detect_ports.ps1` instead of assuming a fixed COM number.

## Git History

The handoff bundle is stored at:

```text
_handoff/y700-switch2-pro-bridge-history-v5.9.1.bundle
```

The current workspace has been attached to that history. V5.9.1 handoff
changes intentionally remain visible as working-tree changes until they are
reviewed and committed as a coherent baseline.
