#!/system/bin/sh
set -eu

OUT=/data/local/tmp/switch2_ble_write.txt
CASE="${1:-help}"
HOLD="${2:-0.55}"
TARGET="${3:-cmd}"

write_cmd() {
  label="$1"
  hex="$2"
  echo "$label target=$TARGET"
  printf '%s %s\n' "$TARGET" "$hex" > "$OUT"
}

write_target() {
  target="$1"
  hex="$2"
  printf '%s %s\n' "$target" "$hex" > "$OUT"
}

stop_all() {
  # Known preset stop, plus neutral candidates for the active target.
  write_target cmd 0a910102000800000000000000000000
  sleep 0.08
  write_target "$TARGET" 0a910102000800008701201187012011
  sleep 0.08
  write_target "$TARGET" 10508701201187012011
}

case "$CASE" in
  preset)
    # Known positive control: should produce a short preset effect if BLE is connected.
    write_cmd "preset positive control 01" "0a910102000800000100000000000000"
    sleep "$HOLD"
    stop_all
    ;;

  env4)
    # BLE envelope, 8-byte payload as classic Switch Pro 4+4 rumble data.
    write_cmd "hd candidate env4 active: envelope + 87152751/87152751" "0a910102000800008715275187152751"
    sleep "$HOLD"
    stop_all
    ;;

  env5)
    # BLE envelope, first 8 bytes taken from the Switch2-style 5-byte rumble frame stream.
    write_cmd "hd candidate env5 active: envelope + 8715275171871527" "0a910102000800008715275171871527"
    sleep "$HOLD"
    stop_all
    ;;

  raw10)
    # Direct Switch Pro rumble-only report shape: report 0x10, packet 0x50, 4+4 rumble bytes.
    write_cmd "hd candidate raw10 active: 1050 + 87152751/87152751" "10508715275187152751"
    sleep "$HOLD"
    stop_all
    ;;

  raw64)
    # Direct Switch2/Steam-like 64-byte HID output frame captured from the Windows side.
    write_cmd "hd candidate raw64 active: direct 02 50 ... frame" "025087152751710000000000000000000050871527517100000000000000000000000000000000000000000000000000000000000000000000000000000000000000"
    sleep "$HOLD"
    stop_all
    ;;

  raw64-mid)
    # Direct Switch2/Steam-like 64-byte HID output frame at about 50% SDL scale.
    write_cmd "hd candidate raw64 mid: direct 02 50 ... frame" "025087892391380000000000000000000050878923913800000000000000000000000000000000000000000000000000000000000000000000000000000000000000"
    sleep "$HOLD"
    stop_all
    ;;

  raw64-bzzz)
    # Runtime frame observed from BzzzController/Steam, about high=71%, low=60%.
    write_cmd "hd candidate raw64 bzzz: direct 02 50 ... frame" "025087052511440000000000000000000050870525114400000000000000000000000000000000000000000000000000000000000000000000000000000000000000"
    sleep "$HOLD"
    stop_all
    ;;

  raw10-mid)
    # Switch Pro-like rumble-only report shape, about 50% SDL scale.
    write_cmd "hd candidate raw10 mid: 1050 + 87892391/87892391" "10508789239187892391"
    sleep "$HOLD"
    stop_all
    ;;

  stop)
    echo "stop all"
    stop_all
    ;;

  *)
    echo "Usage: $0 {preset|env4|env5|raw10|raw10-mid|raw64|raw64-mid|raw64-bzzz|stop} [hold_seconds] [target]"
    echo
    echo "target aliases: cmd, 649d, 3dac, 4147, fdf, cc48"
    echo "preset: known BLE preset positive control"
    echo "env4: BLE envelope carrying classic 4+4 HD rumble bytes"
    echo "env5: BLE envelope carrying first 8 bytes of Switch2-style HD rumble stream"
    echo "raw10: direct Switch Pro rumble-only report candidate"
    echo "raw64: direct Steam/Y700 64-byte HID output candidate"
    echo "raw64-mid/raw64-bzzz/raw10-mid: lower-intensity variants"
    exit 2
    ;;
esac

echo "done $CASE"
