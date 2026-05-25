#!/system/bin/sh
set -eu

OUT=/data/local/tmp/switch2_ble_write.txt
TARGETS="${*:-cmd cc48 3dac 4147 649d fdf}"
ZERO10=00000000000000000000
ZERO41=0000000000000000000000000000000000000000000000000000000000000000000000000000000000

send_line() {
  target="$1"
  hex="$2"
  printf '%s %s\n' "$target" "$hex" > "$OUT"
}

send_hid_frame() {
  target="$1"
  report_seq="$2"
  data="$3"
  send_line "$target" "02${report_seq}${data}${ZERO10}${report_seq}${data}${ZERO41}"
}

send_stop() {
  target="$1"
  send_hid_frame "$target" 50 0000000000
}

echo "Writing BLE haptic test commands through $OUT"
echo "Targets: $TARGETS"

# Known Switch 2 Pro USB-side haptic samples from the ProCon2 WebHID test pattern.
PATTERN="
93 35 36 1c 0d
a8 29 c5 dc 0c
75 21 b5 5d 13
75 f5 70 1e 11
ba 55 40 1e 08
90 31 10 9e 00
75 15 73 1e 11
7b 95 92 5c 13
8d c5 a1 1b 10
7e 31 c1 dc 0b
6f 2d 31 dc 03
75 19 41 9b 03
"

for target in $TARGETS; do
  echo "Testing $target"

  # BLE command-channel haptics/config candidate from the Switch 2 init sequence.
  if [ "$target" = "cmd" ] || [ "$target" = "649d" ]; then
    send_line "$target" 0a9101080014000001ffffffffffffffff350046000000000000000000
    sleep 0.08
    send_line "$target" 0391010a0004000009000000
    sleep 0.08
  fi

  seq=0
  echo "$PATTERN" | while read a b c d e; do
    [ -n "${a:-}" ] || continue
    case "$seq" in
      0) s=50 ;;
      1) s=51 ;;
      2) s=52 ;;
      3) s=53 ;;
      4) s=54 ;;
      5) s=55 ;;
      6) s=56 ;;
      7) s=57 ;;
      8) s=58 ;;
      9) s=59 ;;
      10) s=5a ;;
      11) s=5b ;;
      12) s=5c ;;
      13) s=5d ;;
      14) s=5e ;;
      *) s=5f ;;
    esac
    send_hid_frame "$target" "$s" "${a}${b}${c}${d}${e}"
    seq=$((seq + 1))
    sleep 0.02
  done

  send_stop "$target"
  sleep 0.5
done

echo "Done. Check /data/local/tmp/switch2_ble_bridge.log for BLE write status."
