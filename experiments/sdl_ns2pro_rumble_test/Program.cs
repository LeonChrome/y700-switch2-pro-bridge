using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;

var options = Args.Parse(args);
string? sdlPath = ResolveSdl3(options.Sdl3Path);

if (string.IsNullOrWhiteSpace(sdlPath) || !File.Exists(sdlPath))
{
    Console.WriteLine("[SDL] blocked=SDL3.dll not found. Pass --sdl3 <path> or set SDL3_DLL.");
    return 2;
}

IntPtr sdlHandle = NativeLibrary.Load(sdlPath);
NativeLibrary.SetDllImportResolver(typeof(Sdl).Assembly, (libraryName, assembly, searchPath) =>
    libraryName == "SDL3" ? sdlHandle : IntPtr.Zero);

Console.WriteLine($"[SDL] dll={sdlPath}");

try
{
    int linkedVersion = Sdl.GetVersion();
    Console.WriteLine($"[SDL] version={VersionToText(linkedVersion)} raw={linkedVersion}");
    Console.WriteLine($"[SDL] revision={NativeText.FromUtf8(Sdl.GetRevision())}");

    SetHint("SDL_JOYSTICK_HIDAPI", "1");
    SetHint("SDL_JOYSTICK_HIDAPI_NINTENDO_SWITCH", "1");
    SetHint("SDL_JOYSTICK_HIDAPI_SWITCH", "1");
    SetHint("SDL_JOYSTICK_HIDAPI_JOY_CONS", "1");
    SetHint("SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS", "1");
    if (!string.IsNullOrWhiteSpace(options.GameControllerType))
    {
        SetHint("SDL_GAMECONTROLLERTYPE", options.GameControllerType);
    }

    if (!Sdl.Init(Sdl.InitJoystick | Sdl.InitGamepad | Sdl.InitHaptic))
    {
        Console.WriteLine($"[SDL] blocked=SDL_Init failed error={Sdl.Error}");
        return 3;
    }

    Sdl.UpdateJoysticks();

    bool anyRumble = false;
    bool anyEffect = false;
    int joystickCount = EnumerateAndExerciseJoysticks(options, ref anyRumble, ref anyEffect);
    int gamepadCount = EnumerateAndExerciseGamepads(options, ref anyRumble, ref anyEffect);

    Console.WriteLine($"[SDL] any_rumble={anyRumble.ToString().ToLowerInvariant()}");
    Console.WriteLine($"[SDL] any_hd_effect={anyEffect.ToString().ToLowerInvariant()}");
    if (joystickCount == 0 && gamepadCount == 0)
    {
        Console.WriteLine("[SDL] blocked=no SDL joystick or gamepad detected");
        return 4;
    }
    return anyRumble || anyEffect ? 0 : 5;
}
catch (EntryPointNotFoundException ex)
{
    Console.WriteLine($"[SDL] blocked=SDL3 entrypoint mismatch {ex.TargetSite?.Name}");
    return 6;
}
finally
{
    Sdl.Quit();
}

static int EnumerateAndExerciseJoysticks(Args options, ref bool anyRumble, ref bool anyEffect)
{
    IntPtr idsPtr = Sdl.GetJoysticks(out int count);
    Console.WriteLine($"[SDL] joystick_count={count}");
    if (count <= 0 || idsPtr == IntPtr.Zero)
    {
        return 0;
    }

    try
    {
        for (int i = 0; i < count; i++)
        {
            int instanceId = Marshal.ReadInt32(idsPtr, i * sizeof(int));
            string nameForId = TryGetText(() => Sdl.GetJoystickNameForID(instanceId), "not_available");
            string pathForId = TryGetText(() => Sdl.GetJoystickPathForID(instanceId), "not_available");
            bool? isGamepad = TryGetBool(() => Sdl.IsGamepad(instanceId));

            Console.WriteLine($"[SDL] joystick_id={instanceId}");
            Console.WriteLine($"[SDL] joystick_name_for_id={nameForId}");
            Console.WriteLine($"[SDL] joystick_path_for_id={pathForId}");
            Console.WriteLine($"[SDL] joystick_is_gamepad={NullableBool(isGamepad)}");

            IntPtr joystick = Sdl.OpenJoystick(instanceId);
            if (joystick == IntPtr.Zero)
            {
                Console.WriteLine($"[SDL] joystick_open_failed instance={instanceId} error={Sdl.Error}");
                continue;
            }

            try
            {
                string name = TryGetText(() => Sdl.GetJoystickName(joystick), nameForId);
                string path = TryGetText(() => Sdl.GetJoystickPath(joystick), pathForId);
                (string vendorId, string productId) = ExtractVidPid(path);
                string serial = TryGetText(() => Sdl.GetJoystickSerial(joystick), "not_available");
                string firmware = TryGetNumber(() => Sdl.GetJoystickFirmwareVersion(joystick), "not_available");
                int axes = TryGetInt(() => Sdl.GetNumJoystickAxes(joystick), -1);
                int buttons = TryGetInt(() => Sdl.GetNumJoystickButtons(joystick), -1);

                Console.WriteLine($"[SDL] name={name}");
                Console.WriteLine($"[SDL] path={path}");
                Console.WriteLine($"[SDL] vendor={vendorId}");
                Console.WriteLine($"[SDL] product={productId}");
                Console.WriteLine($"[SDL] nintendo_path={LooksNintendoPath(name, path, vendorId).ToString().ToLowerInvariant()}");
                Console.WriteLine($"[SDL] switch_path={LooksSwitchPath(name, path, productId).ToString().ToLowerInvariant()}");
                Console.WriteLine($"[SDL] serial={serial}");
                Console.WriteLine($"[SDL] firmware_version={firmware}");
                Console.WriteLine($"[SDL] joystick_axes={axes}");
                Console.WriteLine($"[SDL] joystick_buttons={buttons}");

                if (!ShouldExercise(options, name, path))
                {
                    Console.WriteLine("[SDL] joystick_skipped=name/path does not look like ns2pro; pass --all to rumble every SDL joystick");
                    continue;
                }

                bool? rumbleSupported = TryGetBool(() => Sdl.JoystickHasRumble(joystick));
                bool? triggerRumbleSupported = TryGetBool(() => Sdl.JoystickHasRumbleTriggers(joystick));
                Console.WriteLine($"[SDL] rumble_supported={NullableBool(rumbleSupported)}");
                Console.WriteLine($"[SDL] trigger_rumble_supported={NullableBool(triggerRumbleSupported)}");

                bool rumble = Sdl.RumbleJoystick(joystick, options.Low, options.High, options.DurationMs);
                Console.WriteLine($"[SDL] joystick_rumble_result={rumble.ToString().ToLowerInvariant()} low={options.Low} high={options.High} duration_ms={options.DurationMs} error={Sdl.Error}");
                PulseJoystickUpdates(options.DurationMs);
                anyRumble |= rumble;

                bool trigger = Sdl.RumbleJoystickTriggers(joystick, options.High, options.High, options.DurationMs);
                Console.WriteLine($"[SDL] joystick_trigger_rumble_result={trigger.ToString().ToLowerInvariant()} left={options.High} right={options.High} duration_ms={options.DurationMs} error={Sdl.Error}");
                PulseJoystickUpdates(options.DurationMs);
                anyRumble |= trigger;

                if (!options.NoEffect)
                {
                    foreach ((string label, byte[] payload) in BuildEffectPayloads(options))
                    {
                        try
                        {
                            bool effect = Sdl.SendJoystickEffect(joystick, payload, payload.Length);
                            Console.WriteLine($"[SDL] send_effect_result={effect.ToString().ToLowerInvariant()} api=joystick label={label} size={payload.Length} hex={ToHex(payload)} error={Sdl.Error}");
                            PulseJoystickUpdates(250);
                            anyEffect |= effect;
                        }
                        catch (EntryPointNotFoundException)
                        {
                            Console.WriteLine("[SDL] send_effect_blocked=SDL_SendJoystickEffect entrypoint not found in this SDL3.dll");
                            break;
                        }
                    }
                }
            }
            finally
            {
                Sdl.CloseJoystick(joystick);
            }
        }
    }
    finally
    {
        Sdl.Free(idsPtr);
    }

    return count;
}

static int EnumerateAndExerciseGamepads(Args options, ref bool anyRumble, ref bool anyEffect)
{
    IntPtr idsPtr = Sdl.GetGamepads(out int count);
    Console.WriteLine($"[SDL] gamepad_count={count}");
    if (count <= 0 || idsPtr == IntPtr.Zero)
    {
        return 0;
    }

    try
    {
        for (int i = 0; i < count; i++)
        {
            int instanceId = Marshal.ReadInt32(idsPtr, i * sizeof(int));
            IntPtr gamepad = Sdl.OpenGamepad(instanceId);
            if (gamepad == IntPtr.Zero)
            {
                Console.WriteLine($"[SDL] gamepad_open_failed instance={instanceId} error={Sdl.Error}");
                continue;
            }

            try
            {
                string name = NativeText.FromUtf8(Sdl.GetGamepadName(gamepad));
                string path = TryGetText(() => Sdl.GetGamepadPath(gamepad), "not_available");
                (string vendorId, string productId) = ExtractVidPid(path);
                string serial = TryGetText(() => Sdl.GetGamepadSerial(gamepad), "not_available");
                string firmware = TryGetNumber(() => Sdl.GetGamepadFirmwareVersion(gamepad), "not_available");
                Console.WriteLine($"[SDL] device={instanceId}");
                Console.WriteLine($"[SDL] name={name}");
                Console.WriteLine($"[SDL] path={path}");
                Console.WriteLine($"[SDL] vendor={vendorId}");
                Console.WriteLine($"[SDL] product={productId}");
                Console.WriteLine($"[SDL] nintendo_path={LooksNintendoPath(name, path, vendorId).ToString().ToLowerInvariant()}");
                Console.WriteLine($"[SDL] switch_path={LooksSwitchPath(name, path, productId).ToString().ToLowerInvariant()}");
                Console.WriteLine($"[SDL] serial={serial}");
                Console.WriteLine($"[SDL] firmware_version={firmware}");

                if (!ShouldExercise(options, name, path))
                {
                    Console.WriteLine("[SDL] gamepad_skipped=name/path does not look like ns2pro; pass --all to rumble every SDL gamepad");
                    continue;
                }

                bool? rumbleSupported = TryGetBool(() => Sdl.GamepadHasRumble(gamepad));
                bool? triggerRumbleSupported = TryGetBool(() => Sdl.GamepadHasRumbleTriggers(gamepad));
                Console.WriteLine($"[SDL] rumble_supported={NullableBool(rumbleSupported)}");
                Console.WriteLine($"[SDL] trigger_rumble_supported={NullableBool(triggerRumbleSupported)}");

                bool result = Sdl.RumbleGamepad(gamepad, options.Low, options.High, options.DurationMs);
                Console.WriteLine($"[SDL] rumble_result={result.ToString().ToLowerInvariant()} low={options.Low} high={options.High} duration_ms={options.DurationMs} error={Sdl.Error}");
                PulseJoystickUpdates(options.DurationMs);
                anyRumble |= result;

                bool triggerResult = Sdl.RumbleGamepadTriggers(gamepad, options.High, options.High, options.DurationMs);
                Console.WriteLine($"[SDL] trigger_rumble_result={triggerResult.ToString().ToLowerInvariant()} left={options.High} right={options.High} duration_ms={options.DurationMs} error={Sdl.Error}");
                PulseJoystickUpdates(options.DurationMs);
                anyRumble |= triggerResult;

                if (!options.NoEffect)
                {
                    foreach ((string label, byte[] payload) in BuildEffectPayloads(options))
                    {
                        try
                        {
                            bool effectResult = Sdl.SendGamepadEffect(gamepad, payload, payload.Length);
                            Console.WriteLine($"[SDL] send_effect_result={effectResult.ToString().ToLowerInvariant()} api=gamepad label={label} size={payload.Length} hex={ToHex(payload)} error={Sdl.Error}");
                            PulseJoystickUpdates(250);
                            anyEffect |= effectResult;
                        }
                        catch (EntryPointNotFoundException)
                        {
                            Console.WriteLine("[SDL] send_effect_blocked=SDL_SendGamepadEffect entrypoint not found in this SDL3.dll");
                            break;
                        }
                    }
                }
            }
            finally
            {
                Sdl.CloseGamepad(gamepad);
            }
        }
    }
    finally
    {
        Sdl.Free(idsPtr);
    }

    return count;
}

static string? ResolveSdl3(string? explicitPath)
{
    foreach (string? candidate in new[]
    {
        explicitPath,
        Environment.GetEnvironmentVariable("SDL3_DLL"),
        Path.Combine(AppContext.BaseDirectory, "SDL3.dll"),
        Path.Combine(Environment.CurrentDirectory, "work", "deps", "sdl3", "SDL3.dll")
    })
    {
        if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
        {
            return Path.GetFullPath(candidate);
        }
    }
    return null;
}

static void SetHint(string name, string value)
{
    try
    {
        bool result = Sdl.SetHint(name, value);
        Console.WriteLine($"[SDL] hint_{name}={value} result={result.ToString().ToLowerInvariant()}");
    }
    catch (EntryPointNotFoundException)
    {
        Console.WriteLine($"[SDL] hint_{name}=not_available");
    }
}

static bool ShouldExercise(Args options, string name, string path)
{
    if (options.All)
    {
        return true;
    }

    string text = $"{name} {path}";
    return text.Contains("Switch 2 Pro", StringComparison.OrdinalIgnoreCase) ||
           text.Contains("Nintendo", StringComparison.OrdinalIgnoreCase) ||
           text.Contains("ns2pro", StringComparison.OrdinalIgnoreCase) ||
           text.Contains("057e", StringComparison.OrdinalIgnoreCase) ||
           text.Contains("2069", StringComparison.OrdinalIgnoreCase);
}

static (string VendorId, string ProductId) ExtractVidPid(string path)
{
    string vendor = ExtractHexField(path, "VID_");
    string product = ExtractHexField(path, "PID_");
    return (vendor, product);
}

static string ExtractHexField(string path, string marker)
{
    int index = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
    if (index < 0 || index + marker.Length + 4 > path.Length)
    {
        return "not_available";
    }
    string value = path.Substring(index + marker.Length, 4);
    return value.All(Uri.IsHexDigit) ? value.ToUpperInvariant() : "not_available";
}

static bool LooksNintendoPath(string name, string path, string vendorId) =>
    string.Equals(vendorId, "057E", StringComparison.OrdinalIgnoreCase) ||
    name.Contains("Nintendo", StringComparison.OrdinalIgnoreCase) ||
    path.Contains("VID_057E", StringComparison.OrdinalIgnoreCase);

static bool LooksSwitchPath(string name, string path, string productId) =>
    string.Equals(productId, "2069", StringComparison.OrdinalIgnoreCase) ||
    name.Contains("Switch", StringComparison.OrdinalIgnoreCase) ||
    name.Contains("ns2pro", StringComparison.OrdinalIgnoreCase) ||
    path.Contains("PID_2069", StringComparison.OrdinalIgnoreCase);

static string VersionToText(int version) =>
    $"{version / 1_000_000}.{version / 1_000 % 1_000}.{version % 1_000}";

static string NullableBool(bool? value) =>
    value.HasValue ? value.Value.ToString().ToLowerInvariant() : "not_available";

static bool? TryGetBool(Func<bool> getter)
{
    try
    {
        return getter();
    }
    catch (EntryPointNotFoundException)
    {
        return null;
    }
}

static int TryGetInt(Func<int> getter, int fallback)
{
    try
    {
        return getter();
    }
    catch (EntryPointNotFoundException)
    {
        return fallback;
    }
}

static string TryGetText(Func<IntPtr> getter, string fallback)
{
    try
    {
        string value = NativeText.FromUtf8(getter());
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
    catch (EntryPointNotFoundException)
    {
        return fallback;
    }
}

static string TryGetNumber(Func<ushort> getter, string fallback)
{
    try
    {
        return getter().ToString(CultureInfo.InvariantCulture);
    }
    catch (EntryPointNotFoundException)
    {
        return fallback;
    }
}

static void PulseJoystickUpdates(uint durationMs)
{
    uint elapsed = 0;
    uint step = 50;
    while (elapsed < durationMs)
    {
        Sdl.UpdateJoysticks();
        Thread.Sleep((int)Math.Min(step, durationMs - elapsed));
        elapsed += step;
    }
}

static IEnumerable<(string Label, byte[] Payload)> BuildEffectPayloads(Args options)
{
    if (!string.IsNullOrWhiteSpace(options.EffectHex))
    {
        yield return ("custom", ParseHex(options.EffectHex));
        yield break;
    }

    yield return ("ns2pro_hd_report_02_16_16", BuildNs2ProHdReport(includeReportId: true));
    yield return ("ns2pro_hd_16_16", BuildNs2ProHdReport(includeReportId: false));
    yield return ("switch_pro_output_10", new byte[] { 0x10, 0x00, 0x48, 0x01, 0x60, 0x40, 0x48, 0x01, 0x60, 0x40 });
}

static byte[] BuildNs2ProHdReport(bool includeReportId)
{
    byte[] payload = new byte[includeReportId ? 33 : 32];
    int offset = includeReportId ? 1 : 0;
    if (includeReportId)
    {
        payload[0] = 0x02;
    }
    for (int i = 0; i < 16; i++)
    {
        payload[offset + i] = (byte)(0x20 + i);
        payload[offset + 16 + i] = (byte)(0x80 + i);
    }
    return payload;
}

static byte[] ParseHex(string text)
{
    string compact = new(text.Where(Uri.IsHexDigit).ToArray());
    if (compact.Length % 2 != 0)
    {
        throw new ArgumentException("--effect-hex must contain an even number of hex digits");
    }

    byte[] bytes = new byte[compact.Length / 2];
    for (int i = 0; i < bytes.Length; i++)
    {
        bytes[i] = byte.Parse(compact.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }
    return bytes;
}

static string ToHex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

static class NativeText
{
    public static string FromUtf8(IntPtr ptr) => ptr == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(ptr) ?? "";
}

sealed class Args
{
    public string? Sdl3Path { get; init; }
    public ushort Low { get; init; } = 0xffff;
    public ushort High { get; init; } = 0xffff;
    public uint DurationMs { get; init; } = 800;
    public bool All { get; init; }
    public bool NoEffect { get; init; }
    public string? EffectHex { get; init; }
    public string GameControllerType { get; init; } = "SwitchPro";

    public static Args Parse(string[] args)
    {
        string? GetValue(string name)
        {
            int i = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        static ushort ParseU16(string? value, ushort fallback) =>
            ushort.TryParse(value, out ushort parsed) ? parsed : fallback;
        static uint ParseU32(string? value, uint fallback) =>
            uint.TryParse(value, out uint parsed) ? parsed : fallback;

        return new Args
        {
            Sdl3Path = GetValue("--sdl3"),
            Low = ParseU16(GetValue("--low"), 0xffff),
            High = ParseU16(GetValue("--high"), 0xffff),
            DurationMs = ParseU32(GetValue("--duration-ms"), 800),
            All = args.Any(a => string.Equals(a, "--all", StringComparison.OrdinalIgnoreCase)),
            NoEffect = args.Any(a => string.Equals(a, "--no-effect", StringComparison.OrdinalIgnoreCase)),
            EffectHex = GetValue("--effect-hex"),
            GameControllerType = GetValue("--gamecontroller-type") ?? "SwitchPro"
        };
    }
}

static partial class Sdl
{
    public const uint InitJoystick = 0x00000200;
    public const uint InitHaptic = 0x00001000;
    public const uint InitGamepad = 0x00002000;

    [DllImport("SDL3", EntryPoint = "SDL_SetHint", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SetHint(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    [DllImport("SDL3", EntryPoint = "SDL_GetVersion", CallingConvention = CallingConvention.Cdecl)]
    public static extern int GetVersion();

    [DllImport("SDL3", EntryPoint = "SDL_GetRevision", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr GetRevision();

    [DllImport("SDL3", EntryPoint = "SDL_Init", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool Init(uint flags);

    [DllImport("SDL3", EntryPoint = "SDL_Quit", CallingConvention = CallingConvention.Cdecl)]
    public static extern void Quit();

    [DllImport("SDL3", EntryPoint = "SDL_UpdateJoysticks", CallingConvention = CallingConvention.Cdecl)]
    public static extern void UpdateJoysticks();

    [DllImport("SDL3", EntryPoint = "SDL_GetJoysticks", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr GetJoysticks(out int count);

    [DllImport("SDL3", EntryPoint = "SDL_GetJoystickNameForID", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr GetJoystickNameForID(int instanceId);

    [DllImport("SDL3", EntryPoint = "SDL_GetJoystickPathForID", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr GetJoystickPathForID(int instanceId);

    [DllImport("SDL3", EntryPoint = "SDL_OpenJoystick", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr OpenJoystick(int instanceId);

    [DllImport("SDL3", EntryPoint = "SDL_IsGamepad", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool IsGamepad(int instanceId);

    [DllImport("SDL3", EntryPoint = "SDL_GetJoystickName", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr GetJoystickName(IntPtr joystick);

    [DllImport("SDL3", EntryPoint = "SDL_GetJoystickPath", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr GetJoystickPath(IntPtr joystick);

    [DllImport("SDL3", EntryPoint = "SDL_GetJoystickSerial", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr GetJoystickSerial(IntPtr joystick);

    [DllImport("SDL3", EntryPoint = "SDL_GetJoystickFirmwareVersion", CallingConvention = CallingConvention.Cdecl)]
    public static extern ushort GetJoystickFirmwareVersion(IntPtr joystick);

    [DllImport("SDL3", EntryPoint = "SDL_GetNumJoystickAxes", CallingConvention = CallingConvention.Cdecl)]
    public static extern int GetNumJoystickAxes(IntPtr joystick);

    [DllImport("SDL3", EntryPoint = "SDL_GetNumJoystickButtons", CallingConvention = CallingConvention.Cdecl)]
    public static extern int GetNumJoystickButtons(IntPtr joystick);

    [DllImport("SDL3", EntryPoint = "SDL_JoystickHasRumble", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool JoystickHasRumble(IntPtr joystick);

    [DllImport("SDL3", EntryPoint = "SDL_JoystickHasRumbleTriggers", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool JoystickHasRumbleTriggers(IntPtr joystick);

    [DllImport("SDL3", EntryPoint = "SDL_RumbleJoystick", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool RumbleJoystick(IntPtr joystick, ushort lowFrequencyRumble, ushort highFrequencyRumble, uint durationMs);

    [DllImport("SDL3", EntryPoint = "SDL_RumbleJoystickTriggers", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool RumbleJoystickTriggers(IntPtr joystick, ushort leftRumble, ushort rightRumble, uint durationMs);

    [DllImport("SDL3", EntryPoint = "SDL_SendJoystickEffect", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SendJoystickEffect(IntPtr joystick, [In] byte[] data, int size);

    [DllImport("SDL3", EntryPoint = "SDL_CloseJoystick", CallingConvention = CallingConvention.Cdecl)]
    public static extern void CloseJoystick(IntPtr joystick);

    [DllImport("SDL3", EntryPoint = "SDL_GetGamepads", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr GetGamepads(out int count);

    [DllImport("SDL3", EntryPoint = "SDL_OpenGamepad", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr OpenGamepad(int instanceId);

    [DllImport("SDL3", EntryPoint = "SDL_GetGamepadName", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr GetGamepadName(IntPtr gamepad);

    [DllImport("SDL3", EntryPoint = "SDL_GetGamepadPath", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr GetGamepadPath(IntPtr gamepad);

    [DllImport("SDL3", EntryPoint = "SDL_GetGamepadSerial", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr GetGamepadSerial(IntPtr gamepad);

    [DllImport("SDL3", EntryPoint = "SDL_GetGamepadFirmwareVersion", CallingConvention = CallingConvention.Cdecl)]
    public static extern ushort GetGamepadFirmwareVersion(IntPtr gamepad);

    [DllImport("SDL3", EntryPoint = "SDL_GamepadHasRumble", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool GamepadHasRumble(IntPtr gamepad);

    [DllImport("SDL3", EntryPoint = "SDL_GamepadHasRumbleTriggers", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool GamepadHasRumbleTriggers(IntPtr gamepad);

    [DllImport("SDL3", EntryPoint = "SDL_RumbleGamepad", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool RumbleGamepad(IntPtr gamepad, ushort lowFrequencyRumble, ushort highFrequencyRumble, uint durationMs);

    [DllImport("SDL3", EntryPoint = "SDL_RumbleGamepadTriggers", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool RumbleGamepadTriggers(IntPtr gamepad, ushort leftRumble, ushort rightRumble, uint durationMs);

    [DllImport("SDL3", EntryPoint = "SDL_SendGamepadEffect", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SendGamepadEffect(IntPtr gamepad, [In] byte[] data, int size);

    [DllImport("SDL3", EntryPoint = "SDL_CloseGamepad", CallingConvention = CallingConvention.Cdecl)]
    public static extern void CloseGamepad(IntPtr gamepad);

    [DllImport("SDL3", EntryPoint = "SDL_free", CallingConvention = CallingConvention.Cdecl)]
    public static extern void Free(IntPtr mem);

    [DllImport("SDL3", EntryPoint = "SDL_GetError", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr GetErrorPtr();

    public static string Error => NativeText.FromUtf8(GetErrorPtr());
}
