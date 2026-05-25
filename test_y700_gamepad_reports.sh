#!/system/bin/sh
#
# Send a visible sequence of 8-byte gamepad reports to /dev/hidg0.
# Open Windows joy.cpl before running this script.

set -e

HID="${HID:-/dev/hidg0}"
UDC_STATE="${UDC_STATE:-/sys/class/udc/a600000.dwc3/state}"
PAUSE="${PAUSE:-0.35}"
RETRIES="${RETRIES:-50}"
RETRY_DELAY="${RETRY_DELAY:-0.2}"
NEUTRAL='\x00\x00\x08\x00\x00\x00\x00\x00'

die() {
    echo "ERROR: $*" >&2
    exit 1
}

write_report() {
    report="$1"
    i=0
    while [ "$i" -lt "$RETRIES" ]; do
        if printf '%b' "$report" > "$HID" 2>/dev/null; then
            return 0
        fi
        i=$((i + 1))
        sleep "$RETRY_DELAY"
    done

    if [ -e "$UDC_STATE" ]; then
        echo "UDC state after waiting: $(cat "$UDC_STATE" 2>/dev/null || true)" >&2
    fi
    # Run once more without stderr suppression so the real kernel error is visible.
    printf '%b' "$report" > "$HID"
}

send() {
    label="$1"
    report="$2"
    echo "$label"
    write_report "$report"
    sleep "$PAUSE"
    write_report "$NEUTRAL"
    sleep "$PAUSE"
}

[ -e "$HID" ] || die "Missing $HID. Run setup_y700_gamepad_v2.sh first."

echo "Waiting for host to accept HID input reports..."
write_report "$NEUTRAL"
sleep "$PAUSE"

send "Button 1"  '\x01\x00\x08\x00\x00\x00\x00\x00'
send "Button 2"  '\x02\x00\x08\x00\x00\x00\x00\x00'
send "Button 8"  '\x80\x00\x08\x00\x00\x00\x00\x00'
send "Button 9"  '\x00\x01\x08\x00\x00\x00\x00\x00'
send "Button 16" '\x00\x80\x08\x00\x00\x00\x00\x00'

send "Hat up"    '\x00\x00\x00\x00\x00\x00\x00\x00'
send "Hat right" '\x00\x00\x02\x00\x00\x00\x00\x00'
send "Hat down"  '\x00\x00\x04\x00\x00\x00\x00\x00'
send "Hat left"  '\x00\x00\x06\x00\x00\x00\x00\x00'

send "X right"   '\x00\x00\x08\x7f\x00\x00\x00\x00'
send "X left"    '\x00\x00\x08\x81\x00\x00\x00\x00'
send "Y down"    '\x00\x00\x08\x00\x7f\x00\x00\x00'
send "Y up"      '\x00\x00\x08\x00\x81\x00\x00\x00'
send "Z plus"    '\x00\x00\x08\x00\x00\x7f\x00\x00'
send "Z minus"   '\x00\x00\x08\x00\x00\x81\x00\x00'
send "Rz plus"   '\x00\x00\x08\x00\x00\x00\x7f\x00'
send "Rz minus"  '\x00\x00\x08\x00\x00\x00\x81\x00'

echo "Final neutral report..."
write_report "$NEUTRAL"
echo "Done."
