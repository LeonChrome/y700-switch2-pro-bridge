#!/system/bin/sh
set -e

GADGET="${GADGET:-/config/usb_gadget/g1}"
UDC_NAME="${UDC_NAME:-a600000.dwc3}"
LOG="${LOG:-/data/local/tmp/kick_y700_usb_rebind.log}"

{
    date
    echo "unbinding $GADGET"
    echo "" > "$GADGET/UDC" 2>/dev/null || true
    sleep 2
    echo "binding $UDC_NAME"
    echo "$UDC_NAME" > "$GADGET/UDC"
    sleep 1
    echo "UDC=$(cat "$GADGET/UDC" 2>/dev/null || true)"
    ls -l /dev/hidg* 2>/dev/null || true
    ls -l /dev/usb-ffs/switch2 2>/dev/null || true
} > "$LOG" 2>&1
