# Contributing

Contributions are welcome. This project is still experimental, so the most useful contributions are clear, measured, and well documented.

## Recommended Contribution Areas

- Pico 2W / RP2350 / nRF52840 / other board ports.
- HID descriptor experiments.
- macOS / Android compatibility testing.
- BLE rate and latency testing.
- Documentation improvements.
- Release packaging improvements.

## Please Avoid Large Binaries In The Main Branch

Please do not submit large generated binaries directly to the main branch unless they are explicitly needed for source-level development.

Recommended release assets:

- firmware zip files
- manager EXE packages
- JAR/EXE helper binaries
- packaged test tools

These should normally be attached to GitHub Releases, with SHA256 checksums when possible.

## Board Port Contributions

If you contribute a new board port, please include:

- hardware model and exact board revision
- MCU / radio chip
- SDK and version
- BLE connection path
- USB HID output path
- flashing method
- tested host OS
- BLE input rate
- USB report rate
- known limitations

## Test Result Contributions

If you contribute test results, please include as much of this as possible:

- host OS
- board type
- firmware version
- controller firmware version, if known
- BLE input rate
- USB report rate
- Steam / OS detection result
- input mapping result
- rumble result
- known issues
- logs or screenshots, if available

## Documentation Contributions

Documentation improvements are very welcome, especially:

- clearer flashing steps
- driver notes
- troubleshooting cases
- host compatibility notes
- release packaging notes
- translations

## License

By contributing, you agree that your contribution may be distributed under the Apache License 2.0 used by this repository.

