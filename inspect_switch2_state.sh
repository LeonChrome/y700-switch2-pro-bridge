#!/system/bin/sh

echo "== id =="
id

echo
echo "== gadget =="
echo "UDC: $(cat /config/usb_gadget/g1/UDC 2>/dev/null || true)"
echo "UDC state: $(cat /sys/class/udc/a600000.dwc3/state 2>/dev/null || true)"
ls -l /config/usb_gadget/g1/configs/b.1 2>/dev/null || true

echo
echo "== functionfs =="
grep switch2 /proc/mounts || true
ls -l /dev/usb-ffs/switch2 2>/dev/null || true
ls -l /config/usb_gadget/g1/functions/ffs.switch2 2>/dev/null || true

echo
echo "== hid =="
ls -l /config/usb_gadget/g1/functions/hid.usb0 2>/dev/null || true
ls -l /dev/hidg* 2>/dev/null || true

echo
echo "== responder log =="
cat /data/local/tmp/switch2_ffs_responder.log 2>/dev/null || true

echo
echo "== processes =="
ps -A -o PID,PPID,USER,ARGS 2>/dev/null \
    | grep -E 'Switch2FfsResponder|app_process|setup_y700' \
    | grep -v grep || true

echo
echo "== dmesg filtered =="
dmesg 2>/dev/null \
    | grep -i -E 'killed process|lowmemory|oom|app_process|functionfs|hidg|configfs|dwc3|failed to start|read descriptors|switch2|ffs' \
    | tail -220 || true

echo
echo "== logcat filtered =="
logcat -d -t 500 2>/dev/null \
    | grep -i -E 'Switch2FfsResponder|app_process|Killed|Fatal|AndroidRuntime|functionfs|hidg' || true
