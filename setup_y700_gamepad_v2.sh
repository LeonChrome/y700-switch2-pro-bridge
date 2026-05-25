#!/system/bin/sh
#
# Configure Lenovo Y700 2025 /config USB gadget g1 as a simple USB HID gamepad.
#
# Report format, 8 bytes, no Report ID:
#   byte 0-1: 16 buttons, little-endian bitfield
#   byte 2  : hat switch, 0=up, 2=right, 4=down, 6=left, 8=neutral/null
#   byte 3  : X axis, signed 8-bit, -127..127, 0=center
#   byte 4  : Y axis, signed 8-bit, -127..127, 0=center
#   byte 5  : Z axis, signed 8-bit, -127..127, 0=center
#   byte 6  : Rz axis, signed 8-bit, -127..127, 0=center
#   byte 7  : reserved constant byte

set -e

GADGET="${GADGET:-/config/usb_gadget/g1}"
CONFIG="${CONFIG:-b.1}"
FUNCTION="${FUNCTION:-hid.usb0}"
UDC_NAME="${UDC_NAME:-a600000.dwc3}"
REPORT_LENGTH=8

die() {
    echo "ERROR: $*" >&2
    exit 1
}

need_root() {
    uid="$(id -u 2>/dev/null || true)"
    [ "$uid" = "0" ] || die "Run this script as root, for example: su -c sh /data/local/tmp/setup_y700_gamepad_v2.sh"
}

require_path() {
    [ -e "$1" ] || die "Missing required path: $1"
}

write_report_desc() {
    # Generic Desktop / Game Pad top-level collection.
    # 16 buttons + 8-bit hat + 4 signed 8-bit axes + 1 reserved byte = 8 bytes.
    printf \
'\x05\x01\x09\x05\xa1\x01'\
'\x05\x09\x19\x01\x29\x10\x15\x00\x25\x01\x75\x01\x95\x10\x81\x02'\
'\x05\x01\x09\x39\x15\x00\x25\x07\x35\x00\x46\x3b\x01\x65\x14\x75\x08\x95\x01\x81\x42\x65\x00'\
'\x09\x30\x09\x31\x09\x32\x09\x35\x15\x81\x25\x7f\x75\x08\x95\x04\x81\x02'\
'\x75\x08\x95\x01\x81\x03'\
'\xc0' > "$1"
}

main() {
    need_root

    require_path "$GADGET"
    require_path "$GADGET/configs/$CONFIG"
    require_path "/sys/class/udc/$UDC_NAME"

    func_path="$GADGET/functions/$FUNCTION"
    link_path="$GADGET/configs/$CONFIG/$FUNCTION"

    old_udc="$(cat "$GADGET/UDC" 2>/dev/null || true)"
    echo "Using gadget: $GADGET"
    echo "Using config : $CONFIG"
    echo "Using UDC    : $UDC_NAME"
    [ -n "$old_udc" ] && echo "Currently bound UDC: $old_udc"

    echo "Unbinding gadget..."
    echo "" > "$GADGET/UDC"

    echo "Removing old HID function link/function if present..."
    if [ -L "$link_path" ] || [ -e "$link_path" ]; then
        rm -f "$link_path"
    fi
    rmdir "$func_path" 2>/dev/null || true

    echo "Creating HID function..."
    mkdir -p "$func_path"
    echo 0 > "$func_path/protocol"
    echo 0 > "$func_path/subclass"
    echo "$REPORT_LENGTH" > "$func_path/report_length"
    write_report_desc "$func_path/report_desc"

    echo "Linking HID function into config..."
    ln -s "$func_path" "$link_path"

    echo "Rebinding gadget..."
    echo "$UDC_NAME" > "$GADGET/UDC"

    udc_state_path="/sys/class/udc/$UDC_NAME/state"
    if [ -e "$udc_state_path" ]; then
        udc_state="$(cat "$udc_state_path" 2>/dev/null || true)"
        echo "UDC state: $udc_state"
        if [ "$udc_state" != "configured" ]; then
            echo "WARNING: UDC is not configured yet. /dev/hidg0 writes will fail until the USB host enumerates this gadget."
        fi
    fi

    echo "Waiting for /dev/hidg0..."
    i=0
    while [ "$i" -lt 20 ]; do
        [ -e /dev/hidg0 ] && break
        sleep 0.1
        i=$((i + 1))
    done

    ls -l /dev/hidg* 2>/dev/null || die "/dev/hidg0 was not created"

    echo
    echo "Done. Neutral report:"
    printf '%s\n' "  printf '\\x00\\x00\\x08\\x00\\x00\\x00\\x00\\x00' > /dev/hidg0"
    echo
    echo "Button 1 test:"
    printf '%s\n' "  printf '\\x01\\x00\\x08\\x00\\x00\\x00\\x00\\x00' > /dev/hidg0"
    echo "  sleep 0.2"
    printf '%s\n' "  printf '\\x00\\x00\\x08\\x00\\x00\\x00\\x00\\x00' > /dev/hidg0"
    echo
    echo "Run the full local test with:"
    echo "  sh /data/local/tmp/test_y700_gamepad_reports.sh"
}

main "$@"
