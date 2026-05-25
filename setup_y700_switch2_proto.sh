#!/system/bin/sh
#
# Experimental Nintendo Switch Pro-compatible USB protocol shape:
#   interface 0: 64-byte vendor HID state endpoint
#   interface 1: FunctionFS vendor bulk IN/OUT endpoint pair
#
# This is intentionally experimental. It removes USB ADB from the current
# config, so keep wireless ADB connected.

set -e

GADGET="${GADGET:-/config/usb_gadget/g1}"
CONFIG="${CONFIG:-b.1}"
UDC_NAME="${UDC_NAME:-a600000.dwc3}"
FFS_NAME="${FFS_NAME:-switch2}"
FFS_DIR="${FFS_DIR:-/dev/usb-ffs/$FFS_NAME}"
RESPONDER_JAR="${RESPONDER_JAR:-/data/local/tmp/switch2_ffs_responder.jar}"
READY="${READY:-/data/local/tmp/switch2_ffs_ready}"
LOG="${LOG:-/data/local/tmp/switch2_ffs_responder.log}"

die() {
    echo "ERROR: $*" >&2
    exit 1
}

need_root() {
    uid="$(id -u 2>/dev/null || true)"
    [ "$uid" = "0" ] || die "Run as root"
}

kill_old_responder() {
    echo "Stopping old responder..."
    pids="$(ps -A -o PID,ARGS 2>/dev/null | grep Switch2FfsResponder | grep -v grep | awk '{print $1}' || true)"
    for pid in $pids; do
        kill "$pid" 2>/dev/null || true
    done
    sleep 0.2
}

unlink_config() {
    rm -f "$GADGET/configs/$CONFIG/function0" \
          "$GADGET/configs/$CONFIG/function1" \
          "$GADGET/configs/$CONFIG/function2" \
          "$GADGET/configs/$CONFIG/hid.usb0" \
          "$GADGET/configs/$CONFIG/switch2" \
          "$GADGET/configs/$CONFIG/ffs.switch2" \
          "$GADGET/configs/$CONFIG/ffs.adb"
}

write_hid64_desc() {
    # Keep a vendor/raw HID shape so Steam selects its Nintendo HIDAPI driver,
    # but declare the same report IDs that the userspace responder uses:
    #   input  report 0x09 + 63 payload bytes = 64 bytes on the wire
    #   output report 0x02 + 63 payload bytes = 64 bytes from Steam
    printf '\x06\x00\xff\x09\x01\xa1\x01\x15\x00\x26\xff\x00\x75\x08\x85\x09\x95\x3f\x09\x01\x81\x02\x85\x02\x95\x3f\x09\x01\x91\x02\xc0' > "$1"
}

setup_hid64() {
    echo "Setting HID function..."
    func="$GADGET/functions/hid.usb0"
    rmdir "$func" 2>/dev/null || true
    mkdir -p "$func"
    echo 0 > "$func/protocol"
    echo 0 > "$func/subclass"
    echo 64 > "$func/report_length"
    write_hid64_desc "$func/report_desc"
}

setup_identity() {
    echo "Setting USB identity..."
    echo 0x057e > "$GADGET/idVendor"
    echo 0x2069 > "$GADGET/idProduct"
    echo 0x0104 > "$GADGET/bcdDevice"
    echo 0x0200 > "$GADGET/bcdUSB"
    echo "Nintendo Co., Ltd." > "$GADGET/strings/0x409/manufacturer"
    echo "Nintendo Switch Pro Controller" > "$GADGET/strings/0x409/product"
    echo "Nintendo Switch Pro Controller" > "$GADGET/configs/$CONFIG/strings/0x409/configuration"

    echo 1 > "$GADGET/os_desc/use" 2>/dev/null || true
    echo 0xcd > "$GADGET/os_desc/b_vendor_code" 2>/dev/null || true
    echo "MSFT100" > "$GADGET/os_desc/qw_sign" 2>/dev/null || true
}

setup_functionfs() {
    echo "Setting FunctionFS..."
    mkdir -p "$FFS_DIR"
    mkdir -p "$GADGET/functions/ffs.$FFS_NAME"

    if mount | grep -q " on $FFS_DIR type functionfs "; then
        echo "FunctionFS already mounted at $FFS_DIR; reusing existing mount."
        return 0
    fi
    toybox mount -t functionfs -o uid=0,gid=0,mode=0777 "$FFS_NAME" "$FFS_DIR"
}

start_responder() {
    echo "Starting responder..."
    [ -f "$RESPONDER_JAR" ] || die "Missing responder jar: $RESPONDER_JAR"
    rm -f "$READY" "$LOG"
    setsid sh -c "CLASSPATH=$RESPONDER_JAR app_process64 /system/bin Switch2FfsResponder $FFS_DIR /dev/hidg0 >>$LOG 2>&1" >/dev/null 2>&1 &

    i=0
    while [ "$i" -lt 50 ]; do
        [ -e "$READY" ] && return 0
        sleep 0.1
        i=$((i + 1))
    done

    tail -80 "$LOG" 2>/dev/null || true
    die "Responder did not become ready"
}

link_config() {
    echo "Linking config..."
    unlink_config
    ln -s "$GADGET/functions/hid.usb0" "$GADGET/configs/$CONFIG/function0"
    ln -s "$GADGET/functions/ffs.$FFS_NAME" "$GADGET/configs/$CONFIG/function1"
}

main() {
    need_root
    [ -d "$GADGET" ] || die "Missing gadget: $GADGET"
    [ -e "/sys/class/udc/$UDC_NAME" ] || die "Missing UDC: $UDC_NAME"

    kill_old_responder
    echo "Unbinding gadget..."
    echo "" > "$GADGET/UDC" 2>/dev/null || true

    unlink_config
    setup_identity
    setup_hid64
    setup_functionfs
    start_responder
    link_config

    echo "Binding gadget..."
    echo "$UDC_NAME" > "$GADGET/UDC"

    echo "Current config links:"
    ls -l "$GADGET/configs/$CONFIG"
    echo
    echo "Responder log:"
    tail -40 "$LOG" 2>/dev/null || true
    echo
    echo "UDC state: $(cat /sys/class/udc/$UDC_NAME/state 2>/dev/null || true)"
    echo "HID node:"
    ls -l /dev/hidg* 2>/dev/null || true
    echo "FunctionFS nodes:"
    ls -l "$FFS_DIR" 2>/dev/null || true
}

main "$@"
