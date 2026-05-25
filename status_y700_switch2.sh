#!/system/bin/sh
set -u

G=/config/usb_gadget/g1

echo "id:"
id
echo

echo "usb props:"
getprop sys.usb.config
getprop persist.sys.usb.config
echo

echo "UDC:"
cat "$G/UDC" 2>/dev/null || true
echo

echo "config links:"
ls -l "$G/configs/b.1" 2>/dev/null || true
echo

echo "functions:"
ls -l "$G/functions" 2>/dev/null | head -n 80 || true
echo

echo "nodes:"
ls -l /dev/hidg* 2>/dev/null || true
ls -l /dev/usb-ffs/switch2 2>/dev/null || true
echo

echo "functionfs mounts:"
mount | grep functionfs || true
echo

echo "processes:"
ps -A | grep -E 'Switch2FfsResponder|Switch2BleBridge|app_process' || true
