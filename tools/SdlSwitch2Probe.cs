using System;
using System.Runtime.InteropServices;
using System.Text;

public static class SdlSwitch2Probe
{
    private const uint SDL_INIT_JOYSTICK = 0x00000200;
    private const uint SDL_INIT_GAMEPAD = 0x00002000;

    private delegate void SdlLogOutput(IntPtr userdata, int category, int priority, IntPtr message);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetDllDirectory(string lpPathName);

    [DllImport("SDL3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool SDL_SetHint([MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    [DllImport("SDL3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SDL_SetLogPriorities(int priority);

    [DllImport("SDL3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SDL_SetLogOutputFunction(SdlLogOutput callback, IntPtr userdata);

    [DllImport("SDL3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool SDL_Init(uint flags);

    [DllImport("SDL3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SDL_Quit();

    [DllImport("SDL3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SDL_GetError();

    [DllImport("SDL3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SDL_GetJoysticks(out int count);

    [DllImport("SDL3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SDL_GetGamepads(out int count);

    [DllImport("SDL3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SDL_GetJoystickNameForID(uint instanceId);

    [DllImport("SDL3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SDL_GetGamepadNameForID(uint instanceId);

    [DllImport("SDL3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SDL_free(IntPtr mem);

    private static readonly SdlLogOutput LogCallback = Log;

    public static int Main(string[] args)
    {
        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string steamRoot = args.Length > 0 ? args[0] : PathCombine(programFilesX86, "Steam");
        if (!SetDllDirectory(steamRoot)) {
            Console.WriteLine("SetDllDirectory failed: " + Marshal.GetLastWin32Error());
            return 2;
        }

        SDL_SetLogPriorities(1);
        SDL_SetLogOutputFunction(LogCallback, IntPtr.Zero);
        SetHint("SDL_JOYSTICK_HIDAPI", "1");
        SetHint("SDL_JOYSTICK_HIDAPI_SWITCH2", "1");
        SetHint("SDL_JOYSTICK_HIDAPI_NINTENDO", "1");
        SetHint("SDL_JOYSTICK_HIDAPI_LIBUSB", "1");
        SetHint("SDL_JOYSTICK_HIDAPI_STEAM", "1");
        SetHint("SDL_GAMECONTROLLER_IGNORE_DEVICES", "");
        SetHint("SDL_GAMECONTROLLER_IGNORE_DEVICES_EXCEPT", "");

        bool ok = SDL_Init(SDL_INIT_JOYSTICK | SDL_INIT_GAMEPAD);
        Console.WriteLine("SDL_Init=" + ok + " error=" + Utf8(SDL_GetError()));
        DumpJoysticks();
        DumpGamepads();
        SDL_Quit();
        return ok ? 0 : 1;
    }

    private static void SetHint(string name, string value)
    {
        bool ok = SDL_SetHint(name, value);
        Console.WriteLine("hint " + name + "=" + value + " ok=" + ok);
    }

    private static string PathCombine(string left, string right)
    {
        return System.IO.Path.Combine(left, right);
    }

    private static void DumpJoysticks()
    {
        int count;
        IntPtr ids = SDL_GetJoysticks(out count);
        Console.WriteLine("joystick_count=" + count);
        for (int i = 0; i < count; i++) {
            uint id = (uint)Marshal.ReadInt32(ids, i * 4);
            Console.WriteLine("joystick[" + i + "] id=" + id + " name=" + Utf8(SDL_GetJoystickNameForID(id)));
        }
        if (ids != IntPtr.Zero) {
            SDL_free(ids);
        }
    }

    private static void DumpGamepads()
    {
        int count;
        IntPtr ids = SDL_GetGamepads(out count);
        Console.WriteLine("gamepad_count=" + count);
        for (int i = 0; i < count; i++) {
            uint id = (uint)Marshal.ReadInt32(ids, i * 4);
            Console.WriteLine("gamepad[" + i + "] id=" + id + " name=" + Utf8(SDL_GetGamepadNameForID(id)));
        }
        if (ids != IntPtr.Zero) {
            SDL_free(ids);
        }
    }

    private static void Log(IntPtr userdata, int category, int priority, IntPtr message)
    {
        Console.WriteLine("SDL_LOG category=" + category + " priority=" + priority + " " + Utf8(message));
    }

    private static string Utf8(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero) {
            return "";
        }

        int len = 0;
        while (Marshal.ReadByte(ptr, len) != 0) {
            len++;
        }
        byte[] bytes = new byte[len];
        Marshal.Copy(ptr, bytes, 0, len);
        return Encoding.UTF8.GetString(bytes);
    }
}
