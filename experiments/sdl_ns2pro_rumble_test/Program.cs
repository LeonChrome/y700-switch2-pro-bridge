using System.Runtime.InteropServices;

var options = Args.Parse(args);
string? sdlPath = ResolveSdl3(options.Sdl3Path);

if (string.IsNullOrWhiteSpace(sdlPath) || !File.Exists(sdlPath))
{
    Console.WriteLine("[SDL_NS2PRO] blocked: SDL3.dll not found. Pass --sdl3 <path> or set SDL3_DLL.");
    return 2;
}

IntPtr sdlHandle = NativeLibrary.Load(sdlPath);
NativeLibrary.SetDllImportResolver(typeof(Sdl).Assembly, (libraryName, assembly, searchPath) =>
    libraryName == "SDL3" ? sdlHandle : IntPtr.Zero);

Console.WriteLine($"[SDL_NS2PRO] sdl3={sdlPath}");

try
{
    if (!Sdl.Init(Sdl.InitGamepad | Sdl.InitHaptic))
    {
        Console.WriteLine($"[SDL_NS2PRO] blocked: SDL_Init failed error={Sdl.Error}");
        return 3;
    }

    IntPtr idsPtr = Sdl.GetGamepads(out int count);
    Console.WriteLine($"[SDL_NS2PRO] gamepads={count}");
    if (count <= 0 || idsPtr == IntPtr.Zero)
    {
        Console.WriteLine("[SDL_NS2PRO] blocked: no SDL gamepad detected");
        return 4;
    }

    bool anyRumble = false;
    for (int i = 0; i < count; i++)
    {
        int instanceId = Marshal.ReadInt32(idsPtr, i * sizeof(int));
        IntPtr gamepad = Sdl.OpenGamepad(instanceId);
        if (gamepad == IntPtr.Zero)
        {
            Console.WriteLine($"[SDL_NS2PRO] open_failed instance={instanceId} error={Sdl.Error}");
            continue;
        }

        string name = NativeText.FromUtf8(Sdl.GetGamepadName(gamepad));
        Console.WriteLine($"[SDL_NS2PRO] device={instanceId} name=\"{name}\"");

        bool looksLikeNs2Pro =
            name.Contains("Switch 2 Pro", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Nintendo", StringComparison.OrdinalIgnoreCase);
        bool shouldRumble = options.All || looksLikeNs2Pro;
        if (shouldRumble)
        {
            bool result = Sdl.RumbleGamepad(gamepad, options.Low, options.High, options.DurationMs);
            Console.WriteLine($"[SDL_NS2PRO] rumble_result={result.ToString().ToLowerInvariant()} low={options.Low} high={options.High} duration_ms={options.DurationMs} error={Sdl.Error}");
            anyRumble |= result;

            bool triggerResult = Sdl.RumbleGamepadTriggers(gamepad, options.High, options.High, options.DurationMs);
            Console.WriteLine($"[SDL_NS2PRO] trigger_rumble_result={triggerResult.ToString().ToLowerInvariant()} left={options.High} right={options.High} duration_ms={options.DurationMs} error={Sdl.Error}");
            anyRumble |= triggerResult;
        }
        else
        {
            Console.WriteLine("[SDL_NS2PRO] skipped: name does not look like ns2pro; pass --all to rumble every SDL gamepad");
        }

        Sdl.CloseGamepad(gamepad);
    }

    Sdl.Free(idsPtr);
    Console.WriteLine($"[SDL_NS2PRO] any_rumble={anyRumble.ToString().ToLowerInvariant()}");
    return anyRumble ? 0 : 5;
}
catch (EntryPointNotFoundException ex)
{
    Console.WriteLine($"[SDL_NS2PRO] blocked: SDL3 entrypoint mismatch {ex.TargetSite?.Name}");
    return 6;
}
finally
{
    Sdl.Quit();
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
            All = args.Any(a => string.Equals(a, "--all", StringComparison.OrdinalIgnoreCase))
        };
    }
}

static partial class Sdl
{
    public const uint InitHaptic = 0x00001000;
    public const uint InitGamepad = 0x00002000;

    [DllImport("SDL3", EntryPoint = "SDL_Init", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool Init(uint flags);

    [DllImport("SDL3", EntryPoint = "SDL_Quit", CallingConvention = CallingConvention.Cdecl)]
    public static extern void Quit();

    [DllImport("SDL3", EntryPoint = "SDL_GetGamepads", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr GetGamepads(out int count);

    [DllImport("SDL3", EntryPoint = "SDL_OpenGamepad", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr OpenGamepad(int instanceId);

    [DllImport("SDL3", EntryPoint = "SDL_GetGamepadName", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr GetGamepadName(IntPtr gamepad);

    [DllImport("SDL3", EntryPoint = "SDL_RumbleGamepad", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool RumbleGamepad(IntPtr gamepad, ushort lowFrequencyRumble, ushort highFrequencyRumble, uint durationMs);

    [DllImport("SDL3", EntryPoint = "SDL_RumbleGamepadTriggers", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool RumbleGamepadTriggers(IntPtr gamepad, ushort leftRumble, ushort rightRumble, uint durationMs);

    [DllImport("SDL3", EntryPoint = "SDL_CloseGamepad", CallingConvention = CallingConvention.Cdecl)]
    public static extern void CloseGamepad(IntPtr gamepad);

    [DllImport("SDL3", EntryPoint = "SDL_free", CallingConvention = CallingConvention.Cdecl)]
    public static extern void Free(IntPtr mem);

    [DllImport("SDL3", EntryPoint = "SDL_GetError", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr GetErrorPtr();

    public static string Error => NativeText.FromUtf8(GetErrorPtr());
}
