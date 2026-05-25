#!/system/bin/sh
set -u

ROOT="/data/local/tmp"
BRIDGE_LOG="$ROOT/switch2_ble_bridge.log"
RAW_LOG="$ROOT/switch2_ble_input_raw.log"
WRITE_FILE="$ROOT/switch2_ble_write.txt"
WAIT_READY_SECONDS="${1:-14400}"
READY_POLL_SECONDS="${2:-30}"
VALUE_END="${3:-145}"
PRESET_HEX="${4:-01}"
STOP_HEX="0a910102000800000000000000000000"
RUN_ID="$(date +%Y%m%d_%H%M%S)"
OUT_DIR="$ROOT/switch2_payload_sweep_$RUN_ID"
EVENTS="$OUT_DIR/events.tsv"
STATUS="$OUT_DIR/status.txt"

mkdir -p "$OUT_DIR"

log_status() {
    echo "$(date '+%Y-%m-%dT%H:%M:%S') $*" >> "$STATUS"
}

mark_logs() {
    marker="$1"
    echo "===$marker===" >> "$BRIDGE_LOG"
    echo "M $(date '+%H:%M:%S') $marker" >> "$RAW_LOG"
}

write_cmd() {
    target="$1"
    hex="$2"
    echo "$target $hex" > "$WRITE_FILE"
}

case_has_disconnect() {
    marker="$1"
    tail -n 180 "$BRIDGE_LOG" 2>/dev/null |
        sed -n "/===$marker===/,\$p" |
        grep -Eq "connection state status=.* newState=0|BLE write skipped, no current GATT|BLE write skipped, main service missing"
}

make_cmd() {
    byte_index="$1"
    value_hex="$2"
    out=""
    i=0
    for byte in 0a 91 01 02 00 08 00 00 "$PRESET_HEX" 00 00 00 00 00 00 00; do
        if [ "$i" -eq "$byte_index" ]; then
            out="${out}${value_hex}"
        else
            out="${out}${byte}"
        fi
        i=$((i + 1))
    done
    echo "$out"
}

cat > "$EVENTS" <<EOF
case_index	byte_index0	byte_position1	value_hex	tx_hex	marker	started_at
EOF

log_status "waiting for bridge post-init marker wait=${WAIT_READY_SECONDS}s poll=${READY_POLL_SECONDS}s preset=${PRESET_HEX}"
start_epoch="$(date +%s)"
while ! grep -q "post-init notification setup complete" "$BRIDGE_LOG" 2>/dev/null; do
    now_epoch="$(date +%s)"
    elapsed=$((now_epoch - start_epoch))
    if [ "$elapsed" -ge "$WAIT_READY_SECONDS" ]; then
        log_status "timeout waiting for bridge readiness"
        exit 2
    fi
    sleep "$READY_POLL_SECONDS"
done

log_status "bridge ready; starting active preset payload sweep"
case_index=0
for byte_index in 9 10 11 12 13 14 15; do
    value=0
    while [ "$value" -le "$VALUE_END" ]; do
        value_hex="$(printf '%02x' "$value")"
        tx_hex="$(make_cmd "$byte_index" "$value_hex")"
        case_index=$((case_index + 1))
        marker="LOCAL_PAYLOAD_CASE_$(printf '%05d' "$case_index")_B$(printf '%02d' "$byte_index")_V${value_hex}_$(date +%s)"
        started_at="$(date '+%Y-%m-%dT%H:%M:%S')"

        echo "$case_index	$byte_index	$((byte_index + 1))	$value_hex	$tx_hex	$marker	$started_at" >> "$EVENTS"
        mark_logs "$marker"
        write_cmd "cmd" "$STOP_HEX"
        sleep 0.5
        write_cmd "cmd" "$tx_hex"
        sleep 2
        write_cmd "cmd" "$STOP_HEX"
        sleep 0.5
        if case_has_disconnect "$marker"; then
            log_status "disconnect warning after case=$case_index byte=$byte_index value=$value_hex marker=$marker"
            exit 3
        fi

        value=$((value + 1))
    done
done

write_cmd "cmd" "$STOP_HEX"
log_status "done cases=$case_index"
