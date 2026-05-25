#!/system/bin/sh
#
# Capture labeled evdev events for mapping/debugging.
#
# Usage:
#   sh capture_evdev_events.sh /dev/input/event10 15

set -e

EVENT="${1:-$EVENT}"
SECONDS_TO_CAPTURE="${2:-15}"
OUT="${OUT:-/data/local/tmp/gamepad_events_$(date +%Y%m%d_%H%M%S).log}"

[ -n "$EVENT" ] || {
    echo "Usage: sh capture_evdev_events.sh /dev/input/eventX [seconds]" >&2
    exit 1
}

[ -e "$EVENT" ] || {
    echo "Missing event device: $EVENT" >&2
    exit 1
}

echo "Capturing $EVENT for ${SECONDS_TO_CAPTURE}s"
echo "Move sticks, press all buttons, use the D-pad."
echo "Output: $OUT"

(
    echo "# device: $EVENT"
    echo "# start : $(date)"
    getevent -lt "$EVENT"
) > "$OUT" &

pid="$!"
sleep "$SECONDS_TO_CAPTURE"
kill "$pid" 2>/dev/null || true
wait "$pid" 2>/dev/null || true

echo "# end   : $(date)" >> "$OUT"
echo "Done: $OUT"
