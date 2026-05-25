import java.io.ByteArrayOutputStream;
import java.io.Closeable;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.RandomAccessFile;
import java.text.SimpleDateFormat;
import java.util.Arrays;
import java.util.Date;
import java.util.Locale;

public class Switch2FfsResponder {
    private static final int FUNCTIONFS_DESCRIPTORS_MAGIC_V2 = 3;
    private static final int FUNCTIONFS_STRINGS_MAGIC = 2;
    private static final int FUNCTIONFS_HAS_FS_DESC = 1;
    private static final int FUNCTIONFS_HAS_HS_DESC = 2;
    private static final int FUNCTIONFS_HAS_SS_DESC = 4;
    private static final int FUNCTIONFS_HAS_MS_OS_DESC = 8;

    private static final int USB_DT_INTERFACE = 4;
    private static final int USB_DT_ENDPOINT = 5;
    private static final int USB_DT_SS_ENDPOINT_COMP = 48;
    private static final int USB_CLASS_VENDOR_SPEC = 0xff;
    private static final int USB_ENDPOINT_XFER_BULK = 2;

    private static final File LOG_FILE = new File("/data/local/tmp/switch2_ffs_responder.log");
    private static final File HID_OUTPUT_LOG_FILE = new File("/data/local/tmp/switch2_hid_output.log");
    private static final File HID_LAST_OUTPUT_FILE = new File("/data/local/tmp/switch2_last_hid_output.txt");
    private static final File STATE_FILE = new File("/data/local/tmp/switch2_state.txt");
    private static final File BLE_WRITE_FILE = new File("/data/local/tmp/switch2_ble_write.txt");
    private static final File LED_RUMBLE_FLAG_FILE = new File("/data/local/tmp/switch2_bridge_led_rumble");
    private static final File RICH_HAPTIC_CYCLE_FLAG_FILE = new File("/data/local/tmp/switch2_haptic_cycle_rich");
    private static final File HAPTIC_LOG_ONLY_FLAG_FILE = new File("/data/local/tmp/switch2_haptic_log_only");
    private static final int PRESET_STOP = 0;
    private static final int PRESET_SHORT_LOW = 1;
    private static final int PRESET_SHORT_MID = 2;
    private static final int PRESET_NOTIFY = 4;
    private static final int PRESET_DOUBLE = 5;
    private static final int PRESET_LONG = 6;
    private static final int PRESET_NOTIFY_HIGH = 7;
    private static final long DOUBLE_PULSE_WINDOW_MS = 260;
    private static final long LONG_RUMBLE_THRESHOLD_MS = 360;
    private static final long RICH_RUMBLE_REPEAT_MS = 700;
    private static final long GAME_RUMBLE_REPEAT_MS = 800;
    private static final int[] HID_SHORT_PRESET_SEQUENCE = new int[] { PRESET_SHORT_LOW, PRESET_SHORT_MID };
    private static final int[] HID_GAME_SUSTAIN_PRESET_SEQUENCE = new int[] { PRESET_DOUBLE, PRESET_LONG };
    private static final int[] HID_RICH_PRESET_SEQUENCE = new int[] {
            PRESET_SHORT_LOW, PRESET_SHORT_MID, PRESET_DOUBLE, PRESET_NOTIFY, PRESET_LONG, PRESET_NOTIFY_HIGH
    };
    private static volatile boolean running = true;

    private static FileOutputStream bulkIn;
    private static long stateMtime;
    private static int stateB5;
    private static int stateB6;
    private static int stateB7;
    private static int stateB8;
    private static int stateLx = 2048;
    private static int stateLy = 2048;
    private static int stateRx = 2048;
    private static int stateRy = 2048;
    private static int loggedB5 = -1;
    private static int loggedB6 = -1;
    private static int loggedB7 = -1;
    private static int loggedB8 = -1;
    private static int loggedLx = -1;
    private static int loggedLy = -1;
    private static int loggedRx = -1;
    private static int loggedRy = -1;
    private static long lastStateLogMs;
    private static long lastBulkLedBridgeMs;
    private static int inputSeq;

    public static void main(String[] args) throws Exception {
        try {
            run(args);
        } catch (Throwable t) {
            log("fatal: " + t);
            StackTraceElement[] trace = t.getStackTrace();
            for (int i = 0; i < trace.length && i < 12; i++) {
                log("  at " + trace[i]);
            }
            throw t;
        }
    }

    private static void run(String[] args) throws Exception {
        String ffsDir = args.length > 0 ? args[0] : "/dev/usb-ffs/switch2";
        String hidPath = args.length > 1 ? args[1] : "/dev/hidg0";
        File dir = new File(ffsDir);

        log("Switch2FfsResponder starting, ffsDir=" + ffsDir + ", hidPath=" + hidPath);
        deleteIfExists(HID_OUTPUT_LOG_FILE);
        deleteIfExists(HID_LAST_OUTPUT_FILE);
        RandomAccessFile ep0 = openEp0AndWriteDescriptors(new File(dir, "ep0"));
        touch("/data/local/tmp/switch2_ffs_ready");

        Thread ep0Thread = new Thread(new Ep0EventLoop(ep0), "ep0-events");
        ep0Thread.setDaemon(true);
        ep0Thread.start();

        Thread hidThread = new Thread(new HidNeutralLoop(new File(hidPath)), "hid-neutral");
        hidThread.setDaemon(true);
        hidThread.start();

        Thread hidOutputThread = new Thread(new HidOutputLoop(new File(hidPath)), "hid-output");
        hidOutputThread.setDaemon(true);
        hidOutputThread.start();

        while (running) {
            File ep1 = new File(dir, "ep1"); // IN to host
            File ep2 = new File(dir, "ep2"); // OUT from host
            FileOutputStream localBulkIn = null;
            FileInputStream bulkOut = null;
            try {
                waitFor(ep1);
                waitFor(ep2);

                log("Opening FunctionFS bulk endpoints");
                localBulkIn = new FileOutputStream(ep1);
                bulkIn = localBulkIn;
                bulkOut = new FileInputStream(ep2);

                byte[] buf = new byte[512];
                while (running) {
                    int n = bulkOut.read(buf);
                    if (n < 0) {
                        log("bulk OUT EOF");
                        break;
                    }
                    if (n == 0) {
                        continue;
                    }
                    byte[] cmd = Arrays.copyOf(buf, n);
                    log("bulk OUT " + n + " bytes: " + hex(cmd, Math.min(n, 64)));
                    bridgeBulkOutputToBle(cmd);
                    byte[] reply = buildReply(cmd);
                    if (reply != null && reply.length > 0) {
                        bulkIn.write(reply);
                        bulkIn.flush();
                        log("bulk IN  " + reply.length + " bytes: " + hex(reply, Math.min(reply.length, 64)));
                    }
                }
            } catch (Throwable t) {
                log("bulk loop: " + t);
                sleep(500);
            } finally {
                closeQuietly(bulkOut);
                closeQuietly(localBulkIn);
                bulkIn = null;
            }
        }
    }

    private static RandomAccessFile openEp0AndWriteDescriptors(File ep0File) throws IOException {
        waitFor(ep0File);
        RandomAccessFile ep0 = new RandomAccessFile(ep0File, "rw");
        byte[] descriptors = buildDescriptors();
        byte[] strings = buildStrings();
        ep0.write(descriptors);
        log("wrote descriptors: " + descriptors.length + " bytes");
        ep0.write(strings);
        log("wrote strings: " + strings.length + " bytes");
        return ep0;
    }

    private static byte[] buildDescriptors() throws IOException {
        ByteArrayOutputStream body = new ByteArrayOutputStream();
        le32(body, 3); // FS descriptor count
        le32(body, 3); // HS descriptor count
        le32(body, 5); // SS descriptor count
        le32(body, 2); // MS OS descriptor count
        writeInterfaceAndBulkEndpoints(body, 64, false);
        writeInterfaceAndBulkEndpoints(body, 512, false);
        writeInterfaceAndBulkEndpoints(body, 1024, true);
        writeWinUsbCompatOsDescriptor(body);
        writeWinUsbPropertyOsDescriptor(body);

        ByteArrayOutputStream out = new ByteArrayOutputStream();
        le32(out, FUNCTIONFS_DESCRIPTORS_MAGIC_V2);
        le32(out, 12 + body.size());
        le32(out, FUNCTIONFS_HAS_FS_DESC | FUNCTIONFS_HAS_HS_DESC | FUNCTIONFS_HAS_SS_DESC | FUNCTIONFS_HAS_MS_OS_DESC);
        out.write(body.toByteArray());
        return out.toByteArray();
    }

    private static void writeInterfaceAndBulkEndpoints(ByteArrayOutputStream out, int maxPacket, boolean superSpeed)
            throws IOException {
        // Interface descriptor. The kernel assigns bInterfaceNumber inside the final composite config.
        out.write(new byte[] {
                9, USB_DT_INTERFACE,
                0, 0,
                2,
                (byte) USB_CLASS_VENDOR_SPEC, 0, 0,
                1
        });

        writeEndpoint(out, 0x81, maxPacket); // device-to-host bulk IN
        if (superSpeed) {
            writeSsCompanion(out);
        }
        writeEndpoint(out, 0x02, maxPacket); // host-to-device bulk OUT
        if (superSpeed) {
            writeSsCompanion(out);
        }
    }

    private static void writeEndpoint(ByteArrayOutputStream out, int address, int maxPacket) throws IOException {
        out.write(7);
        out.write(USB_DT_ENDPOINT);
        out.write(address);
        out.write(USB_ENDPOINT_XFER_BULK);
        le16(out, maxPacket);
        out.write(0);
    }

    private static void writeSsCompanion(ByteArrayOutputStream out) throws IOException {
        out.write(6);
        out.write(USB_DT_SS_ENDPOINT_COMP);
        out.write(0); // bMaxBurst
        out.write(0); // bmAttributes
        le16(out, 0); // wBytesPerInterval
    }

    private static void writeWinUsbCompatOsDescriptor(ByteArrayOutputStream out) throws IOException {
        out.write(0); // FunctionFS-local interface number; the kernel remaps it in the composite config.
        le32(out, 35); // usb_os_desc_header + one usb_ext_compat_desc
        le16(out, 1); // FunctionFS expects literal version 1 here, not BCD 0x0100.
        le16(out, 4); // Extended Compatibility ID descriptor
        out.write(1); // bCount
        out.write(0); // reserved

        out.write(0); // bFirstInterfaceNumber inside this FunctionFS function
        out.write(1); // reserved
        writeFixedAscii(out, "WINUSB", 8);
        writeFixedAscii(out, "", 8);
        for (int i = 0; i < 6; i++) {
            out.write(0);
        }
    }

    private static void writeWinUsbPropertyOsDescriptor(ByteArrayOutputStream out) throws IOException {
        byte[] name = asciiNul("DeviceInterfaceGUID");
        byte[] data = asciiNul("{6F13725E-EF0E-4FD3-AE5F-B2DE989EC825}");
        int propSize = 4 + 4 + 2 + name.length + 4 + data.length;
        int totalSize = 11 + propSize;

        out.write(0); // FunctionFS-local interface number; the kernel remaps it in the composite config.
        le32(out, totalSize);
        le16(out, 1);
        le16(out, 5); // Extended Properties descriptor
        le16(out, 1); // wCount

        le32(out, propSize);
        le32(out, 1); // REG_SZ
        le16(out, name.length);
        out.write(name);
        le32(out, data.length);
        out.write(data);
    }

    private static void writeFixedAscii(ByteArrayOutputStream out, String value, int width) throws IOException {
        byte[] bytes = value.getBytes("US-ASCII");
        int n = Math.min(bytes.length, width);
        out.write(bytes, 0, n);
        for (int i = n; i < width; i++) {
            out.write(0);
        }
    }

    private static byte[] asciiNul(String value) throws IOException {
        ByteArrayOutputStream out = new ByteArrayOutputStream();
        byte[] bytes = value.getBytes("US-ASCII");
        out.write(bytes);
        out.write(0);
        return out.toByteArray();
    }

    private static byte[] buildStrings() throws IOException {
        byte[] iface = "Nintendo Switch 2 bulk".getBytes("UTF-8");
        ByteArrayOutputStream body = new ByteArrayOutputStream();
        le16(body, 0x0409);
        body.write(iface);
        body.write(0);

        ByteArrayOutputStream out = new ByteArrayOutputStream();
        le32(out, FUNCTIONFS_STRINGS_MAGIC);
        le32(out, 16 + body.size());
        le32(out, 1);
        le32(out, 1);
        out.write(body.toByteArray());
        return out.toByteArray();
    }

    private static byte[] buildReply(byte[] cmd) {
        if (cmd.length == 0) {
            return null;
        }

        int c0 = u(cmd[0]);
        int arg1Lo = cmd.length > 2 ? u(cmd[2]) : 0;
        int arg1Hi = cmd.length > 3 ? u(cmd[3]) : 0;

        if (cmd.length >= 16 && c0 == 0x02) {
            return buildFlashReadReply(cmd);
        }

        if (c0 == 0x0c && arg1Hi == 0x02) {
            return null;
        }

        if (c0 == 0x10) {
            return null;
        }

        if (c0 == 0x03 && arg1Hi == 0x0d) {
            byte[] reply = buildAck(cmd, 12);
            reply[8] = 0x01;
            return reply;
        }

        if (c0 == 0x15 && arg1Hi == 0x01) {
            byte[] reply = buildAck(cmd, 17);
            reply[8] = 0x01;
            reply[9] = 0x04;
            reply[10] = 0x01;
            writeBytes(reply, 11, new byte[] {
                    0x2d, (byte) 0xfc, 0x27, (byte) 0xce, (byte) 0xc6, 0x38
            });
            return reply;
        }

        if (c0 == 0x15 && arg1Hi == 0x02) {
            byte[] reply = buildAck(cmd, 25);
            reply[8] = 0x01;
            return reply;
        }

        if (c0 == 0x15 && arg1Hi == 0x03) {
            byte[] reply = buildAck(cmd, 9);
            reply[8] = 0x01;
            return reply;
        }

        if (c0 == 0x11) {
            byte[] reply = buildAck(cmd, 37);
            reply[8] = 0x01;
            writeBytes(reply, 9, hexBytes("20 03 00 00 0a e8 1c 3b 79 7d 8b 3a 0a e8 9c 42 58 a0 0b 42 0a e8 9c 41 58 a0 0b 41"));
            return reply;
        }

        if (c0 == 0x01 && arg1Hi == 0x0c) {
            byte[] reply = buildAck(cmd, 12);
            writeBytes(reply, 8, hexBytes("61 12 50 10"));
            return reply;
        }

        if (c0 == 0x03 && arg1Hi == 0x01) {
            byte[] reply = buildAck(cmd, 16);
            reply[10] = 0x40;
            reply[11] = (byte) 0xf0;
            reply[14] = 0x60;
            return reply;
        }

        return buildAck(cmd, 8);
    }

    private static byte[] buildAck(byte[] cmd, int length) {
        byte[] reply = new byte[length];
        reply[0] = cmd[0];
        reply[1] = 0x01;
        if (length > 2 && cmd.length > 2) {
            reply[2] = cmd[2];
        }
        if (length > 3 && cmd.length > 3) {
            reply[3] = cmd[3];
        }
        if (length > 4 && cmd.length > 4) {
            reply[4] = cmd[4];
        }
        if (length > 5) {
            reply[5] = (byte) 0xf8;
        }
        return reply;
    }

    private static byte[] buildFlashReadReply(byte[] cmd) {
        int address = u(cmd[12]) | (u(cmd[13]) << 8) | (u(cmd[14]) << 16) | (u(cmd[15]) << 24);
        int dataLength = flashReadLength(address);
        byte[] reply = new byte[0x10 + dataLength];
        byte[] data = new byte[dataLength];

        if (address == 0x13000) {
            byte[] serial = "HA2F83JF".getBytes();
            System.arraycopy(serial, 0, data, 2, serial.length);
        }

        if (address == 0x13080 || address == 0x130C0) {
            Arrays.fill(data, (byte) 0xff);
            byte[] calib = packStickCalibration(2048, 2048, 2048, 2048, 2048, 2048);
            System.arraycopy(calib, 0, data, 0x28, calib.length);
        }

        if (address == 0x1fc040 || address == 0x1fc080 || address == 0x13060) {
            Arrays.fill(data, (byte) 0xff);
        }

        if (address == 0x13040) {
            writeBytes(data, 0, hexBytes("16 f4 d3 41 48 ce 85 ba f1 05 71 ba 1f 27 cb 3b"));
        }

        if (address == 0x13100) {
            writeBytes(data, 0, hexBytes("00 00 00 00 00 00 00 00 00 00 00 00 2d 10 a7 3d e7 49 35 3c a4 2d 20 41"));
        }

        reply[0] = 0x02;
        reply[1] = 0x01;
        reply[2] = cmd[2];
        reply[3] = cmd[3];
        reply[5] = (byte) 0xf8;
        reply[8] = (byte) dataLength;
        System.arraycopy(cmd, 12, reply, 12, 4);
        System.arraycopy(data, 0, reply, 0x10, data.length);
        return reply;
    }

    private static int flashReadLength(int address) {
        if (address == 0x13040) {
            return 0x10;
        }
        if (address == 0x13100) {
            return 0x18;
        }
        if (address == 0x13060) {
            return 0x20;
        }
        return 0x40;
    }

    private static void writeBytes(byte[] out, int offset, byte[] data) {
        int n = Math.min(data.length, out.length - offset);
        if (n > 0) {
            System.arraycopy(data, 0, out, offset, n);
        }
    }

    private static byte[] hexBytes(String text) {
        String[] parts = text.trim().split("\\s+");
        byte[] out = new byte[parts.length];
        for (int i = 0; i < parts.length; i++) {
            out[i] = (byte) Integer.parseInt(parts[i], 16);
        }
        return out;
    }

    private static byte[] packStickCalibration(int xn, int yn, int xmax, int ymax, int xmin, int ymin) {
        byte[] out = new byte[9];
        pack12Pair(out, 0, xn, yn);
        pack12Pair(out, 3, xmax, ymax);
        pack12Pair(out, 6, xmin, ymin);
        return out;
    }

    private static void pack12Pair(byte[] out, int offset, int x, int y) {
        out[offset] = (byte) (x & 0xff);
        out[offset + 1] = (byte) (((x >> 8) & 0x0f) | ((y & 0x0f) << 4));
        out[offset + 2] = (byte) ((y >> 4) & 0xff);
    }

    private static byte[] neutralSwitch2State() {
        loadStateOverride();
        byte[] state = new byte[64];
        state[0] = 0x09; // Switch 2 Pro full input report.
        state[1] = (byte) (inputSeq++ & 0xff);
        state[2] = 0x20; // Switch 2 input status byte seen in BLE notifications.
        // The state file keeps Pro2 BLE button bytes. Steam's Switch 2 USB
        // parser reads the wired state packet several bytes later.
        state[5] = (byte) switch2UsbRightButtons(stateB5);
        state[6] = (byte) switch2UsbSystemButtons(stateB5, stateB6, stateB7);
        state[7] = (byte) switch2UsbLeftButtons(stateB6);
        state[8] = (byte) switch2UsbGripButtons(stateB7);
        pack12Pair(state, 11, stateLx, stateLy);
        pack12Pair(state, 14, stateRx, stateRy);

        long now = System.nanoTime() / 1000L;
        state[0x2b] = (byte) (now & 0xff);
        state[0x2c] = (byte) ((now >> 8) & 0xff);
        state[0x2d] = (byte) ((now >> 16) & 0xff);
        state[0x2e] = (byte) ((now >> 24) & 0xff);
        return state;
    }

    private static int switch2UsbRightButtons(int bleRight) {
        int usb = 0;
        usb |= mapBit(bleRight, 0x04, 0x01); // Y -> West.
        usb |= mapBit(bleRight, 0x08, 0x02); // X -> North.
        usb |= mapBit(bleRight, 0x01, 0x04); // B -> South.
        usb |= mapBit(bleRight, 0x02, 0x08); // A -> East.
        usb |= mapBit(bleRight, 0x10, 0x40); // R.
        usb |= mapBit(bleRight, 0x20, 0x80); // ZR.
        return usb;
    }

    private static int switch2UsbSystemButtons(int bleRight, int bleLeft, int bleSystem) {
        int usb = 0;
        usb |= mapBit(bleLeft, 0x40, 0x01); // Minus.
        usb |= mapBit(bleRight, 0x40, 0x02); // Plus.
        usb |= mapBit(bleRight, 0x80, 0x04); // Right stick.
        usb |= mapBit(bleLeft, 0x80, 0x08); // Left stick.
        usb |= mapBit(bleSystem, 0x01, 0x10); // Home.
        usb |= mapBit(bleSystem, 0x02, 0x20); // Capture.
        usb |= mapBit(bleSystem, 0x10, 0x40); // C.
        return usb;
    }

    private static int switch2UsbLeftButtons(int bleLeft) {
        int usb = 0;
        usb |= mapBit(bleLeft, 0x01, 0x01); // Down.
        usb |= mapBit(bleLeft, 0x08, 0x02); // Up.
        usb |= mapBit(bleLeft, 0x02, 0x04); // Right.
        usb |= mapBit(bleLeft, 0x04, 0x08); // Left.
        usb |= mapBit(bleLeft, 0x10, 0x40); // L.
        usb |= mapBit(bleLeft, 0x20, 0x80); // ZL.
        return usb;
    }

    private static int switch2UsbGripButtons(int bleSystem) {
        int usb = 0;
        usb |= mapBit(bleSystem, 0x04, 0x01); // GR.
        usb |= mapBit(bleSystem, 0x08, 0x02); // GL.
        return usb;
    }

    private static int mapBit(int value, int sourceMask, int targetMask) {
        return (value & sourceMask) != 0 ? targetMask : 0;
    }

    private static int parseStateValue(String key, String text) {
        if ("b5".equals(key) || "b6".equals(key) || "b7".equals(key) || "b8".equals(key)) {
            String trimmed = text.trim();
            if (trimmed.startsWith("0x") || trimmed.startsWith("0X")) {
                return parseFlexibleInt(trimmed);
            }
            return Integer.parseInt(trimmed, 16);
        }
        return parseFlexibleInt(text);
    }

    private static void loadStateOverride() {
        try {
            long mtime = STATE_FILE.exists() ? STATE_FILE.lastModified() : 0;
            if (mtime == stateMtime) {
                return;
            }
            stateMtime = mtime;
            stateB5 = 0;
            stateB6 = 0;
            stateB7 = 0;
            stateB8 = 0;
            stateLx = 2048;
            stateLy = 2048;
            stateRx = 2048;
            stateRy = 2048;
            if (mtime == 0) {
                return;
            }

            FileInputStream in = new FileInputStream(STATE_FILE);
            byte[] buf = new byte[(int) Math.min(STATE_FILE.length(), 1024)];
            int n = in.read(buf);
            in.close();
            if (n <= 0) {
                return;
            }

            String[] tokens = new String(buf, 0, n, "US-ASCII").trim().split("\\s+");
            for (String token : tokens) {
                int eq = token.indexOf('=');
                if (eq <= 0) {
                    continue;
                }
                String key = token.substring(0, eq);
                int value = parseStateValue(key, token.substring(eq + 1));
                if ("b5".equals(key)) {
                    stateB5 = value & 0xff;
                } else if ("b6".equals(key)) {
                    stateB6 = value & 0xff;
                } else if ("b7".equals(key)) {
                    stateB7 = value & 0xff;
                } else if ("b8".equals(key)) {
                    stateB8 = value & 0xff;
                } else if ("lx".equals(key)) {
                    stateLx = clamp12(value);
                } else if ("ly".equals(key)) {
                    stateLy = clamp12(value);
                } else if ("rx".equals(key)) {
                    stateRx = clamp12(value);
                } else if ("ry".equals(key)) {
                    stateRy = clamp12(value);
                }
            }
            if (shouldLogStateOverride()) {
                loggedB5 = stateB5;
                loggedB6 = stateB6;
                loggedB7 = stateB7;
                loggedB8 = stateB8;
                loggedLx = stateLx;
                loggedLy = stateLy;
                loggedRx = stateRx;
                loggedRy = stateRy;
                log("state override b5=" + stateB5 + " b6=" + stateB6 + " b7=" + stateB7 + " b8=" + stateB8 +
                        " lx=" + stateLx + " ly=" + stateLy + " rx=" + stateRx + " ry=" + stateRy);
            }
        } catch (Throwable t) {
            log("state override ignored: " + t);
        }
    }

    private static boolean shouldLogStateOverride() {
        if (stateB5 != loggedB5 || stateB6 != loggedB6 || stateB7 != loggedB7 || stateB8 != loggedB8) {
            lastStateLogMs = System.currentTimeMillis();
            return true;
        }

        int stickDelta = Math.max(Math.max(Math.abs(stateLx - loggedLx), Math.abs(stateLy - loggedLy)),
                Math.max(Math.abs(stateRx - loggedRx), Math.abs(stateRy - loggedRy)));
        long now = System.currentTimeMillis();
        if (stickDelta >= 96 || now - lastStateLogMs >= 5000) {
            lastStateLogMs = now;
            return true;
        }
        return false;
    }

    private static void bridgeBulkOutputToBle(byte[] cmd) {
        if (cmd.length < 2) {
            return;
        }

        if (isSwitch2HidRumbleReport(cmd)) {
            boolean active = hasNonZeroPayload(cmd, 2) && !isNeutralSwitchRumble(cmd);
            writeBlePreset(active ? PRESET_SHORT_LOW : PRESET_STOP,
                    active ? "bulk-hid-report-active" : "bulk-hid-report-stop");
            return;
        }

        int c0 = u(cmd[0]);
        int sub = cmd.length > 3 ? u(cmd[3]) : -1;

        if (c0 == 0x0a && sub == 0x02 && cmd.length >= 16) {
            byte[] ble = Arrays.copyOf(cmd, cmd.length);
            ble[2] = 0x01; // Windows/USB uses transport 0; the BLE command characteristic expects transport 1.
            writeBleCommand(ble, "bulk-vibrate-preset");
            return;
        }

        if (c0 == 0x0a && sub == 0x08) {
            log("bulk rumble config seen, not bridged directly");
            return;
        }

        if (LED_RUMBLE_FLAG_FILE.exists() && c0 == 0x09 && sub == 0x07 && cmd.length >= 16) {
            long now = System.currentTimeMillis();
            if (now - lastBulkLedBridgeMs >= 1000) {
                lastBulkLedBridgeMs = now;
                writeBlePreset(PRESET_NOTIFY, "bulk-led-identify");
            }
        }
    }

    private static boolean isSwitch2HidRumbleReport(byte[] report) {
        return report.length >= 7
                && u(report[0]) == 0x02
                && (u(report[1]) & 0xf0) == 0x50;
    }

    private static boolean hasNonZeroPayload(byte[] report, int offset) {
        for (int i = offset; i < report.length; i++) {
            if (report[i] != 0) {
                return true;
            }
        }
        return false;
    }

    private static boolean isNeutralSwitchRumble(byte[] report) {
        return hasNeutralRumbleFrame(report, 2) && hasNeutralRumbleFrame(report, 0x12);
    }

    private static boolean hasNeutralRumbleFrame(byte[] report, int offset) {
        return report.length >= offset + 5
                && u(report[offset]) == 0x87
                && u(report[offset + 1]) == 0x01
                && u(report[offset + 2]) == 0x20
                && u(report[offset + 3]) == 0x11
                && u(report[offset + 4]) == 0x00;
    }

    private static String rumbleFrameKey(byte[] report) {
        if (report.length < 0x17) {
            return "short";
        }
        return hexSlice(report, 2, 5) + " / " + hexSlice(report, 0x12, 5);
    }

    private static String rumbleDecodedKey(byte[] report) {
        if (report.length < 0x17) {
            return "short";
        }
        int seq = report.length > 1 ? u(report[1]) & 0x0f : -1;
        return "seq=" + seq
                + " left{" + decodeSwitch2RumbleFrame(report, 2) + "}"
                + " right{" + decodeSwitch2RumbleFrame(report, 0x12) + "}";
    }

    private static String decodeSwitch2RumbleFrame(byte[] report, int offset) {
        if (report.length < offset + 5) {
            return "short";
        }
        int b0 = u(report[offset]);
        int b1 = u(report[offset + 1]);
        int b2 = u(report[offset + 2]);
        int b3 = u(report[offset + 3]);
        int b4 = u(report[offset + 4]);

        int highFreq = b0 | ((b1 & 0x03) << 8);
        int highAmp = ((b1 & 0xfc) << 4) | ((b2 & 0x0f) << 12);
        int lowFreq = ((b2 & 0xf0) >> 4) | ((b3 & 0x3f) << 4);
        int lowAmp = (b3 & 0xc0) | (b4 << 8);

        return String.format(Locale.US,
                "hf=0x%03x ha=%d/%d lf=0x%03x la=%d/%d",
                highFreq, highAmp, ampPercent(highAmp), lowFreq, lowAmp, ampPercent(lowAmp));
    }

    private static int ampPercent(int value) {
        int percent = (value * 100 + 14500) / 29000;
        if (percent < 0) {
            return 0;
        }
        if (percent > 100) {
            return 100;
        }
        return percent;
    }

    private static String hexSlice(byte[] data, int offset, int length) {
        StringBuilder out = new StringBuilder();
        int end = Math.min(data.length, offset + length);
        for (int i = offset; i < end; i++) {
            if (out.length() > 0) {
                out.append(' ');
            }
            out.append(String.format(Locale.US, "%02x", u(data[i])));
        }
        return out.toString();
    }

    private static void writeBlePreset(int preset, String reason) {
        int value = preset & 0xff;
        String command = String.format(Locale.US,
                "cmd 0a91010200080000%02x00000000000000%n", value);
        if (HAPTIC_LOG_ONLY_FLAG_FILE.exists() && preset != PRESET_STOP) {
            log("rumble bridge " + reason + " preset=" + preset + " suppressed by log-only mode");
            return;
        }
        try {
            writeText(BLE_WRITE_FILE, command);
            log("rumble bridge " + reason + " preset=" + preset + " wrote " + command.trim());
        } catch (Throwable t) {
            log("rumble bridge failed " + reason + " preset=" + preset + ": " + t);
        }
    }

    private static void writeBleCommand(byte[] data, String reason) {
        String command = "cmd " + hexCompact(data) + "\n";
        try {
            writeText(BLE_WRITE_FILE, command);
            log("rumble bridge " + reason + " wrote " + command.trim());
        } catch (Throwable t) {
            log("rumble bridge failed " + reason + ": " + t);
        }
    }

    private static int parseFlexibleInt(String text) {
        if (text.startsWith("0x") || text.startsWith("0X")) {
            return Integer.parseInt(text.substring(2), 16);
        }
        boolean hex = false;
        for (int i = 0; i < text.length(); i++) {
            char ch = text.charAt(i);
            if ((ch >= 'a' && ch <= 'f') || (ch >= 'A' && ch <= 'F')) {
                hex = true;
                break;
            }
        }
        return Integer.parseInt(text, hex ? 16 : 10);
    }

    private static int clamp12(int value) {
        if (value < 0) {
            return 0;
        }
        if (value > 4095) {
            return 4095;
        }
        return value;
    }

    private static final class HidOutputLoop implements Runnable {
        private final File hid;
        private boolean bridgedRumbleActive;
        private int shortPresetCursor;
        private int richPresetCursor;
        private long rumbleStartMs;
        private long lastShortStopMs;
        private long lastActiveReportMs;
        private long lastRichRepeatMs;
        private long lastGameRepeatMs;
        private int lastShortDurationMs;
        private int currentPreset;
        private int gameSustainPresetCursor;
        private boolean longPresetSent;

        HidOutputLoop(File hid) {
            this.hid = hid;
        }

        public void run() {
            byte[] buf = new byte[64];
            while (running) {
                try {
                    waitFor(hid);
                    FileInputStream in = new FileInputStream(hid);
                    log("opened HID output endpoint " + hid);
                    while (running) {
                        int n = in.read(buf);
                        if (n < 0) {
                            log("HID output EOF");
                            break;
                        }
                        if (n == 0) {
                            continue;
                        }
                        byte[] report = Arrays.copyOf(buf, n);
                        String text = "HID OUT " + n + " bytes: " + hex(report, Math.min(n, 64));
                        log(text);
                        logHidOutput(text);
                        writeText(HID_LAST_OUTPUT_FILE, text + "\n");
                        bridgeHidOutputToBlePreset(report);
                    }
                    in.close();
                } catch (Throwable t) {
                    log("HID output loop: " + t);
                    sleep(500);
                }
            }
        }

        private void bridgeHidOutputToBlePreset(byte[] report) {
            if (report.length < 2 || (report[0] & 0xff) != 0x02) {
                return;
            }

            boolean active = hasNonZeroPayload(report, 2) && !isNeutralSwitchRumble(report);
            long now = System.currentTimeMillis();
            if (active) {
                lastActiveReportMs = now;
                if (bridgedRumbleActive) {
                    if (RICH_HAPTIC_CYCLE_FLAG_FILE.exists()) {
                        maybeRepeatRichRumble(report, now);
                    } else {
                        maybeUpgradeLongRumble(report, now);
                    }
                    return;
                }
                bridgedRumbleActive = true;
                rumbleStartMs = now;
                lastRichRepeatMs = now;
                lastGameRepeatMs = now;
                long gap = lastShortStopMs == 0 ? -1 : now - lastShortStopMs;
                boolean doublePulse = lastShortStopMs != 0
                        && gap >= 0
                        && gap <= DOUBLE_PULSE_WINDOW_MS
                        && lastShortDurationMs > 0
                        && lastShortDurationMs < LONG_RUMBLE_THRESHOLD_MS;
                boolean richCycle = RICH_HAPTIC_CYCLE_FLAG_FILE.exists();
                currentPreset = richCycle ? nextRichPreset() : (doublePulse ? PRESET_DOUBLE : nextShortPreset());
                longPresetSent = false;
                log("HID rumble event start kind=" + (richCycle ? "rich-cycle" : (doublePulse ? "double" : "short"))
                        + " preset=" + currentPreset
                        + " gapMs=" + gap
                        + " frame=" + rumbleFrameKey(report)
                        + " decoded=" + rumbleDecodedKey(report));
                writeBlePreset(currentPreset, "hid-out-active");
            } else if (bridgedRumbleActive) {
                bridgedRumbleActive = false;
                int duration = (int) (now - rumbleStartMs);
                if (duration < LONG_RUMBLE_THRESHOLD_MS) {
                    lastShortStopMs = now;
                    lastShortDurationMs = duration;
                } else {
                    lastShortStopMs = 0;
                    lastShortDurationMs = 0;
                }
                log("HID rumble event stop durationMs=" + duration
                        + " activeReportAgeMs=" + (lastActiveReportMs == 0 ? -1 : now - lastActiveReportMs)
                        + " preset=" + currentPreset
                        + " longSent=" + longPresetSent);
                writeBlePreset(PRESET_STOP, "hid-out-stop");
                lastRichRepeatMs = 0;
                lastGameRepeatMs = 0;
            }
        }

        private void maybeRepeatRichRumble(byte[] report, long now) {
            if (lastRichRepeatMs != 0 && now - lastRichRepeatMs < RICH_RUMBLE_REPEAT_MS) {
                return;
            }
            lastRichRepeatMs = now;
            currentPreset = nextRichPreset();
            log("HID rumble event repeat kind=rich-cycle preset=" + currentPreset
                    + " durationMs=" + (now - rumbleStartMs)
                    + " frame=" + rumbleFrameKey(report)
                    + " decoded=" + rumbleDecodedKey(report));
            writeBlePreset(currentPreset, "hid-out-rich-repeat");
        }

        private void maybeUpgradeLongRumble(byte[] report, long now) {
            long duration = now - rumbleStartMs;
            if (!longPresetSent && duration >= LONG_RUMBLE_THRESHOLD_MS) {
                longPresetSent = true;
                currentPreset = PRESET_LONG;
                lastGameRepeatMs = now;
                log("HID rumble event upgrade kind=long preset=" + currentPreset
                        + " durationMs=" + duration
                        + " frame=" + rumbleFrameKey(report)
                        + " decoded=" + rumbleDecodedKey(report));
                writeBlePreset(currentPreset, "hid-out-long");
                return;
            }
            if (longPresetSent && lastGameRepeatMs != 0 && now - lastGameRepeatMs >= GAME_RUMBLE_REPEAT_MS) {
                lastGameRepeatMs = now;
                currentPreset = nextGameSustainPreset();
                log("HID rumble event repeat kind=game-sustain preset=" + currentPreset
                        + " durationMs=" + duration
                        + " frame=" + rumbleFrameKey(report)
                        + " decoded=" + rumbleDecodedKey(report));
                writeBlePreset(currentPreset, "hid-out-game-repeat");
            }
        }

        private int nextShortPreset() {
            int preset = HID_SHORT_PRESET_SEQUENCE[shortPresetCursor % HID_SHORT_PRESET_SEQUENCE.length];
            shortPresetCursor++;
            return preset;
        }

        private int nextRichPreset() {
            int preset = HID_RICH_PRESET_SEQUENCE[richPresetCursor % HID_RICH_PRESET_SEQUENCE.length];
            richPresetCursor++;
            return preset;
        }

        private int nextGameSustainPreset() {
            int preset = HID_GAME_SUSTAIN_PRESET_SEQUENCE[gameSustainPresetCursor % HID_GAME_SUSTAIN_PRESET_SEQUENCE.length];
            gameSustainPresetCursor++;
            return preset;
        }

        private void writeBlePreset(int preset, String reason) {
            Switch2FfsResponder.writeBlePreset(preset, reason);
        }
    }

    private static final class HidNeutralLoop implements Runnable {
        private final File hid;

        HidNeutralLoop(File hid) {
            this.hid = hid;
        }

        public void run() {
            while (running) {
                try {
                    waitFor(hid);
                    FileOutputStream out = new FileOutputStream(hid);
                    log("opened HID state endpoint " + hid);
                    while (running) {
                        out.write(neutralSwitch2State());
                        out.flush();
                        Thread.sleep(8);
                    }
                } catch (Throwable t) {
                    log("HID state loop: " + t);
                    sleep(500);
                }
            }
        }
    }

    private static final class Ep0EventLoop implements Runnable {
        private final RandomAccessFile ep0;

        Ep0EventLoop(RandomAccessFile ep0) {
            this.ep0 = ep0;
        }

        public void run() {
            byte[] buf = new byte[12 * 8];
            while (running) {
                try {
                    int n;
                    while ((n = ep0.read(buf)) > 0) {
                        for (int off = 0; off + 12 <= n; off += 12) {
                            int type = u(buf[off + 8]);
                            log("ep0 event type=" + type);
                        }
                    }
                } catch (Throwable t) {
                    log("ep0 loop: " + t);
                    sleep(500);
                }
            }
        }
    }

    private static void waitFor(File file) {
        while (!file.exists()) {
            sleep(100);
        }
    }

    private static void touch(String path) throws IOException {
        FileOutputStream out = new FileOutputStream(path);
        out.write(1);
        out.close();
    }

    private static void deleteIfExists(File file) {
        if (file.exists() && !file.delete()) {
            log("could not delete " + file);
        }
    }

    private static void writeText(File file, String text) throws IOException {
        FileOutputStream out = new FileOutputStream(file, false);
        out.write(text.getBytes("UTF-8"));
        out.close();
    }

    private static void closeQuietly(Closeable closeable) {
        if (closeable == null) {
            return;
        }
        try {
            closeable.close();
        } catch (IOException ignored) {
        }
    }

    private static void le16(ByteArrayOutputStream out, int value) {
        out.write(value & 0xff);
        out.write((value >> 8) & 0xff);
    }

    private static void le32(ByteArrayOutputStream out, int value) {
        out.write(value & 0xff);
        out.write((value >> 8) & 0xff);
        out.write((value >> 16) & 0xff);
        out.write((value >> 24) & 0xff);
    }

    private static int u(byte b) {
        return b & 0xff;
    }

    private static void sleep(long ms) {
        try {
            Thread.sleep(ms);
        } catch (InterruptedException ignored) {
        }
    }

    private static String hex(byte[] data, int n) {
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < n; i++) {
            if (i > 0) {
                sb.append(' ');
            }
            int v = data[i] & 0xff;
            if (v < 16) {
                sb.append('0');
            }
            sb.append(Integer.toHexString(v));
        }
        if (data.length > n) {
            sb.append(" ...");
        }
        return sb.toString();
    }

    private static String hexCompact(byte[] data) {
        StringBuilder sb = new StringBuilder(data.length * 2);
        for (int i = 0; i < data.length; i++) {
            int v = data[i] & 0xff;
            if (v < 16) {
                sb.append('0');
            }
            sb.append(Integer.toHexString(v));
        }
        return sb.toString();
    }

    private static synchronized void log(String msg) {
        writeLogLine(LOG_FILE, msg);
    }

    private static synchronized void logHidOutput(String msg) {
        writeLogLine(HID_OUTPUT_LOG_FILE, msg);
    }

    private static void writeLogLine(File file, String msg) {
        String ts = new SimpleDateFormat("yyyy-MM-dd HH:mm:ss.SSS", Locale.US).format(new Date());
        String line = ts + " " + msg + "\n";
        try {
            FileOutputStream out = new FileOutputStream(file, true);
            out.write(line.getBytes("UTF-8"));
            out.close();
        } catch (IOException ignored) {
        }
    }
}
