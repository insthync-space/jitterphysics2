using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using JitterDemo.Renderer.OpenGL.Native;

namespace JitterDemo.Renderer.OpenGL;

public struct CreationSettings
{
    public int Width;
    public int Height;
    public string Title;

    public CreationSettings(int width, int height, string title)
    {
        Title = title;
        Width = width;
        Height = height;
    }
}

public class OpenGLVersionNotSupportedException : Exception
{
    public OpenGLVersionNotSupportedException(string msg) : base(msg) { }
}

public class GLFWWindow
{
    private static bool created;

    public IntPtr Handle { get; private set; }
    public double Time { get; private set; }
    public Keyboard Keyboard { get; private set; } = null!;
    public Mouse Mouse { get; private set; } = null!;

    public (int, int) OpenGLVersion { get; private set; }
    public (int, int, int) GLFWVersion { get; private set; }

    private int width;
    private int height;
    private string title = string.Empty;

    private readonly GL.DebugMessageDelegate debugCallback;
    private GLFW.ErrorDelegate errorCallback = null!;

    public GLFWWindow()
    {
        if (created) throw new NotSupportedException("Only one GLFW window is supported.");
        created = true;
        debugCallback = OnGLDebug;
    }

    public int Width
    {
        get => width;
        set { width = value; GLFW.SetWindowSize(Handle, width, height); }
    }

    public int Height
    {
        get => height;
        set { height = value; GLFW.SetWindowSize(Handle, width, height); }
    }

    public string Title
    {
        get => title;
        set { title = value; GLFW.SetWindowTitle(Handle, value); }
    }

    public (int Width, int Height) FramebufferSize
    {
        get
        {
            int w = 0, h = 0;
            GLFW.GetFramebufferSize(Handle, ref w, ref h);
            return (w, h);
        }
    }

    public (float X, float Y) WindowContentScale
    {
        get
        {
            if (Handle == IntPtr.Zero) return (1f, 1f);

            GLFW.GetWindowContentScale(Handle, out float x, out float y);
            return (x > 0f ? x : 1f, y > 0f ? y : 1f);
        }
    }

    public bool VerticalSync
    {
        set
        {
            GLFW.MakeContextCurrent(Handle);
            GLFW.SwapInterval(value ? 1 : 0);
        }
    }

    private int targetFps = 100;
    private double targetTicks = Stopwatch.Frequency / 100.0;

    public int TargetFPS
    {
        get => targetFps;
        set
        {
            targetFps = value;
            targetTicks = Stopwatch.Frequency / (double)value;
        }
    }

    public virtual void Load() { }
    public virtual void Draw() { }

    public string GetClipboardString()
    {
        var ptr = GLFW.GetClipboardString(Handle);
        return Marshal.PtrToStringUTF8(ptr) ?? string.Empty;
    }

    public void Open(CreationSettings settings)
    {
        if (GLFW.Init() == 0) throw new Exception("Unable to initialize GLFW.");

        GLFW.GetVersion(out int gMajor, out int gMinor, out int gRev);
        GLFWVersion = (gMajor, gMinor, gRev);
        Debug.WriteLine($"GLFW {GLFW.GetVersionString()}");

        GLFW.WindowHint(GLFWC.SAMPLES, 4);
#if DEBUG
        GLFW.WindowHint(GLFWC.OPENGL_DEBUG_CONTEXT, GLFWC.TRUE);
#endif
        Handle = GLFW.CreateWindow(settings.Width, settings.Height, settings.Title, IntPtr.Zero, IntPtr.Zero);
        if (Handle == IntPtr.Zero)
        {
            GLFW.Terminate();
            throw new Exception("Unable to create window.");
        }

        title = settings.Title;
        GLFW.MakeContextCurrent(Handle);
        GL.Load();
        GL.Enable(GLC.MULTISAMPLE);

        int major = GL.GetIntegerv(GLC.MAJOR_VERSION);
        int minor = GL.GetIntegerv(GLC.MINOR_VERSION);
        OpenGLVersion = (major, minor);
        Debug.WriteLine($"OpenGL {major}.{minor}");

        if (major < 3 || (major == 3 && minor < 3))
        {
            throw new OpenGLVersionNotSupportedException(
                $"OpenGL {major}.{minor} detected. At least OpenGL 3.3 is required.");
        }

        GL.DebugMessageCallback(debugCallback, IntPtr.Zero);
        GL.Enable(GLC.DEBUG_OUTPUT_SYNCHRONOUS);

        errorCallback = (code, msg) => Debug.WriteLine($"[GLFW error {code}] {msg}");
        GLFW.SetErrorCallback(errorCallback);

        Keyboard = new Keyboard(Handle);
        Mouse = new Mouse(Handle);

        GLFW.GetWindowSize(Handle, out width, out height);
        Time = GLFW.GetTime();

        Load();
        RunLoop();
    }

    public void Close() => GLFW.SetWindowShouldClose(Handle, 1);

    private void RunLoop()
    {
        while (GLFW.WindowShouldClose(Handle) == 0)
        {
            long start = Stopwatch.GetTimestamp();
            GLFW.GetWindowSize(Handle, out width, out height);

            Time = GLFW.GetTime();
            Draw();
            GLFW.SwapBuffers(Handle);
            Keyboard.SwapStates();
            Mouse.SwapStates();
            GLFW.PollEvents();

            while (targetTicks - (Stopwatch.GetTimestamp() - start) > 0)
            {
                Thread.Sleep(0);
            }
        }

        GLFW.DestroyWindow(Handle);
    }

    private static void OnGLDebug(uint source, uint type, uint id, uint severity,
                                  int length, IntPtr message)
    {
        // Ignore non-significant codes suggested by learnopengl.com
        if (id == 131169 || id == 131185 || id == 131218 || id == 131204) return;

        string msg = Marshal.PtrToStringUTF8(message) ?? string.Empty;
        Debug.WriteLine($"[GL {(GLDebugMessageSeverity)severity}] {(GLDebugMessageType)type}: {msg}");

        if (severity == (uint)GLDebugMessageSeverity.High)
        {
            throw new Exception($"GL error: {msg}");
        }
    }
}
