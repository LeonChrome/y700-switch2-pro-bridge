# Release Packaging Plan

Date: 2026-06-01

This is a documentation-only plan. Do not delete existing files only because this document exists.

## 1. User Download Policy

Normal users should download release assets from GitHub Releases:

- Manager EXE
- all-in-one flasher EXE
- firmware zip
- Y700 historical payload zip/JAR, where relevant
- packaged test tools

The repository main branch should primarily contain:

- source code
- scripts
- documentation
- small metadata files
- release templates

## 2. Stable vs Legacy Naming

README and release notes should clearly separate:

- **Stable**: V5.0.0 ESP32-S3 Pro2 Bridge
- **Historical stable**: V4.0.0 ESP32-S3 Pro2 Bridge
- **Legacy**: Y700 Android USB Gadget route

V5.0.0 is the current recommended public path. It bundles V5 firmware payload, defaults USB report output to 250 Hz for gyro stability, keeps 1000 Hz optional, and documents that audio/voice is not implemented.

## 3. Binary File Guidance

Do not forcibly remove existing binaries from history or from the current tree without a separate cleanup decision.

However, future cleanup should consider moving large generated binaries out of the repository root:

- `*.exe`
- `*.jar`
- firmware `*.bin`
- release `*.zip`

Recommended future policy:

- keep source and build scripts in main
- attach generated binaries to GitHub Releases
- keep only small checked-in firmware manifests where useful
- avoid committing `bin/`, `obj/`, and firmware build output unless explicitly needed

## 4. SHA256

Future public releases should include SHA256 checksums for:

- all-in-one Manager EXE
- firmware zip
- firmware-only zip
- standalone Manager EXE
- any historical Y700 payload zip

Suggested format:

```text
SHA256  filename
```

or a simple table in the release notes.

## 5. Release Asset Structure

Recommended stable ESP32-S3 release assets:

```text
esp32s3-pro2-bridge-vX.Y.Z-YYYYMMDD.zip
Y700Switch2Manager-aio-vX.Y.Z.exe
SHA256SUMS.txt
```

Optional split assets, when useful:

```text
esp32s3-pro2-bridge-firmware-vX.Y.Z-YYYYMMDD.zip
Y700Switch2Manager-vX.Y.Z.exe
```

## 6. README Release Guidance

README should always answer:

- Which version is stable?
- Which route is legacy?
- Which file should a normal tester download?
- Which hardware port is for flashing?
- Which hardware port is for USB HID output?
- Which features are verified?
- Which features are planned or not tested?
- What is the recommended USB report rate?
- What is intentionally not implemented, especially audio/voice?

## 7. Safe Cleanup Recommendation

If the repository is cleaned later:

1. Open a separate cleanup PR/commit.
2. List every binary file to be moved or removed.
3. Confirm each file is already available in GitHub Releases or can be regenerated.
4. Do not delete historical Y700 artifacts without preserving a release tag or release asset.
5. Update README and release notes after cleanup.
