#!/system/bin/sh
#
# Change only the USB gadget identity for Steam/Nintendo experiments.
# This keeps the existing HID gamepad descriptor/report format from
# setup_y700_gamepad_v2.sh, so it is intentionally only a VID/PID/name test.
#
# Usage:
#   MODE=switch2 sh setup_y700_switch_identity_experiment.sh
#   MODE=switchpro sh setup_y700_switch_identity_experiment.sh
#   MODE=restore sh setup_y700_switch_identity_experiment.sh

set -e

GADGET="${GADGET:-/config/usb_gadget/g1}"
UDC_NAME="${UDC_NAME:-a600000.dwc3}"
MODE="${MODE:-switch2}"
BACKUP_DIR="${BACKUP_DIR:-/data/local/tmp/y700_usb_identity_backup}"

die() {
    echo "ERROR: $*" >&2
    exit 1
}

need_root() {
    uid="$(id -u 2>/dev/null || true)"
    [ "$uid" = "0" ] || die "Run as root, for example: su -c sh /data/local/tmp/setup_y700_switch_identity_experiment.sh"
}

read_or_empty() {
    cat "$1" 2>/dev/null || true
}

write_value() {
    path="$1"
    value="$2"
    printf '%s' "$value" > "$path"
}

backup_once() {
    mkdir -p "$BACKUP_DIR"

    [ -e "$BACKUP_DIR/idVendor" ] || read_or_empty "$GADGET/idVendor" > "$BACKUP_DIR/idVendor"
    [ -e "$BACKUP_DIR/idProduct" ] || read_or_empty "$GADGET/idProduct" > "$BACKUP_DIR/idProduct"
    [ -e "$BACKUP_DIR/bcdDevice" ] || read_or_empty "$GADGET/bcdDevice" > "$BACKUP_DIR/bcdDevice"
    [ -e "$BACKUP_DIR/bcdUSB" ] || read_or_empty "$GADGET/bcdUSB" > "$BACKUP_DIR/bcdUSB"
    [ -e "$BACKUP_DIR/manufacturer" ] || read_or_empty "$GADGET/strings/0x409/manufacturer" > "$BACKUP_DIR/manufacturer"
    [ -e "$BACKUP_DIR/product" ] || read_or_empty "$GADGET/strings/0x409/product" > "$BACKUP_DIR/product"
    [ -e "$BACKUP_DIR/serialnumber" ] || read_or_empty "$GADGET/strings/0x409/serialnumber" > "$BACKUP_DIR/serialnumber"
    [ -e "$BACKUP_DIR/configuration" ] || read_or_empty "$GADGET/configs/b.1/strings/0x409/configuration" > "$BACKUP_DIR/configuration"
}

restore_identity() {
    [ -d "$BACKUP_DIR" ] || die "No backup found at $BACKUP_DIR"

    write_value "$GADGET/idVendor" "$(cat "$BACKUP_DIR/idVendor")"
    write_value "$GADGET/idProduct" "$(cat "$BACKUP_DIR/idProduct")"
    write_value "$GADGET/bcdDevice" "$(cat "$BACKUP_DIR/bcdDevice")"
    write_value "$GADGET/bcdUSB" "$(cat "$BACKUP_DIR/bcdUSB")"
    write_value "$GADGET/strings/0x409/manufacturer" "$(cat "$BACKUP_DIR/manufacturer")"
    write_value "$GADGET/strings/0x409/product" "$(cat "$BACKUP_DIR/product")"
    write_value "$GADGET/strings/0x409/serialnumber" "$(cat "$BACKUP_DIR/serialnumber")"
    write_value "$GADGET/configs/b.1/strings/0x409/configuration" "$(cat "$BACKUP_DIR/configuration")"
}

apply_switch2_identity() {
    write_value "$GADGET/idVendor" "0x057e"
    write_value "$GADGET/idProduct" "0x2069"
    write_value "$GADGET/bcdDevice" "0x0101"
    write_value "$GADGET/bcdUSB" "0x0200"
    write_value "$GADGET/strings/0x409/manufacturer" "Nintendo Co., Ltd."
    write_value "$GADGET/strings/0x409/product" "Nintendo Switch 2 Pro Controller"
    write_value "$GADGET/configs/b.1/strings/0x409/configuration" "Nintendo Switch 2 Pro Controller"
}

apply_switchpro_identity() {
    write_value "$GADGET/idVendor" "0x057e"
    write_value "$GADGET/idProduct" "0x2009"
    write_value "$GADGET/bcdDevice" "0x0110"
    write_value "$GADGET/bcdUSB" "0x0200"
    write_value "$GADGET/strings/0x409/manufacturer" "Nintendo Co., Ltd."
    write_value "$GADGET/strings/0x409/product" "Pro Controller"
    write_value "$GADGET/configs/b.1/strings/0x409/configuration" "Pro Controller"
}

print_identity() {
    echo "Current USB identity:"
    echo "  idVendor     = $(read_or_empty "$GADGET/idVendor")"
    echo "  idProduct    = $(read_or_empty "$GADGET/idProduct")"
    echo "  bcdDevice    = $(read_or_empty "$GADGET/bcdDevice")"
    echo "  bcdUSB       = $(read_or_empty "$GADGET/bcdUSB")"
    echo "  manufacturer = $(read_or_empty "$GADGET/strings/0x409/manufacturer")"
    echo "  product      = $(read_or_empty "$GADGET/strings/0x409/product")"
    echo "  serialnumber = $(read_or_empty "$GADGET/strings/0x409/serialnumber")"
}

main() {
    need_root
    [ -d "$GADGET" ] || die "Missing gadget path: $GADGET"
    [ -e "/sys/class/udc/$UDC_NAME" ] || die "Missing UDC: $UDC_NAME"

    echo "Mode: $MODE"
    echo "Gadget: $GADGET"
    echo "Backup: $BACKUP_DIR"
    backup_once

    echo "Unbinding gadget..."
    echo "" > "$GADGET/UDC"

    case "$MODE" in
        switch2)
            apply_switch2_identity
            ;;
        switchpro|switch1|pro)
            apply_switchpro_identity
            ;;
        restore)
            restore_identity
            ;;
        *)
            die "Unknown MODE=$MODE. Use switch2, switchpro, or restore."
            ;;
    esac

    echo "Rebinding gadget..."
    echo "$UDC_NAME" > "$GADGET/UDC"

    print_identity

    state_path="/sys/class/udc/$UDC_NAME/state"
    if [ -e "$state_path" ]; then
        echo "UDC state: $(cat "$state_path" 2>/dev/null || true)"
    fi

    echo
    echo "If Windows/Steam still shows the old name, unplug/replug USB or remove the old device instance in Device Manager."
}

main "$@"
