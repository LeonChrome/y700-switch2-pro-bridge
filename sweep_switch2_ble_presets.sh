#!/system/bin/sh
set -eu

OUT=/data/local/tmp/switch2_ble_write.txt
START="${1:-1}"
END="${2:-15}"
HOLD="${3:-0.22}"
GAP="${4:-0.85}"

send_preset() {
  value="$1"
  hex=$(printf '%02x' "$value")
  printf 'cmd 0a91010200080000%s00000000000000\n' "$hex" > "$OUT"
}

send_stop() {
  printf 'cmd 0a910102000800000000000000000000\n' > "$OUT"
}

echo "Switch 2 BLE preset sweep: $START..$END hold=${HOLD}s gap=${GAP}s"
send_stop
sleep 0.4

i="$START"
while [ "$i" -le "$END" ]; do
  hex=$(printf '%02x' "$i")
  echo "preset $i / 0x$hex"
  send_preset "$i"
  sleep "$HOLD"
  send_stop
  sleep "$GAP"
  i=$((i + 1))
done

send_stop
echo "Done."
