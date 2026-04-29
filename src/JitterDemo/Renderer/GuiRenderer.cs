using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Vellum.Rendering;
using JitterDemo.Renderer.OpenGL;

namespace JitterDemo.Renderer;

public class GuiRenderer : IRenderer
{
    private GpuBuffer vbo = null!;
    private GpuBuffer ebo = null!;
    private Vao vao = null!;
    private Shader shader = null!;

    private List<Texture2D> textures = new();
    private Stack<int> freeTextureSlots = new();
    private int viewportWidth = 1;
    private int viewportHeight = 1;
    private int framebufferWidth = 1;
    private int framebufferHeight = 1;

    public GuiRenderer()
    {
        shader = new Shader(Vs, Fs);
        shader.Use();
        shader.Set("uFontTexture", 0);
        shader.Set("uLcdMaskPass", false);

        vao = new Vao();
        vbo = GpuBuffer.Vertex();
        ebo = GpuBuffer.Index();

        int stride = Unsafe.SizeOf<DrawVertex>();
        vao.Attrib(0, vbo, 2, AttribType.Float, stride, 0);  // pos
        vao.Attrib(1, vbo, 2, AttribType.Float, stride, 2 * sizeof(float)); // uv
        vao.Attrib(2, vbo, 4, AttribType.UnsignedByte, stride, 4 * sizeof(float), normalized: true); // color
        vao.AttachIndexBuffer(ebo);

        textures.Add(CreateSolidTexture());
    }
    
    
    public void BeginFrame(RenderFrameInfo frame)
    {
        frame = frame.Normalized();
        viewportWidth = Math.Max(1, frame.LogicalWidth);
        viewportHeight = Math.Max(1, frame.LogicalHeight);
        framebufferWidth = Math.Max(1, frame.FramebufferWidth);
        framebufferHeight = Math.Max(1, frame.FramebufferHeight);
    }

    public void BeginFrame(int viewportWidth, int viewportHeight)
    {
        BeginFrame(new RenderFrameInfo(viewportWidth, viewportHeight));
    }

    public void SetFramebufferSize(int framebufferWidth, int framebufferHeight)
    {
        this.framebufferWidth = Math.Max(1, framebufferWidth);
        this.framebufferHeight = Math.Max(1, framebufferHeight);
    }

    public unsafe void Render(RenderList renderList)
    {
        if (renderList.Commands.Count == 0) return;

        shader.Use();
        vao.Bind();
        
        Matrix4 projection = MatrixHelper.CreateOrthographicOffCenter(
            0, viewportWidth, viewportHeight, 0, -1f, +1f);
        shader.Set("uProjection", projection);
        
        GLDevice.Enable(Capability.Blend);
        SetAlphaBlend();
        GLDevice.Disable(Capability.DepthTest);
        GLDevice.Disable(Capability.CullFace);
        
        var vertexSpan = CollectionsMarshal.AsSpan(renderList.Vertices);
        var indexSpan = CollectionsMarshal.AsSpan(renderList.Indices);
        
        vbo.Stream(vertexSpan);
        ebo.Stream(indexSpan);

        foreach (var cmd in renderList.Commands)
        {
            if (cmd.HasClip)
            {
                if (!ApplyClip(cmd.ClipRect)) continue;
                GLDevice.Enable(Capability.ScissorTest);
            }
            else
            {
                GLDevice.Disable(Capability.ScissorTest);
            }

            if (cmd.TextureId < 0 || cmd.TextureId >= textures.Count) continue;

            textures[cmd.TextureId].Bind(0);
            DrawCommand(cmd);
        }
        
        GLDevice.Disable(Capability.Blend);
        GLDevice.Enable(Capability.DepthTest);
        GLDevice.Enable(Capability.CullFace);
        GLDevice.Disable(Capability.ScissorTest);
    }

    public void EndFrame()
    {
        //throw new System.NotImplementedException();
    }

    public unsafe int CreateTexture(byte[] rgba, int width, int height)
    {
        if (rgba.Length == 0 || width <= 0 || height <= 0) return -1;

        Texture2D texture = new Texture2D();

        fixed (byte* p = rgba)
        {
            texture.LoadImage((nint)p, width, height, generateMipmap: false, TextureDataFormat.RGBA);
            texture.SetWrap(TextureWrap.ClampToEdge);
            texture.SetMinMagFilter(TextureFilter.Nearest, TextureFilter.Nearest);
        }

        if (freeTextureSlots.Count > 0)
        {
            int slot = freeTextureSlots.Pop();
            textures[slot] = texture;
            return slot;
        }

        textures.Add(texture);
        return textures.Count - 1;
    }

    public void DestroyTexture(int textureId)
    {
        if (textureId <= RenderTextureIds.Solid || textureId >= textures.Count) return;
        if (ReferenceEquals(textures[textureId], textures[RenderTextureIds.Solid])) return;

        textures[textureId] = textures[RenderTextureIds.Solid];
        freeTextureSlots.Push(textureId);
    }

    private static unsafe Texture2D CreateSolidTexture()
    {
        uint white = 0xffffffff;
        Texture2D texture = new Texture2D();
        texture.LoadImage((nint)(&white), 1, 1, generateMipmap: false);
        texture.SetWrap(TextureWrap.ClampToEdge);
        texture.SetMinMagFilter(TextureFilter.Linear, TextureFilter.Linear);
        return texture;
    }

    private bool ApplyClip(in ClipRect clipRect)
    {
        float scaleX = (float)framebufferWidth / viewportWidth;
        float scaleY = (float)framebufferHeight / viewportHeight;

        float x1f = Math.Clamp(clipRect.X * scaleX, 0f, framebufferWidth);
        float y1f = Math.Clamp(clipRect.Y * scaleY, 0f, framebufferHeight);
        float x2f = Math.Clamp((clipRect.X + clipRect.Width) * scaleX, 0f, framebufferWidth);
        float y2f = Math.Clamp((clipRect.Y + clipRect.Height) * scaleY, 0f, framebufferHeight);

        int x1 = (int)MathF.Floor(x1f);
        int y1 = (int)MathF.Floor(y1f);
        int x2 = (int)MathF.Ceiling(x2f);
        int y2 = (int)MathF.Ceiling(y2f);

        int width = Math.Max(0, x2 - x1);
        int height = Math.Max(0, y2 - y1);
        if (width == 0 || height == 0) return false;

        GLDevice.Scissor(x1, framebufferHeight - y2, width, height);
        return true;
    }

    private void DrawCommand(in DrawCommand cmd)
    {
        if (cmd.Lcd && cmd.TextureId != RenderTextureIds.Solid)
        {
            shader.Set("uLcdMaskPass", true);
            GLDevice.Blend(BlendFunc.Zero, BlendFunc.OneMinusSrcColor);
            DrawIndexed(cmd);

            shader.Set("uLcdMaskPass", false);
            GLDevice.Blend(BlendFunc.One, BlendFunc.One);
            DrawIndexed(cmd);

            SetAlphaBlend();
            return;
        }

        shader.Set("uLcdMaskPass", false);
        DrawIndexed(cmd);
    }

    private static void DrawIndexed(in DrawCommand cmd)
    {
        GLDevice.DrawElements(DrawMode.Triangles, cmd.IndexCount, IndexType.UnsignedInt,
            cmd.IndexOffset * sizeof(uint));
    }

    private static void SetAlphaBlend()
    {
        GLDevice.Blend(BlendFunc.SrcAlpha, BlendFunc.OneMinusSrcAlpha);
    }
    
    private const string Vs = @"
#version 330 core

uniform mat4 uProjection;

layout(location = 0) in vec2 aPos;
layout(location = 1) in vec2 aUV;
layout(location = 2) in vec4 aColor;

out vec4 vColor;
out vec2 vUV;

void main()
{
    gl_Position = uProjection * vec4(aPos, 0, 1);
    vColor = aColor;
    vUV = aUV;
}
";

    private const string Fs = @"
#version 330 core

uniform sampler2D uFontTexture;
uniform bool uLcdMaskPass;

in vec4 vColor;
in vec2 vUV;

out vec4 FragColor;

void main()
{
    vec4 texel = texture(uFontTexture, vUV);
    vec4 tint = uLcdMaskPass ? vColor.aaaa : vColor;
    FragColor = tint * texel;
}
";
}
