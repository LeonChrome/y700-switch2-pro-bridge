#!/system/bin/sh
set -u

JAR=/data/local/tmp/switch2_ffs_responder.jar
LOG=/data/local/tmp/switch2_ffs_responder.log
READY=/data/local/tmp/switch2_ffs_ready
FFS=/dev/usb-ffs/switch2
HID=/dev/hidg0

pids="$(
  ps -A -o PID,ARGS 2>/dev/null |
    awk '/Switch2FfsResponder/ && !/awk/ { print $1 }'
)"

for pid in $pids; do
  kill "$pid" 2>/dev/null || true
done

sleep 0.3
rm -f "$READY" "$LOG"

setsid sh -c "CLASSPATH=$JAR app_process64 /system/bin Switch2FfsResponder $FFS $HID >>$LOG 2>&1" >/dev/null 2>&1 &

i=0
while [ "$i" -lt 50 ]; do
  [ -e "$READY" ] && break
  sleep 0.1
  i=$((i + 1))
done

tail -n 80 "$LOG" 2>/dev/null || true
