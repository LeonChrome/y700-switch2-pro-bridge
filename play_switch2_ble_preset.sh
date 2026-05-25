#!/system/bin/sh
set -eu

OUT=/data/local/tmp/switch2_ble_write.txt
PRESET="${1:?preset number required}"
HOLD="${2:-0.35}"

hex=$(printf '%02x' "$PRESET")
printf 'cmd 0a91010200080000%s00000000000000\n' "$hex" > "$OUT"
sleep "$HOLD"
printf 'cmd 0a910102000800000000000000000000\n' > "$OUT"
