using System;
using System.Numerics;
using Raylib_cs;

namespace JitterClipVisualizer;

internal static class Program
{
    private static readonly Color Background = new(246, 241, 233, 255);
    private static readonly Color PanelFill = new(255, 252, 246, 255);
    private static readonly Color PanelBorder = new(205, 191, 170, 255);
    private static readonly Color Ink = new(37, 45, 57, 255);
    private static readonly Color Muted = new(110, 120, 132, 255);
    private static readonly Color SubjectFill = new(35, 101, 176, 64);
    private static readonly Color SubjectStroke = new(35, 101, 176, 255);
    private static readonly Color ClipFill = new(230, 142, 33, 64);
    private static readonly Color ClipStroke = new(230, 142, 33, 255);
    private static readonly Color OutputFill = new(37, 151, 92, 88);
    private static readonly Color OutputStroke = new(37, 151, 92, 255);
    private static readonly Color InputStroke = new(118, 128, 140, 255);
    private static readonly Color CurrentEdge = new(198, 61, 63, 255);

    private static int presetIndex;
    private static int stepIndex;
    private static float sceneTime;
    private static float stepTimer;
    private static bool animateGeometry = true;
    private static bool animateSteps = true;

    private static void Main()
    {
        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow | ConfigFlags.Msaa4xHint);
        Raylib.InitWindow(1500, 940, "Jitter2 ContactManifold Clipping Visualizer");
        Raylib.SetTargetFPS(60);

        var presets = ContactManifoldClipDebugger.CreatePresets();

        while (!Raylib.WindowShouldClose())
        {
            float deltaTime = Raylib.GetFrameTime();

            HandleInput(presets.Count);

            if (animateGeometry)
            {
                sceneTime += deltaTime;
            }

            ClipSnapshot snapshot = ContactManifoldClipDebugger.BuildSnapshot(presets[presetIndex], sceneTime);
            UpdateCurrentStep(snapshot, deltaTime);

            Raylib.BeginDrawing();
            Raylib.ClearBackground(Background);

            DrawScene(snapshot);

            Raylib.EndDrawing();
        }

        Raylib.CloseWindow();
    }

    private static void HandleInput(int presetCount)
    {
        if (Raylib.IsKeyPressed(KeyboardKey.Space))
        {
            animateSteps = !animateSteps;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.A))
        {
            animateGeometry = !animateGeometry;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.R))
        {
            sceneTime = 0.0f;
            stepIndex = 0;
            stepTimer = 0.0f;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.Tab))
        {
            presetIndex = (presetIndex + 1) % presetCount;
            stepIndex = 0;
            stepTimer = 0.0f;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.One))
        {
            presetIndex = 0;
            stepIndex = 0;
            stepTimer = 0.0f;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.Two) && presetCount > 1)
        {
            presetIndex = 1;
            stepIndex = 0;
            stepTimer = 0.0f;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.Three) && presetCount > 2)
        {
            presetIndex = 2;
            stepIndex = 0;
            stepTimer = 0.0f;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.Right))
        {
            animateSteps = false;
            stepIndex += 1;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.Left))
        {
            animateSteps = false;
            stepIndex -= 1;
        }
    }

    private static void UpdateCurrentStep(ClipSnapshot snapshot, float deltaTime)
    {
        int stepCount = Math.Max(snapshot.Steps.Length, 1);

        if (animateSteps && stepCount > 1)
        {
            stepTimer += deltaTime;

            if (stepTimer >= 0.95f)
            {
                stepTimer -= 0.95f;
                stepIndex = (stepIndex + 1) % stepCount;
            }
        }

        if (stepIndex < 0)
        {
            stepIndex = stepCount - 1;
        }

        if (stepIndex >= stepCount)
        {
            stepIndex = 0;
        }
    }

    private static void DrawScene(ClipSnapshot snapshot)
    {
        int width = Raylib.GetScreenWidth();
        int height = Raylib.GetScreenHeight();

        const float outerMargin = 24.0f;
        const float columnGap = 18.0f;
        const float panelTop = 124.0f;
        const float panelBottom = 88.0f;
        float panelWidth = (width - outerMargin * 2.0f - columnGap * 2.0f) / 3.0f;
        float panelHeight = height - panelTop - panelBottom;

        Rectangle leftPanel = new(outerMargin, panelTop, panelWidth, panelHeight);
        Rectangle centerPanel = new(outerMargin + panelWidth + columnGap, panelTop, panelWidth, panelHeight);
        Rectangle rightPanel = new(outerMargin + (panelWidth + columnGap) * 2.0f, panelTop, panelWidth, panelHeight);

        ClipStep step = snapshot.Steps[stepIndex];
        WorldBounds bounds = CollectBounds(snapshot, step);

        DrawHeader(snapshot);

        DrawPanel(leftPanel, "Projected Inputs");
        DrawPanel(centerPanel, "Current Pass");
        DrawPanel(rightPanel, "Final Output");

        DrawGeometryPanel(leftPanel, bounds, snapshot.Left, snapshot.Right, snapshot.Result);
        DrawStepPanel(centerPanel, bounds, snapshot, step);
        DrawResultPanel(rightPanel, bounds, snapshot.Left, snapshot.Right, snapshot.Result);

        DrawFooter(snapshot);
    }

    private static void DrawHeader(ClipSnapshot snapshot)
    {
        Raylib.DrawText(snapshot.PresetName, 28, 18, 34, Ink);
        Raylib.DrawText(snapshot.Description, 28, 57, 22, Muted);

        string source = "Mirrors the 2D clipping helpers in src/Jitter2/Collision/NarrowPhase/CollisionManifold.cs";
        Raylib.DrawText(source, 28, 85, 18, Muted);

        string status = $"{ContactManifoldClipDebugger.GetModeLabel(snapshot.Mode)} | {snapshot.Status}";
        Raylib.DrawText(status, 28, 106, 18, Ink);
    }

    private static void DrawFooter(ClipSnapshot snapshot)
    {
        int y = Raylib.GetScreenHeight() - 54;
        string controls = "Tab/1-3 preset   Left/Right step   Space autoplay steps   A animate geometry   R reset time   Esc quit";
        string step = $"Step {stepIndex + 1}/{Math.Max(snapshot.Steps.Length, 1)}";

        Raylib.DrawText(controls, 28, y, 18, Muted);
        Raylib.DrawText(step, Raylib.GetScreenWidth() - 170, y, 18, Ink);
    }

    private static void DrawPanel(Rectangle rect, string title)
    {
        Raylib.DrawRectangleRounded(rect, 0.03f, 8, PanelFill);
        Raylib.DrawRectangleRoundedLinesEx(rect, 0.03f, 8, 1.4f, PanelBorder);
        Raylib.DrawText(title, (int)rect.X + 16, (int)rect.Y + 12, 24, Ink);
    }

    private static void DrawGeometryPanel(Rectangle rect, WorldBounds bounds, ClipPoint[] left, ClipPoint[] right, ClipPoint[] result)
    {
        Rectangle plot = GetPlotRect(rect);

        DrawGrid(plot, bounds);
        DrawShape(plot, bounds, left, SubjectFill, SubjectStroke, 2.4f);
        DrawShape(plot, bounds, right, ClipFill, ClipStroke, 2.4f);

        if (result.Length > 0)
        {
            DrawShape(plot, bounds, result, Raylib.Fade(OutputFill, 0.70f), OutputStroke, 2.2f);
        }

        DrawLegend((int)rect.X + 16, (int)rect.Y + 46, SubjectStroke, "subject");
        DrawLegend((int)rect.X + 124, (int)rect.Y + 46, ClipStroke, "clip");
        DrawLegend((int)rect.X + 206, (int)rect.Y + 46, OutputStroke, "result");
    }

    private static void DrawStepPanel(Rectangle rect, WorldBounds bounds, ClipSnapshot snapshot, ClipStep step)
    {
        Rectangle plot = GetPlotRect(rect);
        DrawGrid(plot, bounds);

        DrawShape(plot, bounds, snapshot.Right, ClipFill, ClipStroke, 2.2f);
        DrawShape(plot, bounds, step.InputPolygon, new Color(160, 166, 175, 34), InputStroke, 2.2f);
        DrawShape(plot, bounds, step.OutputPolygon, OutputFill, OutputStroke, 2.6f);

        if (step.EdgeIndex >= 0)
        {
            DrawEdge(plot, bounds, step.EdgeStart, step.EdgeEnd, CurrentEdge, 4.0f);
        }

        int textX = (int)rect.X + 16;
        int textY = (int)(rect.Y + rect.Height) - 88;
        Raylib.DrawText(step.Title, textX, textY, 22, Ink);
        Raylib.DrawText(step.Summary, textX, textY + 28, 18, Muted);
    }

    private static void DrawResultPanel(Rectangle rect, WorldBounds bounds, ClipPoint[] left, ClipPoint[] right, ClipPoint[] result)
    {
        Rectangle plot = GetPlotRect(rect);
        DrawGrid(plot, bounds);

        DrawShape(plot, bounds, left, new Color(0, 0, 0, 0), SubjectStroke, 2.0f);
        DrawShape(plot, bounds, right, new Color(0, 0, 0, 0), ClipStroke, 2.0f);
        DrawShape(plot, bounds, result, OutputFill, OutputStroke, 3.0f);

        string label = result.Length switch
        {
            0 => "empty result",
            1 => "point contact",
            2 => "line overlap",
            _ => "area overlap"
        };

        Raylib.DrawText(label, (int)rect.X + 16, (int)(rect.Y + rect.Height) - 54, 20, Ink);
    }

    private static void DrawGrid(Rectangle plot, WorldBounds bounds)
    {
        Raylib.DrawRectangleRounded(plot, 0.02f, 6, new Color(250, 247, 241, 255));

        Vector2 left = WorldToScreen(plot, bounds, new ClipPoint(bounds.MinX, 0.0f));
        Vector2 right = WorldToScreen(plot, bounds, new ClipPoint(bounds.MaxX, 0.0f));
        Vector2 top = WorldToScreen(plot, bounds, new ClipPoint(0.0f, bounds.MaxY));
        Vector2 bottom = WorldToScreen(plot, bounds, new ClipPoint(0.0f, bounds.MinY));

        Raylib.DrawLineEx(left, right, 1.0f, new Color(226, 219, 206, 255));
        Raylib.DrawLineEx(top, bottom, 1.0f, new Color(226, 219, 206, 255));
    }

    private static void DrawLegend(int x, int y, Color color, string text)
    {
        Raylib.DrawRectangle(x, y + 3, 16, 10, color);
        Raylib.DrawText(text, x + 22, y, 18, Muted);
    }

    private static Rectangle GetPlotRect(Rectangle panel)
    {
        return new Rectangle(panel.X + 14.0f, panel.Y + 78.0f, panel.Width - 28.0f, panel.Height - 166.0f);
    }

    private static void DrawShape(Rectangle plot, WorldBounds bounds, ClipPoint[] polygon, Color fill, Color stroke, float thickness)
    {
        if (polygon.Length == 0) return;

        Vector2[] vertices = new Vector2[polygon.Length];

        for (int i = 0; i < polygon.Length; i++)
        {
            vertices[i] = WorldToScreen(plot, bounds, polygon[i]);
        }

        if (polygon.Length >= 3 && fill.A > 0)
        {
            for (int i = 1; i < vertices.Length - 1; i++)
            {
                Raylib.DrawTriangle(vertices[0], vertices[i], vertices[i + 1], fill);
            }
        }

        if (polygon.Length == 1)
        {
            Raylib.DrawCircleV(vertices[0], 6.0f, stroke);
            return;
        }

        for (int i = 0; i < vertices.Length - 1; i++)
        {
            Raylib.DrawLineEx(vertices[i], vertices[i + 1], thickness, stroke);
        }

        if (polygon.Length >= 3)
        {
            Raylib.DrawLineEx(vertices[^1], vertices[0], thickness, stroke);
        }

        foreach (Vector2 vertex in vertices)
        {
            Raylib.DrawCircleV(vertex, 4.5f, stroke);
            Raylib.DrawCircleV(vertex, 2.2f, PanelFill);
        }
    }

    private static void DrawEdge(Rectangle plot, WorldBounds bounds, ClipPoint start, ClipPoint end, Color color, float thickness)
    {
        Vector2 a = WorldToScreen(plot, bounds, start);
        Vector2 b = WorldToScreen(plot, bounds, end);
        Raylib.DrawLineEx(a, b, thickness, color);
        Raylib.DrawCircleV(a, 5.0f, color);
        Raylib.DrawCircleV(b, 5.0f, color);
    }

    private static Vector2 WorldToScreen(Rectangle plot, WorldBounds bounds, ClipPoint point)
    {
        float rangeX = MathF.Max(bounds.MaxX - bounds.MinX, 1.0f);
        float rangeY = MathF.Max(bounds.MaxY - bounds.MinY, 1.0f);
        float scale = MathF.Min((plot.Width - 36.0f) / rangeX, (plot.Height - 36.0f) / rangeY);

        float centerX = 0.5f * (bounds.MinX + bounds.MaxX);
        float centerY = 0.5f * (bounds.MinY + bounds.MaxY);

        return new Vector2(
            plot.X + plot.Width * 0.5f + (point.X - centerX) * scale,
            plot.Y + plot.Height * 0.5f - (point.Y - centerY) * scale);
    }

    private static WorldBounds CollectBounds(ClipSnapshot snapshot, ClipStep step)
    {
        WorldBounds bounds = new(float.MaxValue, float.MaxValue, float.MinValue, float.MinValue);

        Expand(ref bounds, snapshot.Left);
        Expand(ref bounds, snapshot.Right);
        Expand(ref bounds, snapshot.Result);
        Expand(ref bounds, step.InputPolygon);
        Expand(ref bounds, step.OutputPolygon);

        if (step.EdgeIndex >= 0)
        {
            Expand(ref bounds, step.EdgeStart);
            Expand(ref bounds, step.EdgeEnd);
        }

        if (bounds.MinX == float.MaxValue)
        {
            bounds = new WorldBounds(-120.0f, -120.0f, 120.0f, 120.0f);
        }

        float sizeX = bounds.MaxX - bounds.MinX;
        float sizeY = bounds.MaxY - bounds.MinY;
        float pad = MathF.Max(MathF.Max(sizeX, sizeY) * 0.16f, 24.0f);

        return new WorldBounds(bounds.MinX - pad, bounds.MinY - pad, bounds.MaxX + pad, bounds.MaxY + pad);
    }

    private static void Expand(ref WorldBounds bounds, ClipPoint point)
    {
        bounds.MinX = MathF.Min(bounds.MinX, point.X);
        bounds.MinY = MathF.Min(bounds.MinY, point.Y);
        bounds.MaxX = MathF.Max(bounds.MaxX, point.X);
        bounds.MaxY = MathF.Max(bounds.MaxY, point.Y);
    }

    private static void Expand(ref WorldBounds bounds, ClipPoint[] points)
    {
        foreach (ClipPoint point in points)
        {
            Expand(ref bounds, point);
        }
    }

    private struct WorldBounds(float minX, float minY, float maxX, float maxY)
    {
        public float MinX = minX;
        public float MinY = minY;
        public float MaxX = maxX;
        public float MaxY = maxY;
    }
}
