#!/system/bin/sh
set -e

RUN_LOG="${RUN_LOG:-/data/local/tmp/setup_y700_switch2_proto_v3_run.log}"
SETUP="${SETUP:-/data/local/tmp/setup_y700_switch2_proto_v3.sh}"

rm -f "$RUN_LOG"
setsid sh -c "sh '$SETUP' >'$RUN_LOG' 2>&1" >/dev/null 2>&1 &
echo "detached v3 setup started: $RUN_LOG"
