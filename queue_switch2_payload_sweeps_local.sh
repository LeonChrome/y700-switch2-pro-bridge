#!/system/bin/sh
set -u

ROOT="/data/local/tmp"
SWEEP_SCRIPT="$ROOT/sweep_switch2_preset_payload_local.sh"
QUEUE_LOG="$ROOT/switch2_payload_sweep_queue.log"
WAIT_READY_SECONDS="${1:-14400}"
READY_POLL_SECONDS="${2:-30}"
VALUE_END="${3:-145}"
PRESETS="${4:-02 03 04 05 06 07}"

log_queue() {
    echo "$(date '+%Y-%m-%dT%H:%M:%S') $*" >> "$QUEUE_LOG"
}

wait_for_existing_sweep() {
    while ps -A -o ARGS 2>/dev/null |
        grep "sh $SWEEP_SCRIPT" |
        grep -v "grep" >/dev/null 2>&1; do
        log_queue "waiting for existing sweep process to finish"
        sleep 60
    done
}

log_queue "queue start wait=${WAIT_READY_SECONDS}s poll=${READY_POLL_SECONDS}s value_end=${VALUE_END} presets=${PRESETS}"
wait_for_existing_sweep

for preset in $PRESETS; do
    log_queue "starting preset=${preset}"
    sh "$SWEEP_SCRIPT" "$WAIT_READY_SECONDS" "$READY_POLL_SECONDS" "$VALUE_END" "$preset"
    rc="$?"
    log_queue "finished preset=${preset} rc=${rc}"
    if [ "$rc" -eq 3 ]; then
        log_queue "stop-loss disconnect detected; pausing before next preset"
        sleep 120
    fi
done

log_queue "queue done"
