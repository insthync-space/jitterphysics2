using System;
using JitterDemo.Renderer.OpenGL.Native;

namespace JitterDemo.Renderer.OpenGL;

public enum TextureFormat : uint
{
    Depth = GLC.DEPTH_COMPONENT,
    Red = GLC.RED,
    RG = GLC.RG,
    RGB = GLC.RGB,
    RGBA = GLC.RGBA
}

public enum TextureDataFormat : uint
{
    RGBA = GLC.RGBA,
    BGRA = GLC.BGRA
}

public enum TexelType : uint
{
    UnsignedByte = GLC.UNSIGNED_BYTE,
    Float = GLC.FLOAT
}

public enum TextureWrap : uint
{
    Repeat = GLC.REPEAT,
    MirroredRepeat = GLC.MIRRORED_REPEAT,
    ClampToEdge = GLC.CLAMP_TO_EDGE,
    ClampToBorder = GLC.CLAMP_TO_BORDER
}

public enum TextureFilter : uint
{
    Nearest = GLC.NEAREST,
    Linear = GLC.LINEAR,
    NearestMipmapNearest = GLC.NEAREST_MIPMAP_NEAREST,
    LinearMipmapNearest = GLC.LINEAR_MIPMAP_NEAREST,
    NearestMipmapLinear = GLC.NEAREST_MIPMAP_LINEAR,
    LinearMipmapLinear = GLC.LINEAR_MIPMAP_LINEAR
}

public enum Anisotropy
{
    X1 = 1,
    X2 = 2,
    X4 = 4,
    X8 = 8,
    X16 = 16
}

public class Texture
{
    public uint Handle { get; }
    protected uint Target { get; }

    protected Texture(uint target)
    {
        Target = target;
        Handle = GL.GenTexture();
        GL.BindTexture(Target, Handle);
    }

    public virtual void Bind() => GL.BindTexture(Target, Handle);

    public virtual void Bind(uint unit)
    {
        GL.ActiveTexture(GLC.TEXTURE0 + unit);
        GL.BindTexture(Target, Handle);
    }
}

public sealed class Texture2D : Texture
{
    public Texture2D() : base(GLC.TEXTURE_2D) { }

    // Shared 1x1 opaque-black texture. Lazy because we need a live GL context.
    private static Texture2D? emptyShared;
    public static Texture2D Empty() => emptyShared ??= CreateEmpty();

    private static unsafe Texture2D CreateEmpty()
    {
        var t = new Texture2D();
        int black = 0;
        t.LoadImage((IntPtr)(&black), 1, 1, generateMipmap: false);
        return t;
    }

    public void LoadImage(IntPtr data, int width, int height, bool generateMipmap = true,
        TextureDataFormat dataFormat = TextureDataFormat.BGRA)
    {
        Bind();
        GL.TexImage2D(GLC.TEXTURE_2D, 0, (int)GLC.RGBA, width, height, 0, (uint)dataFormat, GLC.UNSIGNED_BYTE, data);

        if (generateMipmap)
        {
            SetMinMagFilter(TextureFilter.LinearMipmapLinear, TextureFilter.Linear);
            GL.GenerateMipmap(GLC.TEXTURE_2D);
        }
        else
        {
            SetMinMagFilter(TextureFilter.Linear, TextureFilter.Linear);
        }
    }

    public void Allocate(TextureFormat format, int width, int height, TexelType type)
    {
        Bind();
        GL.TexImage2D(GLC.TEXTURE_2D, 0, (int)format, width, height, 0, (uint)format, (uint)type, IntPtr.Zero);
    }

    public void SetBorderColor(in Vector4 color)
    {
        Bind();
        unsafe
        {
            fixed (float* p = &color.X) GL.TexParameterfv(GLC.TEXTURE_2D, GLC.TEXTURE_BORDER_COLOR, p);
        }
    }

    public void SetWrap(TextureWrap wrap)
    {
        Bind();
        GL.TexParameteri(GLC.TEXTURE_2D, GLC.TEXTURE_WRAP_S, (int)wrap);
        GL.TexParameteri(GLC.TEXTURE_2D, GLC.TEXTURE_WRAP_T, (int)wrap);
    }

    public void SetMinMagFilter(TextureFilter min, TextureFilter mag)
    {
        Bind();
        GL.TexParameteri(GLC.TEXTURE_2D, GLC.TEXTURE_MIN_FILTER, (int)min);
        GL.TexParameteri(GLC.TEXTURE_2D, GLC.TEXTURE_MAG_FILTER, (int)mag);
    }

    public void SetAnisotropy(Anisotropy requested)
    {
        Bind();
        const uint TEXTURE_MAX_ANISOTROPY = 0x84FE;
        const uint MAX_TEXTURE_MAX_ANISOTROPY = 0x84FF;

        unsafe
        {
            float hwMax;
            GL.GetFloatv(MAX_TEXTURE_MAX_ANISOTROPY, &hwMax);
            float value = Math.Min((float)requested, hwMax);
            GL.TexParameterfv(GLC.TEXTURE_2D, TEXTURE_MAX_ANISOTROPY, &value);
        }
    }
}

public sealed class CubemapTexture : Texture
{
    public CubemapTexture() : base(GLC.TEXTURE_CUBE_MAP) { }

    public void LoadBitmaps(IntPtr[] bitmaps, int width, int height)
    {
        if (bitmaps.Length != 6) throw new ArgumentException("Array length must be 6.", nameof(bitmaps));

        Bind();

        for (int i = 0; i < 6; i++)
        {
            GL.TexImage2D(GLC.TEXTURE_CUBE_MAP_POSITIVE_X + (uint)i,
                0, (int)GLC.RGBA, width, height, 0,
                GLC.BGRA, GLC.UNSIGNED_BYTE, bitmaps[i]);
        }

        GL.TexParameteri(GLC.TEXTURE_CUBE_MAP, GLC.TEXTURE_MIN_FILTER, (int)GLC.LINEAR);
        GL.TexParameteri(GLC.TEXTURE_CUBE_MAP, GLC.TEXTURE_MAG_FILTER, (int)GLC.LINEAR);
        GL.TexParameteri(GLC.TEXTURE_CUBE_MAP, GLC.TEXTURE_WRAP_S, (int)GLC.CLAMP_TO_EDGE);
        GL.TexParameteri(GLC.TEXTURE_CUBE_MAP, GLC.TEXTURE_WRAP_T, (int)GLC.CLAMP_TO_EDGE);
        GL.TexParameteri(GLC.TEXTURE_CUBE_MAP, GLC.TEXTURE_WRAP_R, (int)GLC.CLAMP_TO_EDGE);
    }
}
