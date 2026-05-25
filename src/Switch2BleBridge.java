import android.bluetooth.BluetoothAdapter;
import android.bluetooth.BluetoothDevice;
import android.bluetooth.BluetoothGatt;
import android.bluetooth.BluetoothGattCallback;
import android.bluetooth.BluetoothGattCharacteristic;
import android.bluetooth.BluetoothGattDescriptor;
import android.bluetooth.BluetoothGattService;
import android.bluetooth.BluetoothManager;
import android.bluetooth.BluetoothProfile;
import android.bluetooth.le.BluetoothLeScanner;
import android.bluetooth.le.ScanCallback;
import android.bluetooth.le.ScanRecord;
import android.bluetooth.le.ScanResult;
import android.content.Context;
import android.os.Build;
import android.os.IBinder;
import android.os.Looper;
import android.os.ParcelUuid;

import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.lang.reflect.Constructor;
import java.lang.reflect.Field;
import java.lang.reflect.Method;
import java.lang.reflect.Modifier;
import java.text.SimpleDateFormat;
import java.util.ArrayDeque;
import java.util.Date;
import java.util.HashSet;
import java.util.List;
import java.util.Locale;
import java.util.Queue;
import java.util.Set;
import java.util.UUID;

public class Switch2BleBridge {
    private static final String DEFAULT_ADDRESS = "38:C6:CE:27:FC:2D";
    private static final File DEFAULT_STATE_FILE = new File("/data/local/tmp/switch2_state.txt");
    private static final File LOG_FILE = new File("/data/local/tmp/switch2_ble_bridge.log");
    private static final File RAW_FILE = new File("/data/local/tmp/switch2_ble_input_raw.log");
    private static final File BUTTON_FILE = new File("/data/local/tmp/switch2_button_changes.log");
    private static final File BLE_WRITE_FILE = new File("/data/local/tmp/switch2_ble_write.txt");

    private static final UUID SERVICE_MAIN = UUID.fromString("ab7de9be-89fe-49ad-828f-118f09df7fd0");
    private static final UUID CHAR_INPUT = UUID.fromString("7492866c-ec3e-4619-8258-32755ffcc0f8");
    private static final UUID CHAR_TELEMETRY = UUID.fromString("ab7de9be-89fe-49ad-828f-118f09df7fd2");
    private static final UUID CHAR_WRITE_3DAC = UUID.fromString("3dacbc7e-6955-40b5-8eaf-6f9809e8b379");
    private static final UUID CHAR_WRITE_4147 = UUID.fromString("4147423d-fdae-4df7-a4f7-d23e5df59f8d");
    private static final UUID CHAR_WRITE_649D = UUID.fromString("649d4ac9-8eb7-4e6c-af44-1ea54fe5f005");
    private static final UUID CHAR_WRITE_FDF = UUID.fromString("ab7de9be-89fe-49ad-828f-118f09df7fdf");
    private static final UUID CHAR_WRITE_CC48 = UUID.fromString("cc483f51-9258-427d-a939-630c31f72b05");
    private static final UUID CHAR_CMD = CHAR_WRITE_649D;
    private static final UUID CHAR_ACK = UUID.fromString("c765a961-d9d8-4d36-a20a-5315b111836a");
    private static final UUID CHAR_NOTIFY_506D = UUID.fromString("506d9f7d-4278-4e95-a549-326ba77657e0");
    private static final UUID CHAR_NOTIFY_D3BD = UUID.fromString("d3bd69d2-841c-4241-ab15-f86f406d2a80");
    private static final UUID CHAR_NOTIFY_FDE = UUID.fromString("ab7de9be-89fe-49ad-828f-118f09df7fde");
    private static final UUID CLIENT_CONFIG = UUID.fromString("00002902-0000-1000-8000-00805f9b34fb");
    private static final int NINTENDO_COMPANY_ID = 0x0553;
    private static final int OUTPUT_DEADZONE = 48;
    private static final String[] INIT_NAMES = new String[] {
            "INIT", "CMD_07", "CMD_16", "CMD_15_03", "FEATSEL_SET_MASK", "CMD_11",
            "VIBRATE_CFG", "FEATSEL_ENABLE", "SELECT_REPORT", "FW_INFO_GET",
            "CMD_01_0C", "RUMBLE_ENABLE", "SET_PLAYER_LED", "CALIB_LEFT", "CALIB_RIGHT"
    };
    private static final byte[][] INIT_COMMANDS = new byte[][] {
            hexBytes("03 91 01 0d 00 08 00 00 01 00 ff ff ff ff ff ff"),
            hexBytes("07 91 01 01 00 00 00 00"),
            hexBytes("16 91 01 01 00 00 00 00"),
            hexBytes("15 91 01 03 00 01 00 00 00"),
            hexBytes("0c 91 01 02 00 04 00 00 2f 00 00 00"),
            hexBytes("11 91 01 03 00 00 00 00"),
            hexBytes("0a 91 01 08 00 14 00 00 01 ff ff ff ff ff ff ff ff 35 00 46 00 00 00 00 00 00 00 00"),
            hexBytes("0c 91 01 04 00 04 00 00 2f 00 00 00"),
            hexBytes("03 91 01 0a 00 04 00 00 09 00 00 00"),
            hexBytes("10 91 01 01 00 00 00 00"),
            hexBytes("01 91 01 0c 00 00 00 00"),
            hexBytes("01 91 01 01 00 04 00 00 00 00 00 00"),
            hexBytes("09 91 01 07 00 08 00 00 01 00 00 00 00 00 00 00"),
            hexBytes("02 91 01 04 00 08 00 00 09 7e 00 00 a8 30 01 00"),
            hexBytes("02 91 01 04 00 08 00 00 09 7e 00 00 e8 30 01 00")
    };

    private static final Queue<BluetoothGattDescriptor> descriptorQueue = new ArrayDeque<BluetoothGattDescriptor>();
    private static final Object queueLock = new Object();
    private static final Object scanLock = new Object();

    private static String address = DEFAULT_ADDRESS;
    private static File stateFile = DEFAULT_STATE_FILE;
    private static boolean writeState = true;
    private static volatile boolean running = true;
    private static volatile BluetoothGatt currentGatt;
    private static volatile BluetoothGattCharacteristic cmdCharacteristic;
    private static volatile ScanCallback activeScan;
    private static int descriptorPhase;
    private static int initIndex;
    private static boolean initStarted;
    private static boolean initDone;
    private static byte[] lastInput;
    private static long lastSummaryMs;
    private static int lastRawB2 = -1;
    private static int lastRawB3 = -1;
    private static int lastRawB4 = -1;
    private static int notifyCount;
    private static int calibrationCount;
    private static long sumLx;
    private static long sumLy;
    private static long sumRx;
    private static long sumRy;
    private static boolean calibrated;
    private static int centerLx = 2048;
    private static int centerLy = 2048;
    private static int centerRx = 2048;
    private static int centerRy = 2048;

    public static void main(String[] args) throws Exception {
        try {
            run(args);
        } catch (Throwable t) {
            log("fatal: " + t);
            StackTraceElement[] trace = t.getStackTrace();
            for (int i = 0; i < trace.length && i < 16; i++) {
                log("  at " + trace[i]);
            }
            throw t;
        }
    }

    private static void run(String[] args) throws Exception {
        parseArgs(args);
        truncate(LOG_FILE);
        truncate(RAW_FILE);
        truncate(BUTTON_FILE);
        writeNeutralState();

        Runtime.getRuntime().addShutdownHook(new Thread(new Runnable() {
            public void run() {
                running = false;
                writeNeutralState();
                BluetoothGatt gatt = currentGatt;
                if (gatt != null) {
                    try {
                        gatt.disconnect();
                        gatt.close();
                    } catch (Throwable ignored) {
                    }
                }
            }
        }, "shutdown"));

        prepareLooper();
        Context context = bluetoothContext(systemContext());
        BluetoothManager manager = (BluetoothManager) context.getSystemService(Context.BLUETOOTH_SERVICE);
        BluetoothAdapter adapter = manager != null ? manager.getAdapter() : null;
        if (adapter == null) {
            adapter = BluetoothAdapter.getDefaultAdapter();
        }
        if (adapter == null) {
            adapter = adapterFromHiddenCreateAdapter(context);
        }
        if (adapter == null) {
            throw new IllegalStateException("BluetoothAdapter is null");
        }
        publishDefaultAdapter(adapter);
        if (!adapter.isEnabled()) {
            log("Bluetooth is disabled; enable it on Y700 first.");
        }

        log("Switch2BleBridge starting");
        log("target address=" + address + ", stateFile=" + stateFile + ", writeState=" + writeState);
        log("input characteristic=" + CHAR_INPUT);
        log("BLE write command file=" + BLE_WRITE_FILE);

        Thread writeFileThread = new Thread(new BleWriteFileLoop(), "ble-write-file");
        writeFileThread.setDaemon(true);
        writeFileThread.start();

        connect(context, adapter);
        Looper.loop();
    }

    private static void prepareLooper() {
        try {
            Looper.prepareMainLooper();
        } catch (Throwable mainLooperError) {
            try {
                Looper.prepare();
            } catch (Throwable ignored) {
            }
        }
    }

    private static void parseArgs(String[] args) {
        for (int i = 0; i < args.length; i++) {
            String arg = args[i];
            if ("--no-state".equals(arg)) {
                writeState = false;
            } else if ("--state".equals(arg) && i + 1 < args.length) {
                stateFile = new File(args[++i]);
            } else if ("--address".equals(arg) && i + 1 < args.length) {
                address = args[++i];
            } else if (!arg.startsWith("--")) {
                address = arg;
            }
        }
    }

    private static Context systemContext() throws Exception {
        Class<?> activityThread = Class.forName("android.app.ActivityThread");
        Method systemMain = activityThread.getDeclaredMethod("systemMain");
        Object thread = systemMain.invoke(null);
        Method getSystemContext = activityThread.getDeclaredMethod("getSystemContext");
        return (Context) getSystemContext.invoke(thread);
    }

    private static Context bluetoothContext(Context base) {
        String[] packages = new String[] {
                "com.android.shell",
                "com.android.bluetooth",
                "android"
        };
        for (int i = 0; i < packages.length; i++) {
            try {
                Context context = base.createPackageContext(packages[i], 0);
                Object manager = context.getSystemService(Context.BLUETOOTH_SERVICE);
                log("package context " + packages[i] + " bluetoothService=" + manager);
                if (manager != null) {
                    return context;
                }
            } catch (Throwable t) {
                log("package context " + packages[i] + " failed: " + t);
            }
        }
        return base;
    }

    private static BluetoothAdapter adapterFromHiddenCreateAdapter(Context context) {
        try {
            Class<?> serviceManager = Class.forName("android.os.ServiceManager");
            Method getService = serviceManager.getDeclaredMethod("getService", String.class);
            IBinder binder = (IBinder) getService.invoke(null, "bluetooth_manager");
            log("bluetooth_manager binder=" + binder);
            if (binder == null && Build.VERSION.SDK_INT < 31) {
                return null;
            }

            Method createAdapter = null;
            Method[] methods = BluetoothAdapter.class.getDeclaredMethods();
            for (int i = 0; i < methods.length; i++) {
                Method method = methods[i];
                if ("createAdapter".equals(method.getName()) && method.getParameterTypes().length == 1) {
                    createAdapter = method;
                    break;
                }
            }
            if (createAdapter == null) {
                log("BluetoothAdapter.createAdapter method not found");
            } else {
                Class<?> param = createAdapter.getParameterTypes()[0];
                Object arg;
                if ("android.content.AttributionSource".equals(param.getName())) {
                    Method getAttributionSource = Context.class.getDeclaredMethod("getAttributionSource");
                    arg = getAttributionSource.invoke(context);
                    log("using createAdapter(AttributionSource)");
                } else if ("android.bluetooth.IBluetoothManager".equals(param.getName())) {
                    Class<?> stub = Class.forName("android.bluetooth.IBluetoothManager$Stub");
                    Method asInterface = stub.getDeclaredMethod("asInterface", IBinder.class);
                    arg = asInterface.invoke(null, binder);
                    log("using createAdapter(IBluetoothManager)");
                } else {
                    log("unsupported createAdapter parameter: " + param.getName());
                    arg = null;
                }

                if (arg != null) {
                    createAdapter.setAccessible(true);
                    BluetoothAdapter adapter = (BluetoothAdapter) createAdapter.invoke(null, arg);
                    if (adapter != null) {
                        return adapter;
                    }
                    log("createAdapter returned null; trying constructors");
                }
            }

            Object manager = null;
            if (binder != null) {
                Class<?> stub = Class.forName("android.bluetooth.IBluetoothManager$Stub");
                Method asInterface = stub.getDeclaredMethod("asInterface", IBinder.class);
                manager = asInterface.invoke(null, binder);
            }

            Object attribution = null;
            try {
                Method getAttributionSource = Context.class.getDeclaredMethod("getAttributionSource");
                attribution = getAttributionSource.invoke(context);
            } catch (Throwable t) {
                log("getAttributionSource failed: " + t);
            }

            Constructor<?>[] constructors = BluetoothAdapter.class.getDeclaredConstructors();
            for (int i = 0; i < constructors.length; i++) {
                Constructor<?> ctor = constructors[i];
                Class<?>[] params = ctor.getParameterTypes();
                log("BluetoothAdapter ctor " + signature(params));
                try {
                    ctor.setAccessible(true);
                    if (params.length == 2 &&
                            "android.bluetooth.IBluetoothManager".equals(params[0].getName()) &&
                            "android.content.AttributionSource".equals(params[1].getName()) &&
                            manager != null && attribution != null) {
                        return (BluetoothAdapter) ctor.newInstance(manager, attribution);
                    }
                    if (params.length == 1 &&
                            "android.content.AttributionSource".equals(params[0].getName()) &&
                            attribution != null) {
                        return (BluetoothAdapter) ctor.newInstance(attribution);
                    }
                    if (params.length == 0) {
                        return (BluetoothAdapter) ctor.newInstance();
                    }
                } catch (Throwable t) {
                    Throwable cause = t.getCause();
                    log("ctor failed: " + t + (cause != null ? " cause=" + cause : ""));
                }
            }
            return null;
        } catch (Throwable t) {
            log("adapterFromHiddenCreateAdapter failed: " + t);
            return null;
        }
    }

    private static void publishDefaultAdapter(BluetoothAdapter adapter) {
        try {
            Field[] fields = BluetoothAdapter.class.getDeclaredFields();
            for (int i = 0; i < fields.length; i++) {
                Field field = fields[i];
                int mods = field.getModifiers();
                if (Modifier.isStatic(mods) && field.getType() == BluetoothAdapter.class) {
                    field.setAccessible(true);
                    Object current = field.get(null);
                    if (current == null) {
                        field.set(null, adapter);
                        log("published default adapter into " + field.getName());
                    } else {
                        log("default adapter field already set: " + field.getName());
                    }
                }
            }
        } catch (Throwable t) {
            log("publishDefaultAdapter failed: " + t);
        }
    }

    private static String signature(Class<?>[] params) {
        StringBuilder out = new StringBuilder("(");
        for (int i = 0; i < params.length; i++) {
            if (i > 0) {
                out.append(',');
            }
            out.append(params[i].getName());
        }
        out.append(')');
        return out.toString();
    }

    private static void connect(final Context context, final BluetoothAdapter adapter) {
        if (!running) {
            return;
        }
        if (startScanThenConnect(context, adapter)) {
            return;
        }
        connectDevice(context, adapter.getRemoteDevice(address), "direct");
    }

    private static boolean startScanThenConnect(final Context context, final BluetoothAdapter adapter) {
        final BluetoothLeScanner scanner;
        try {
            scanner = adapter.getBluetoothLeScanner();
        } catch (Throwable t) {
            log("getBluetoothLeScanner failed: " + t);
            return false;
        }
        if (scanner == null) {
            log("BLE scanner is null; falling back to direct connect");
            return false;
        }

        final Set<String> seen = new HashSet<String>();
        final ScanCallback callback = new ScanCallback() {
            public void onScanResult(int callbackType, ScanResult result) {
                handleScanResult(context, adapter, scanner, this, result, seen);
            }

            public void onBatchScanResults(List<ScanResult> results) {
                for (int i = 0; i < results.size(); i++) {
                    handleScanResult(context, adapter, scanner, this, results.get(i), seen);
                }
            }

            public void onScanFailed(int errorCode) {
                log("BLE scan failed error=" + errorCode);
                if (finishScan(scanner, this)) {
                    connectDevice(context, adapter.getRemoteDevice(address), "direct-after-scan-failed");
                }
            }
        };

        synchronized (scanLock) {
            activeScan = callback;
        }

        try {
            log("starting BLE scan for address=" + address + " or manufacturer=0x0553");
            scanner.startScan(callback);
        } catch (Throwable t) {
            log("startScan failed: " + t);
            synchronized (scanLock) {
                if (activeScan == callback) {
                    activeScan = null;
                }
            }
            return false;
        }

        new Thread(new Runnable() {
            public void run() {
                sleep(10000);
                if (finishScan(scanner, callback)) {
                    log("BLE scan timeout; falling back to direct connect");
                    connectDevice(context, adapter.getRemoteDevice(address), "direct-after-scan-timeout");
                }
            }
        }, "scan-timeout").start();
        return true;
    }

    private static void handleScanResult(Context context, BluetoothAdapter adapter, BluetoothLeScanner scanner,
            ScanCallback callback, ScanResult result, Set<String> seen) {
        if (result == null || result.getDevice() == null) {
            return;
        }
        BluetoothDevice device = result.getDevice();
        String foundAddress = device.getAddress();
        ScanRecord record = result.getScanRecord();
        byte[] nintendoData = record != null ? record.getManufacturerSpecificData(NINTENDO_COMPANY_ID) : null;
        List<ParcelUuid> uuids = record != null ? record.getServiceUuids() : null;
        String name = record != null ? record.getDeviceName() : null;

        boolean exactAddress = foundAddress != null && foundAddress.equalsIgnoreCase(address);
        boolean nintendoManufacturer = nintendoData != null;
        boolean interestingUuid = hasShortUuid(uuids, 0xff80) || hasShortUuid(uuids, 0xff90);
        boolean interesting = exactAddress || nintendoManufacturer || interestingUuid;

        if (interesting || seen.add(foundAddress)) {
            StringBuilder line = new StringBuilder();
            line.append("scan result addr=").append(foundAddress)
                    .append(" name=").append(name)
                    .append(" rssi=").append(result.getRssi());
            if (nintendoData != null) {
                line.append(" mfr0553=").append(hex(nintendoData, nintendoData.length));
            }
            if (uuids != null) {
                line.append(" uuids=").append(uuids);
            }
            log(line.toString());
        }

        if (interesting && finishScan(scanner, callback)) {
            connectDevice(context, device, exactAddress ? "scan-address" :
                    (nintendoManufacturer ? "scan-manufacturer" : "scan-uuid"));
        }
    }

    private static boolean hasShortUuid(List<ParcelUuid> uuids, int shortUuid) {
        if (uuids == null) {
            return false;
        }
        String needle = String.format(Locale.US, "0000%04x-0000-1000-8000-00805f9b34fb", shortUuid);
        for (int i = 0; i < uuids.size(); i++) {
            if (needle.equalsIgnoreCase(uuids.get(i).getUuid().toString())) {
                return true;
            }
        }
        return false;
    }

    private static boolean finishScan(BluetoothLeScanner scanner, ScanCallback callback) {
        synchronized (scanLock) {
            if (activeScan != callback) {
                return false;
            }
            activeScan = null;
        }
        try {
            scanner.stopScan(callback);
        } catch (Throwable t) {
            log("stopScan failed: " + t);
        }
        return true;
    }

    private static void connectDevice(final Context context, BluetoothDevice device, String reason) {
        try {
            log("connecting GATT transport LE to " + device.getAddress() + " reason=" + reason);
            currentGatt = device.connectGatt(context, false, new GattCallback(context, BluetoothAdapter.getDefaultAdapter()), BluetoothDevice.TRANSPORT_LE);
        } catch (Throwable t) {
            log("connect failed: " + t);
            sleep(2000);
            BluetoothAdapter adapter = BluetoothAdapter.getDefaultAdapter();
            if (adapter != null) {
                connect(context, adapter);
            }
        }
    }

    private static final class BleWriteFileLoop implements Runnable {
        private long lastMtime;
        private long lastLength;

        public void run() {
            while (running) {
                try {
                    long mtime = BLE_WRITE_FILE.exists() ? BLE_WRITE_FILE.lastModified() : 0;
                    long length = BLE_WRITE_FILE.exists() ? BLE_WRITE_FILE.length() : 0;
                    if (mtime != 0 && length > 0 && (mtime != lastMtime || length != lastLength)) {
                        lastMtime = mtime;
                        lastLength = length;
                        processBleWriteFile();
                    }
                } catch (Throwable t) {
                    log("BLE write file loop: " + t);
                }
                sleep(100);
            }
        }
    }

    private static void processBleWriteFile() {
        try {
            String text = readText(BLE_WRITE_FILE, 4096);
            String[] lines = text.split("\\r?\\n");
            for (int i = 0; i < lines.length; i++) {
                String line = lines[i].trim();
                if (line.length() == 0 || line.startsWith("#")) {
                    continue;
                }
                sendBleWriteLine(line);
            }
        } catch (Throwable t) {
            log("BLE write file ignored: " + t);
        }
    }

    private static void sendBleWriteLine(String line) {
        String trimmed = line.trim();
        if ("play-raw".equalsIgnoreCase(trimmed)) {
            startHapticTest("raw");
            return;
        }
        if ("play-kernel".equalsIgnoreCase(trimmed)) {
            startHapticTest("kernel");
            return;
        }
        if ("play-preset".equalsIgnoreCase(trimmed)) {
            startHapticTest("preset");
            return;
        }
        int space = line.indexOf(' ');
        if (space <= 0) {
            log("BLE write line needs '<uuid-or-alias> <hex>': " + line);
            return;
        }
        UUID uuid = parseWriteUuid(line.substring(0, space));
        if (uuid == null) {
            log("BLE write unknown target: " + line);
            return;
        }
        byte[] data = hexBytes(line.substring(space + 1));
        sendBleWrite(uuid, data);
    }

    private static UUID parseWriteUuid(String text) {
        String key = text.trim().toLowerCase(Locale.US);
        if ("3dac".equals(key)) {
            return CHAR_WRITE_3DAC;
        }
        if ("4147".equals(key)) {
            return CHAR_WRITE_4147;
        }
        if ("649d".equals(key)) {
            return CHAR_WRITE_649D;
        }
        if ("cmd".equals(key)) {
            return CHAR_CMD;
        }
        if ("fdf".equals(key) || "abfdf".equals(key)) {
            return CHAR_WRITE_FDF;
        }
        if ("cc48".equals(key)) {
            return CHAR_WRITE_CC48;
        }
        try {
            return UUID.fromString(text);
        } catch (Throwable ignored) {
            return null;
        }
    }

    @SuppressWarnings("deprecation")
    private static void sendBleWrite(UUID uuid, byte[] data) {
        BluetoothGatt gatt = currentGatt;
        if (gatt == null) {
            log("BLE write skipped, no current GATT uuid=" + uuid + " data=" + hex(data, data.length));
            return;
        }
        BluetoothGattService service = gatt.getService(SERVICE_MAIN);
        if (service == null) {
            log("BLE write skipped, main service missing uuid=" + uuid);
            return;
        }
        BluetoothGattCharacteristic ch = service.getCharacteristic(uuid);
        if (ch == null) {
            log("BLE write skipped, char missing uuid=" + uuid);
            return;
        }
        writeCharacteristicNoResponse(gatt, ch, data, "BLE write uuid=" + uuid);
    }

    private static void startHapticTest(final String mode) {
        Thread thread = new Thread(new Runnable() {
            public void run() {
                runHapticTest(mode);
            }
        }, "haptic-test-" + mode);
        thread.setDaemon(true);
        thread.start();
    }

    private static void runHapticTest(String mode) {
        BluetoothGatt gatt = currentGatt;
        BluetoothGattCharacteristic cmd = cmdCharacteristic;
        if (gatt == null || cmd == null) {
            log("haptic test skipped, gatt/cmd missing mode=" + mode);
            return;
        }

        log("haptic test start mode=" + mode);
        if ("preset".equals(mode)) {
            byte[][] commands = new byte[][] {
                    hexBytes("01 91 01 01 00 04 00 00 00 00 00 00"),
                    hexBytes("0a 91 01 02 00 08 00 00 01 00 00 00 00 00 00 00"),
                    hexBytes("0a 91 01 02 00 08 00 00 02 00 00 00 00 00 00 00"),
                    hexBytes("0a 91 01 02 00 08 00 00 03 00 00 00 00 00 00 00"),
                    hexBytes("0a 91 01 02 00 08 00 00 04 00 00 00 00 00 00 00"),
                    hexBytes("0a 91 01 02 00 08 00 00 05 00 00 00 00 00 00 00"),
                    hexBytes("0a 91 01 02 00 08 00 00 00 00 00 00 00 00 00 00")
            };
            for (int i = 0; i < commands.length; i++) {
                writeCharacteristicNoResponse(gatt, cmd, commands[i], "haptic preset " + i);
                sleep(250);
            }
            log("haptic test complete mode=" + mode);
            return;
        }

        for (int i = 0; i < 220; i++) {
            byte[] report;
            if ("kernel".equals(mode)) {
                report = buildKernelRumbleReport(i & 0x0f, true);
            } else {
                report = buildPatternRumbleReport(i & 0x0f, i);
            }
            writeCharacteristicNoResponse(gatt, cmd, report, "haptic stream " + mode + " seq=" + (i & 0x0f));
            sleep(4);
        }
        for (int i = 0; i < 12; i++) {
            byte[] stop = "kernel".equals(mode)
                    ? buildKernelRumbleReport(i & 0x0f, false)
                    : buildZeroRumbleReport(i & 0x0f);
            writeCharacteristicNoResponse(gatt, cmd, stop, "haptic stop " + mode + " seq=" + (i & 0x0f));
            sleep(4);
        }
        log("haptic test complete mode=" + mode);
    }

    private static byte[] buildPatternRumbleReport(int seq, int index) {
        byte[][] pattern = new byte[][] {
                hexBytes("93 35 36 1c 0d"),
                hexBytes("a8 29 c5 dc 0c"),
                hexBytes("75 21 b5 5d 13"),
                hexBytes("75 f5 70 1e 11"),
                hexBytes("ba 55 40 1e 08"),
                hexBytes("90 31 10 9e 00"),
                hexBytes("75 15 73 1e 11"),
                hexBytes("7b 95 92 5c 13"),
                hexBytes("8d c5 a1 1b 10"),
                hexBytes("7e 31 c1 dc 0b"),
                hexBytes("6f 2d 31 dc 03"),
                hexBytes("75 19 41 9b 03")
        };
        byte[] report = new byte[64];
        byte[] haptic = pattern[index % pattern.length];
        report[0] = 0x02;
        report[1] = (byte) (0x50 | (seq & 0x0f));
        System.arraycopy(haptic, 0, report, 2, haptic.length);
        report[0x11] = report[1];
        System.arraycopy(haptic, 0, report, 0x12, haptic.length);
        return report;
    }

    private static byte[] buildKernelRumbleReport(int seq, boolean active) {
        byte[] report = new byte[64];
        report[0] = 0x02;
        report[1] = (byte) (0x50 | (seq & 0x0f));
        encodeKernelRumble(report, 2, active ? 0x01c1 : 0, active ? 0x01c1 : 0);
        report[0x11] = report[1];
        encodeKernelRumble(report, 0x12, active ? 0x01c1 : 0, active ? 0x01c1 : 0);
        return report;
    }

    private static byte[] buildZeroRumbleReport(int seq) {
        byte[] report = new byte[64];
        report[0] = 0x02;
        report[1] = (byte) (0x50 | (seq & 0x0f));
        report[0x11] = report[1];
        return report;
    }

    private static void encodeKernelRumble(byte[] out, int offset, int hiAmp, int loAmp) {
        int hiFreq = 0x187;
        int loFreq = 0x112;
        out[offset] = (byte) hiFreq;
        out[offset + 1] = (byte) ((hiFreq >> 8) | (hiAmp << 2));
        out[offset + 2] = (byte) ((hiAmp >> 6) | (loFreq << 4));
        out[offset + 3] = (byte) ((loFreq >> 4) | (loAmp << 6));
        out[offset + 4] = (byte) (loAmp >> 2);
    }

    @SuppressWarnings("deprecation")
    private static void writeCharacteristicNoResponse(BluetoothGatt gatt, BluetoothGattCharacteristic ch,
            byte[] data, String label) {
        ch.setWriteType(BluetoothGattCharacteristic.WRITE_TYPE_NO_RESPONSE);
        if (Build.VERSION.SDK_INT >= 33) {
            int result = gatt.writeCharacteristic(ch, data, BluetoothGattCharacteristic.WRITE_TYPE_NO_RESPONSE);
            log(label + " new api n=" + data.length + " result=" + result + " data=" + hex(data, data.length));
        } else {
            ch.setValue(data);
            boolean result = gatt.writeCharacteristic(ch);
            log(label + " old api n=" + data.length + " result=" + result + " data=" + hex(data, data.length));
        }
    }

    private static final class GattCallback extends BluetoothGattCallback {
        private final Context context;
        private final BluetoothAdapter adapter;

        GattCallback(Context context, BluetoothAdapter adapter) {
            this.context = context;
            this.adapter = adapter;
        }

        public void onConnectionStateChange(BluetoothGatt gatt, int status, int newState) {
            log("connection state status=" + status + " newState=" + newState);
            if (newState == BluetoothProfile.STATE_CONNECTED) {
                currentGatt = gatt;
                synchronized (queueLock) {
                    descriptorQueue.clear();
                    descriptorPhase = 0;
                    initIndex = 0;
                    initStarted = false;
                    initDone = false;
                }
                if (Build.VERSION.SDK_INT >= 21) {
                    log("requesting MTU 247");
                    gatt.requestMtu(247);
                } else {
                    gatt.discoverServices();
                }
            } else if (newState == BluetoothProfile.STATE_DISCONNECTED) {
                writeNeutralState();
                currentGatt = null;
                cmdCharacteristic = null;
                synchronized (queueLock) {
                    descriptorQueue.clear();
                    descriptorPhase = 0;
                    initIndex = 0;
                    initStarted = false;
                    initDone = false;
                }
                try {
                    gatt.close();
                } catch (Throwable ignored) {
                }
                if (running) {
                    sleep(2000);
                    connect(context, adapter);
                }
            }
        }

        public void onMtuChanged(BluetoothGatt gatt, int mtu, int status) {
            log("MTU changed mtu=" + mtu + " status=" + status);
            gatt.discoverServices();
        }

        public void onServicesDiscovered(BluetoothGatt gatt, int status) {
            log("services discovered status=" + status);
            List<BluetoothGattService> services = gatt.getServices();
            for (BluetoothGattService service : services) {
                log("service " + service.getUuid());
                for (BluetoothGattCharacteristic ch : service.getCharacteristics()) {
                    log("  char " + ch.getUuid() + " props=" + props(ch.getProperties()));
                }
            }

            BluetoothGattService main = gatt.getService(SERVICE_MAIN);
            if (main == null) {
                log("main service not found: " + SERVICE_MAIN);
                return;
            }

            BluetoothGattCharacteristic input = main.getCharacteristic(CHAR_INPUT);
            BluetoothGattCharacteristic ack = main.getCharacteristic(CHAR_ACK);
            cmdCharacteristic = main.getCharacteristic(CHAR_CMD);
            if (input == null) {
                log("input characteristic not found: " + CHAR_INPUT);
                return;
            }
            if (ack == null) {
                log("ack characteristic not found: " + CHAR_ACK);
                return;
            }
            if (cmdCharacteristic == null) {
                log("command characteristic not found: " + CHAR_CMD);
                return;
            }

            synchronized (queueLock) {
                descriptorQueue.clear();
                descriptorPhase = 0;
                initIndex = 0;
                initStarted = false;
                initDone = false;
                queueNotifyLocked(gatt, ack, "ack");
                writeNextDescriptorLocked(gatt);
            }
        }

        public void onDescriptorWrite(BluetoothGatt gatt, BluetoothGattDescriptor descriptor, int status) {
            log("descriptor write " + descriptor.getCharacteristic().getUuid() + " status=" + status);
            synchronized (queueLock) {
                writeNextDescriptorLocked(gatt);
            }
        }

        public void onCharacteristicChanged(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic) {
            handleNotify(gatt, characteristic.getUuid(), characteristic.getValue());
        }

        public void onCharacteristicChanged(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic, byte[] value) {
            handleNotify(gatt, characteristic.getUuid(), value);
        }

        public void onCharacteristicWrite(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic, int status) {
            log("characteristic write " + characteristic.getUuid() + " status=" + status);
        }
    }

    @SuppressWarnings("deprecation")
    private static void writeNextDescriptorLocked(BluetoothGatt gatt) {
        BluetoothGattDescriptor desc = descriptorQueue.poll();
        if (desc == null) {
            if (descriptorPhase == 0 && !initStarted) {
                startBleInitLocked(gatt);
                return;
            }
            if (descriptorPhase == 1) {
                descriptorPhase = 2;
                log("post-init notification setup complete");
                return;
            }
            log("notification setup complete");
            return;
        }
        byte[] value = BluetoothGattDescriptor.ENABLE_NOTIFICATION_VALUE;
        if (Build.VERSION.SDK_INT >= 33) {
            int result = gatt.writeDescriptor(desc, value);
            log("write CCCD new api " + desc.getCharacteristic().getUuid() + " result=" + result);
        } else {
            desc.setValue(value);
            boolean result = gatt.writeDescriptor(desc);
            log("write CCCD old api " + desc.getCharacteristic().getUuid() + " result=" + result);
        }
    }

    private static void queueNotifyLocked(BluetoothGatt gatt, BluetoothGattCharacteristic ch, String label) {
        if (ch == null) {
            log("enable notify " + label + " skipped, characteristic missing");
            return;
        }
        boolean ok = gatt.setCharacteristicNotification(ch, true);
        BluetoothGattDescriptor desc = ch.getDescriptor(CLIENT_CONFIG);
        log("enable notify " + label + " " + ch.getUuid() + " setNotification=" + ok + " cccd=" + (desc != null));
        if (desc != null) {
            descriptorQueue.add(desc);
        }
    }

    private static void startBleInitLocked(BluetoothGatt gatt) {
        BluetoothGattCharacteristic cmd = cmdCharacteristic;
        if (cmd == null) {
            log("BLE init skipped, command characteristic is missing");
            return;
        }
        initStarted = true;
        initDone = false;
        initIndex = 0;
        log("BLE init starting, waiting for ACKs on " + CHAR_ACK);
        sendCurrentInitCommandLocked(gatt);
    }

    private static void sendCurrentInitCommandLocked(BluetoothGatt gatt) {
        BluetoothGattCharacteristic cmd = cmdCharacteristic;
        if (cmd == null) {
            log("BLE init stopped, command characteristic disappeared");
            return;
        }
        if (initIndex >= INIT_COMMANDS.length) {
            initDone = true;
            log("BLE init complete; subscribing input/status streams");
            subscribePostInitNotificationsLocked(gatt);
            return;
        }
        String name = initIndex < INIT_NAMES.length ? INIT_NAMES[initIndex] : "CMD_" + initIndex;
        byte[] data = INIT_COMMANDS[initIndex];
        writeCharacteristicNoResponse(gatt, cmd, data, "BLE init send " + initIndex + "/" + INIT_COMMANDS.length + " " + name);
    }

    private static void advanceBleInitFromAck(BluetoothGatt gatt) {
        synchronized (queueLock) {
            if (!initStarted || initDone) {
                return;
            }
            initIndex++;
            sendCurrentInitCommandLocked(gatt);
        }
    }

    private static void subscribePostInitNotificationsLocked(BluetoothGatt gatt) {
        BluetoothGattService main = gatt.getService(SERVICE_MAIN);
        if (main == null) {
            log("post-init subscribe skipped, main service missing");
            return;
        }
        descriptorQueue.clear();
        descriptorPhase = 1;
        queueNotifyLocked(gatt, main.getCharacteristic(CHAR_INPUT), "input");
        queueNotifyLocked(gatt, main.getCharacteristic(CHAR_TELEMETRY), "telemetry");
        queueNotifyLocked(gatt, main.getCharacteristic(CHAR_NOTIFY_506D), "notify-506d");
        queueNotifyLocked(gatt, main.getCharacteristic(CHAR_NOTIFY_D3BD), "notify-d3bd");
        queueNotifyLocked(gatt, main.getCharacteristic(CHAR_NOTIFY_FDE), "notify-fde");
        writeNextDescriptorLocked(gatt);
    }

    private static void handleNotify(BluetoothGatt gatt, UUID uuid, byte[] value) {
        if (value == null) {
            return;
        }
        if (CHAR_ACK.equals(uuid)) {
            appendLine(RAW_FILE, "A " + now() + " " + hex(value, value.length));
            log("ack n=" + value.length + " initIndex=" + initIndex + " data=" + hex(value, Math.min(value.length, 32)));
            advanceBleInitFromAck(gatt);
        } else if (CHAR_INPUT.equals(uuid)) {
            handleInput(value);
        } else if (CHAR_TELEMETRY.equals(uuid)) {
            appendLine(RAW_FILE, "T " + now() + " " + hex(value, value.length));
        } else if (CHAR_NOTIFY_506D.equals(uuid) || CHAR_NOTIFY_D3BD.equals(uuid) || CHAR_NOTIFY_FDE.equals(uuid)) {
            appendLine(RAW_FILE, "N " + now() + " " + uuid + " " + hex(value, value.length));
        } else {
            appendLine(RAW_FILE, "? " + now() + " " + uuid + " " + hex(value, value.length));
        }
    }

    private static void handleInput(byte[] value) {
        notifyCount++;
        appendLine(RAW_FILE, "I " + now() + " " + hex(value, value.length));

        int rawB2 = value.length > 2 ? u(value[2]) : 0;
        int rawB3 = value.length > 3 ? u(value[3]) : 0;
        int rawB4 = value.length > 4 ? u(value[4]) : 0;
        int b5 = rawB2;
        int b6 = rawB3;
        int b7 = rawB4 & 0x1f;
        int b8 = 0;

        // Keep the Pro2 BLE button bytes in the state file. The USB responder
        // maps them into Steam's wired Switch 2 state packet layout.
        // Keep only the five known byte4 controls; the remaining bits are padding.
        int lx = value.length >= 8 ? unpack12X(value, 5) : 2048;
        int ly = value.length >= 8 ? unpack12Y(value, 5) : 2048;
        int rx = value.length >= 11 ? unpack12X(value, 8) : 2048;
        int ry = value.length >= 11 ? unpack12Y(value, 8) : 2048;

        if (!calibrated && b5 == 0 && b6 == 0 && b7 == 0) {
            sumLx += lx;
            sumLy += ly;
            sumRx += rx;
            sumRy += ry;
            calibrationCount++;
            if (calibrationCount >= 20) {
                centerLx = (int) (sumLx / calibrationCount);
                centerLy = (int) (sumLy / calibrationCount);
                centerRx = (int) (sumRx / calibrationCount);
                centerRy = (int) (sumRy / calibrationCount);
                calibrated = true;
                log("auto center lx=" + centerLx + " ly=" + centerLy +
                        " rx=" + centerRx + " ry=" + centerRy);
            }
        }

        int outLx = calibrated ? recenter(lx, centerLx) : 2048;
        int outLy = calibrated ? recenter(ly, centerLy) : 2048;
        int outRx = calibrated ? recenter(rx, centerRx) : 2048;
        int outRy = calibrated ? recenter(ry, centerRy) : 2048;

        if (writeState) {
            writeState(b5, b6, b7, b8, outLx, outLy, outRx, outRy);
        }
        logButtonTransition(rawB2, rawB3, rawB4, b5, b6, b7, b8);

        long nowMs = System.currentTimeMillis();
        if (lastInput == null || nowMs - lastSummaryMs >= 250) {
            String delta = lastInput == null ? "first" : diff(lastInput, value);
            log("input n=" + value.length + " count=" + notifyCount +
                    " rawB=" + hexByte(rawB2) + "," + hexByte(rawB3) + "," + hexByte(rawB4) +
                    " mappedB=" + hexByte(b5) + "," + hexByte(b6) + "," + hexByte(b7) +
                    " raw=" + lx + "," + ly + "," + rx + "," + ry +
                    " out=" + outLx + "," + outLy + "," + outRx + "," + outRy +
                    " delta=" + delta);
            lastSummaryMs = nowMs;
            lastInput = copy(value);
        }
    }

    private static void logButtonTransition(int rawB2, int rawB3, int rawB4,
                                            int b5, int b6, int b7, int b8) {
        if (rawB2 == lastRawB2 && rawB3 == lastRawB3 && rawB4 == lastRawB4) {
            return;
        }

        lastRawB2 = rawB2;
        lastRawB3 = rawB3;
        lastRawB4 = rawB4;

        String text = "buttons rawB=" + hexByte(rawB2) + "," + hexByte(rawB3) + "," + hexByte(rawB4) +
                " known=" + knownButtonNames(rawB2, rawB3, rawB4) +
                " rawB4Extra=" + hexByte(rawB4 & ~0x03) +
                " mappedB=" + hexByte(b5) + "," + hexByte(b6) + "," + hexByte(b7) + "," + hexByte(b8);
        log(text);
        appendLine(BUTTON_FILE, now() + " " + text);
    }

    private static String knownButtonNames(int rawB2, int rawB3, int rawB4) {
        StringBuilder out = new StringBuilder();
        appendButtonName(out, rawB2, 0x01, "B");
        appendButtonName(out, rawB2, 0x02, "A");
        appendButtonName(out, rawB2, 0x04, "Y");
        appendButtonName(out, rawB2, 0x08, "X");
        appendButtonName(out, rawB2, 0x10, "R");
        appendButtonName(out, rawB2, 0x20, "ZR");
        appendButtonName(out, rawB2, 0x40, "Plus");
        appendButtonName(out, rawB2, 0x80, "RStick");
        appendButtonName(out, rawB3, 0x01, "DDown");
        appendButtonName(out, rawB3, 0x02, "DRight");
        appendButtonName(out, rawB3, 0x04, "DLeft");
        appendButtonName(out, rawB3, 0x08, "DUp");
        appendButtonName(out, rawB3, 0x10, "L");
        appendButtonName(out, rawB3, 0x20, "ZL");
        appendButtonName(out, rawB3, 0x40, "Minus");
        appendButtonName(out, rawB3, 0x80, "LStick");
        appendButtonName(out, rawB4, 0x01, "Home");
        appendButtonName(out, rawB4, 0x02, "Capture");
        appendButtonName(out, rawB4, 0x04, "GR");
        appendButtonName(out, rawB4, 0x08, "GL");
        appendButtonName(out, rawB4, 0x10, "C");
        return out.length() == 0 ? "neutral-or-extra" : out.toString();
    }

    private static void appendButtonName(StringBuilder out, int bits, int mask, String name) {
        if ((bits & mask) == 0) {
            return;
        }
        if (out.length() > 0) {
            out.append('+');
        }
        out.append(name);
    }

    private static int recenter(int value, int center) {
        int out = clamp12(2048 + value - center);
        int delta = out - 2048;
        if (delta < 0) {
            delta = -delta;
        }
        return delta <= OUTPUT_DEADZONE ? 2048 : out;
    }

    private static void writeNeutralState() {
        if (writeState) {
            writeState(0, 0, 0, 0, 2048, 2048, 2048, 2048);
        }
    }

    private static void writeState(int b5, int b6, int b7, int b8, int lx, int ly, int rx, int ry) {
        try {
            String text = String.format(Locale.US,
                    "b5=0x%02x b6=0x%02x b7=0x%02x b8=0x%02x lx=%d ly=%d rx=%d ry=%d\n",
                    b5 & 0xff, b6 & 0xff, b7 & 0xff, b8 & 0xff,
                    clamp12(lx), clamp12(ly), clamp12(rx), clamp12(ry));
            FileOutputStream out = new FileOutputStream(stateFile, false);
            out.write(text.getBytes("US-ASCII"));
            out.close();
        } catch (Throwable t) {
            log("state write failed: " + t);
        }
    }

    private static int unpack12X(byte[] value, int off) {
        return clamp12(u(value[off]) | ((u(value[off + 1]) & 0x0f) << 8));
    }

    private static int unpack12Y(byte[] value, int off) {
        return clamp12(((u(value[off + 1]) >> 4) & 0x0f) | (u(value[off + 2]) << 4));
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

    private static byte[] copy(byte[] value) {
        byte[] out = new byte[value.length];
        System.arraycopy(value, 0, out, 0, value.length);
        return out;
    }

    private static String diff(byte[] oldValue, byte[] newValue) {
        StringBuilder out = new StringBuilder();
        int n = Math.min(oldValue.length, newValue.length);
        for (int i = 0; i < n; i++) {
            if (oldValue[i] != newValue[i]) {
                if (out.length() > 0) {
                    out.append(' ');
                }
                out.append(i).append(':').append(hexByte(u(oldValue[i]))).append('>').append(hexByte(u(newValue[i])));
            }
        }
        if (oldValue.length != newValue.length) {
            if (out.length() > 0) {
                out.append(' ');
            }
            out.append("len:").append(oldValue.length).append('>').append(newValue.length);
        }
        return out.length() == 0 ? "none" : out.toString();
    }

    private static String readText(File file, int maxBytes) throws Exception {
        FileInputStream in = new FileInputStream(file);
        byte[] buf = new byte[(int) Math.min(file.length(), maxBytes)];
        int n = in.read(buf);
        in.close();
        if (n <= 0) {
            return "";
        }
        return new String(buf, 0, n, "US-ASCII");
    }

    private static byte[] hexBytes(String text) {
        String clean = text.replaceAll("[^0-9A-Fa-f]", "");
        if ((clean.length() & 1) != 0) {
            throw new IllegalArgumentException("odd hex length: " + text);
        }
        byte[] out = new byte[clean.length() / 2];
        for (int i = 0; i < out.length; i++) {
            out[i] = (byte) Integer.parseInt(clean.substring(i * 2, i * 2 + 2), 16);
        }
        return out;
    }

    private static String props(int properties) {
        StringBuilder out = new StringBuilder();
        appendProp(out, properties, BluetoothGattCharacteristic.PROPERTY_READ, "Read");
        appendProp(out, properties, BluetoothGattCharacteristic.PROPERTY_WRITE, "Write");
        appendProp(out, properties, BluetoothGattCharacteristic.PROPERTY_WRITE_NO_RESPONSE, "WriteNoResp");
        appendProp(out, properties, BluetoothGattCharacteristic.PROPERTY_NOTIFY, "Notify");
        appendProp(out, properties, BluetoothGattCharacteristic.PROPERTY_INDICATE, "Indicate");
        return out.length() == 0 ? "0x" + Integer.toHexString(properties) : out.toString();
    }

    private static void appendProp(StringBuilder out, int properties, int mask, String name) {
        if ((properties & mask) == 0) {
            return;
        }
        if (out.length() > 0) {
            out.append(',');
        }
        out.append(name);
    }

    private static int u(byte value) {
        return value & 0xff;
    }

    private static String hexByte(int value) {
        char[] chars = "0123456789ABCDEF".toCharArray();
        return "" + chars[(value >> 4) & 0x0f] + chars[value & 0x0f];
    }

    private static String hex(byte[] data, int n) {
        StringBuilder out = new StringBuilder();
        int limit = Math.min(data.length, n);
        for (int i = 0; i < limit; i++) {
            out.append(hexByte(u(data[i])));
        }
        return out.toString();
    }

    private static String now() {
        return new SimpleDateFormat("HH:mm:ss.SSS", Locale.US).format(new Date());
    }

    private static void truncate(File file) {
        try {
            FileOutputStream out = new FileOutputStream(file, false);
            out.close();
        } catch (Throwable ignored) {
        }
    }

    private static void log(String msg) {
        String line = now() + " " + msg;
        System.out.println(line);
        appendLine(LOG_FILE, line);
    }

    private static void appendLine(File file, String line) {
        try {
            FileOutputStream out = new FileOutputStream(file, true);
            out.write(line.getBytes("UTF-8"));
            out.write('\n');
            out.close();
        } catch (Throwable ignored) {
        }
    }

    private static void sleep(long ms) {
        try {
            Thread.sleep(ms);
        } catch (InterruptedException ignored) {
        }
    }
}
