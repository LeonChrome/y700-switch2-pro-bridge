#!/system/bin/sh
set -u

G=/config/usb_gadget/g1
UDC=a600000.dwc3

echo "killing old responder"
pids="$(ps -A -o PID,ARGS 2>/dev/null | grep Switch2FfsResponder | grep -v grep | awk '{print $1}' || true)"
for pid in $pids; do
  kill "$pid" 2>/dev/null || true
done
sleep 0.5

echo "binding gadget"
echo "$UDC" > "$G/UDC" 2>/dev/null || true
sleep 1

echo "UDC=$(cat "$G/UDC" 2>/dev/null || true)"
ls -l /dev/hidg* 2>/dev/null || true
ls -l /dev/usb-ffs/switch2 2>/dev/null || true
ps -A | grep -E 'Switch2FfsResponder|app_process' || true
