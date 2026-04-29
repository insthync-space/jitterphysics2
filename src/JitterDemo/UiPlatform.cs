using System;
using System.Collections.Generic;
using Vellum;
using JitterDemo.Renderer.OpenGL;
using JitterDemo.Renderer.OpenGL.Native;

namespace JitterDemo;

public class UiPlatform : IUiPlatform
{
    private readonly GLFWWindow window;
    private readonly Dictionary<UiCursor, IntPtr> cursors = new();

    private UiCursor currentCursor = (UiCursor)(-1);

    public UiPlatform(GLFWWindow window)
    {
        this.window = window;
    }

    public string GetClipboardText()
    {
        return window.GetClipboardString();
    }

    public void SetClipboardText(string text)
    {
        GLFW.SetClipboardString(window.Handle, text);
    }

    public void SetCursor(UiCursor cursor)
    {
        if (currentCursor == cursor) return;

        currentCursor = cursor;
        GLFW.SetCursor(window.Handle, GetCursor(cursor));
    }

    private IntPtr GetCursor(UiCursor cursor)
    {
        if (cursors.TryGetValue(cursor, out IntPtr handle)) return handle;

        handle = GLFW.CreateStandardCursor(ToGlfwCursor(cursor));
        cursors.Add(cursor, handle);
        return handle;
    }

    private static int ToGlfwCursor(UiCursor cursor)
    {
        return cursor switch
        {
            UiCursor.IBeam => GLFWC.IBEAM_CURSOR,
            UiCursor.PointingHand => GLFWC.HAND_CURSOR,
            UiCursor.ResizeEW => GLFWC.HRESIZE_CURSOR,
            UiCursor.ResizeNWSE => GLFWC.HRESIZE_CURSOR,
            _ => GLFWC.ARROW_CURSOR
        };
    }
}
