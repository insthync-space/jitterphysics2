using System;
using System.Collections.Generic;
using System.Numerics;

namespace JitterClipVisualizer;

internal readonly record struct ClipPoint(float X, float Y)
{
    public static ClipPoint operator +(ClipPoint left, ClipPoint right)
    {
        return new ClipPoint(left.X + right.X, left.Y + right.Y);
    }

    public static ClipPoint operator -(ClipPoint left, ClipPoint right)
    {
        return new ClipPoint(left.X - right.X, left.Y - right.Y);
    }

    public static ClipPoint operator *(float scale, ClipPoint point)
    {
        return new ClipPoint(scale * point.X, scale * point.Y);
    }

    public float LengthSquared()
    {
        return X * X + Y * Y;
    }

    public Vector2 ToVector2()
    {
        return new Vector2(X, Y);
    }
}

internal enum ClipDebugMode
{
    PolygonClip,
    SegmentAgainstPolygon,
    SegmentAgainstSegment,
    NoIntersection
}

internal sealed record ClipStep(
    string Title,
    string Summary,
    ClipPoint[] InputPolygon,
    ClipPoint[] OutputPolygon,
    int EdgeIndex = -1,
    ClipPoint EdgeStart = default,
    ClipPoint EdgeEnd = default);

internal sealed record ClipSnapshot(
    string PresetName,
    string Description,
    ClipDebugMode Mode,
    string Status,
    ClipPoint[] Left,
    ClipPoint[] Right,
    ClipPoint[] Result,
    ClipStep[] Steps);

internal sealed record ClipFrame(ClipPoint[] Left, ClipPoint[] Right);

internal sealed record ClipPreset(string Name, string Description, Func<float, ClipFrame> BuildFrame);

internal static class ContactManifoldClipDebugger
{
    private const int MaxManifoldPoints = 6;
    private const int MaxClipPoints = 12;
    private const int FinalVertexLimit = 4;

    private static readonly ClipPoint[] polygonA =
    [
        new ClipPoint(100.0f, 0.0f),
        new ClipPoint(50.0f, 86.0f),
        new ClipPoint(-50.0f, 86.0f),
        new ClipPoint(-100.0f, 0.0f),
        new ClipPoint(-50.0f, -86.0f),
        new ClipPoint(50.0f, -86.0f)
    ];

    private static readonly ClipPoint[] polygonB =
    [
        new ClipPoint(118.0f, 0.0f),
        new ClipPoint(36.0f, 110.0f),
        new ClipPoint(-96.0f, 68.0f),
        new ClipPoint(-96.0f, -68.0f),
        new ClipPoint(36.0f, -110.0f)
    ];

    private static readonly ClipPoint[] lineA =
    [
        new ClipPoint(-165.0f, 0.0f),
        new ClipPoint(165.0f, 0.0f)
    ];

    private static readonly ClipPoint[] lineB =
    [
        new ClipPoint(-130.0f, 0.0f),
        new ClipPoint(130.0f, 0.0f)
    ];

    public static IReadOnlyList<ClipPreset> CreatePresets()
    {
        return
        [
            new ClipPreset(
                "Polygon Clip",
                "The Sutherland-Hodgman style pass sequence used for area overlap in CollisionManifold.",
                BuildPolygonClipFrame),
            new ClipPreset(
                "Segment vs Polygon",
                "The linear fallback path used when one projected feature collapses to a segment.",
                BuildSegmentVsPolygonFrame),
            new ClipPreset(
                "Segment vs Segment",
                "The segment-overlap helper used when both projected features collapse to lines.",
                BuildSegmentVsSegmentFrame)
        ];
    }

    public static ClipSnapshot BuildSnapshot(ClipPreset preset, float time)
    {
        ClipFrame frame = preset.BuildFrame(time);
        return BuildSnapshot(preset.Name, preset.Description, frame.Left, frame.Right);
    }

    public static ClipSnapshot BuildSnapshot(string presetName, string description, ClipPoint[] leftInput, ClipPoint[] rightInput)
    {
        if (leftInput.Length == 0 || rightInput.Length == 0)
        {
            return new ClipSnapshot(
                presetName,
                description,
                ClipDebugMode.NoIntersection,
                "Both inputs need at least one point.",
                leftInput,
                rightInput,
                [],
                [
                    new ClipStep(
                        "Invalid input",
                        "At least one polygon was empty.",
                        Copy(leftInput, leftInput.Length),
                        [])
                ]);
        }

        if (leftInput.Length > MaxClipPoints || rightInput.Length > MaxClipPoints)
        {
            throw new ArgumentOutOfRangeException(nameof(leftInput),
                $"This visualizer mirrors the manifold clipper limits and supports up to {MaxClipPoints} points per feature.");
        }

        ClipPoint[] left = new ClipPoint[MaxClipPoints];
        ClipPoint[] right = new ClipPoint[MaxClipPoints];
        Array.Copy(leftInput, left, leftInput.Length);
        Array.Copy(rightInput, right, rightInput.Length);

        int leftCount = leftInput.Length;
        int rightCount = rightInput.Length;

        CalculateClipTolerance(left, leftCount, right, rightCount,
            out float sideEpsilon, out float distanceEpsilonSq, out float areaEpsilon);

        CompactPolygon(left, ref leftCount, distanceEpsilonSq, areaEpsilon);
        CompactPolygon(right, ref rightCount, distanceEpsilonSq, areaEpsilon);

        if (leftCount > 2) NormalizeWinding(left, leftCount);
        if (rightCount > 2) NormalizeWinding(right, rightCount);

        List<ClipStep> steps =
        [
            new ClipStep(
                "Projected features",
                $"Compacted to {leftCount} and {rightCount} points before clipping.",
                Copy(left, leftCount),
                Copy(left, leftCount))
        ];

        ClipPoint[] clipped = new ClipPoint[MaxClipPoints];
        int clippedCount = 0;
        ClipDebugMode mode = ClipDebugMode.NoIntersection;

        if (leftCount > 2 && rightCount > 2)
        {
            ClipPoint[] buffer = new ClipPoint[MaxClipPoints];
            Array.Copy(left, clipped, leftCount);

            clippedCount = ClipConvexPolygon(clipped, leftCount, right, rightCount, buffer,
                sideEpsilon, distanceEpsilonSq, areaEpsilon, steps);

            if (clippedCount > 0)
            {
                mode = ClipDebugMode.PolygonClip;
            }
        }

        if (clippedCount == 0)
        {
            if (TryClipLinearIntersection(left, leftCount, right, rightCount,
                    sideEpsilon, distanceEpsilonSq, areaEpsilon, clipped, out clippedCount))
            {
                mode = leftCount == 2 && rightCount == 2
                    ? ClipDebugMode.SegmentAgainstSegment
                    : ClipDebugMode.SegmentAgainstPolygon;

                steps.Add(new ClipStep(
                    "Linear fallback",
                    $"Polygon clipping produced no area overlap, so the 1D fallback returned {clippedCount} point(s).",
                    Copy(left, leftCount),
                    Copy(clipped, clippedCount)));
            }
            else
            {
                steps.Add(new ClipStep(
                    "No overlap",
                    "Neither the polygon clipper nor the linear fallback found an intersection.",
                    Copy(left, leftCount),
                    []));
            }
        }

        CompactPolygon(clipped, ref clippedCount, distanceEpsilonSq, areaEpsilon);
        ReducePolygon(clipped, ref clippedCount);

        ClipPoint[] result = Copy(clipped, clippedCount);
        steps.Add(new ClipStep(
            "Final output",
            clippedCount == 0
                ? "The final clipped feature is empty."
                : $"The final clipped feature contains {clippedCount} point(s).",
            Copy(left, leftCount),
            result));

        string status = mode switch
        {
            ClipDebugMode.PolygonClip => $"{clippedCount} point(s) from polygon-vs-polygon clipping.",
            ClipDebugMode.SegmentAgainstPolygon => $"{clippedCount} point(s) from segment-against-polygon fallback.",
            ClipDebugMode.SegmentAgainstSegment => $"{clippedCount} point(s) from segment-against-segment fallback.",
            _ when clippedCount == 0 => "No overlap after both the polygon and linear clipping paths.",
            _ => $"{clippedCount} point(s) returned."
        };

        return new ClipSnapshot(
            presetName,
            description,
            mode,
            status,
            Copy(left, leftCount),
            Copy(right, rightCount),
            result,
            steps.ToArray());
    }

    public static string GetModeLabel(ClipDebugMode mode)
    {
        return mode switch
        {
            ClipDebugMode.PolygonClip => "Polygon clip",
            ClipDebugMode.SegmentAgainstPolygon => "Segment vs polygon fallback",
            ClipDebugMode.SegmentAgainstSegment => "Segment vs segment fallback",
            _ => "No overlap"
        };
    }

    private static ClipFrame BuildPolygonClipFrame(float time)
    {
        Vector2 drift = new(
            32.0f * MathF.Sin(time * 0.85f),
            26.0f * MathF.Cos(time * 0.55f));

        ClipPoint[] left = Transform(polygonA, drift, 0.45f + time * 0.55f, new Vector2(1.20f, 0.82f));
        ClipPoint[] right = Transform(polygonB, new Vector2(12.0f, -10.0f), -0.42f, new Vector2(1.00f, 0.96f));

        return new ClipFrame(left, right);
    }

    private static ClipFrame BuildSegmentVsPolygonFrame(float time)
    {
        Vector2 drift = new(
            48.0f * MathF.Sin(time * 1.15f),
            72.0f * MathF.Sin(time * 0.63f));

        ClipPoint[] left = Transform(lineA, drift, -0.22f + 0.35f * MathF.Sin(time * 0.7f), Vector2.One);
        ClipPoint[] right = Transform(polygonB, Vector2.Zero, -0.36f, new Vector2(0.95f, 0.78f));

        return new ClipFrame(left, right);
    }

    private static ClipFrame BuildSegmentVsSegmentFrame(float time)
    {
        Vector2 leftOffset = new(
            35.0f * MathF.Sin(time * 0.9f),
            25.0f * MathF.Sin(time * 0.5f));

        Vector2 rightOffset = new(
            85.0f * MathF.Sin(time * 0.55f),
            25.0f * MathF.Sin(time * 0.5f));

        ClipPoint[] left = Transform(lineA, leftOffset, 0.0f, Vector2.One);
        ClipPoint[] right = Transform(lineB, rightOffset, 0.0f, Vector2.One);

        return new ClipFrame(left, right);
    }

    private static ClipPoint[] Transform(ClipPoint[] points, Vector2 translation, float rotation, Vector2 scale)
    {
        ClipPoint[] transformed = new ClipPoint[points.Length];
        float cos = MathF.Cos(rotation);
        float sin = MathF.Sin(rotation);

        for (int i = 0; i < points.Length; i++)
        {
            float x = points[i].X * scale.X;
            float y = points[i].Y * scale.Y;

            transformed[i] = new ClipPoint(
                x * cos - y * sin + translation.X,
                x * sin + y * cos + translation.Y);
        }

        return transformed;
    }

    private static ClipPoint[] Copy(ClipPoint[] source, int count)
    {
        ClipPoint[] copy = new ClipPoint[count];
        Array.Copy(source, copy, count);
        return copy;
    }

    private static float Cross2D(in ClipPoint left, in ClipPoint right)
    {
        return left.X * right.Y - left.Y * right.X;
    }

    private static float SignedArea(ClipPoint[] polygon, int count)
    {
        if (count < 3) return 0.0f;

        float area = 0.0f;

        for (int i = 0; i < count; i++)
        {
            ClipPoint current = polygon[i];
            ClipPoint next = polygon[(i + 1) % count];
            area += Cross2D(current, next);
        }

        return area;
    }

    private static void ReversePolygon(ClipPoint[] polygon, int count)
    {
        for (int i = 0, j = count - 1; i < j; i++, j--)
        {
            (polygon[i], polygon[j]) = (polygon[j], polygon[i]);
        }
    }

    private static void NormalizeWinding(ClipPoint[] polygon, int count)
    {
        if (SignedArea(polygon, count) < 0.0f)
        {
            ReversePolygon(polygon, count);
        }
    }

    private static void CalculateClipTolerance(ClipPoint[] left, int leftCount,
        ClipPoint[] right, int rightCount,
        out float sideEpsilon, out float distanceEpsilonSq, out float areaEpsilon)
    {
        float scale = 1.0f;

        for (int i = 0; i < leftCount; i++)
        {
            scale = MathF.Max(scale, MathF.Max(MathF.Abs(left[i].X), MathF.Abs(left[i].Y)));
        }

        for (int i = 0; i < rightCount; i++)
        {
            scale = MathF.Max(scale, MathF.Max(MathF.Abs(right[i].X), MathF.Abs(right[i].Y)));
        }

        float distanceEpsilon = 1e-5f * scale + 1e-7f;
        distanceEpsilonSq = distanceEpsilon * distanceEpsilon;
        areaEpsilon = distanceEpsilon * scale;
        sideEpsilon = areaEpsilon;
    }

    private static void CompactPolygon(ClipPoint[] polygon, ref int count, float distanceEpsilonSq, float areaEpsilon)
    {
        if (count == 0) return;

        int write = 0;

        for (int i = 0; i < count; i++)
        {
            ClipPoint current = polygon[i];

            if (write > 0 && (polygon[write - 1] - current).LengthSquared() <= distanceEpsilonSq)
            {
                continue;
            }

            polygon[write++] = current;
        }

        if (write > 1 && (polygon[0] - polygon[write - 1]).LengthSquared() <= distanceEpsilonSq)
        {
            write -= 1;
        }

        count = write;
        if (count < 3) return;

        bool removed;

        do
        {
            removed = false;

            for (int i = 0; i < count; i++)
            {
                ClipPoint previous = polygon[(i + count - 1) % count];
                ClipPoint current = polygon[i];
                ClipPoint next = polygon[(i + 1) % count];

                ClipPoint edge0 = current - previous;
                ClipPoint edge1 = next - current;

                if (MathF.Abs(Cross2D(edge0, edge1)) > areaEpsilon) continue;

                for (int j = i; j < count - 1; j++)
                {
                    polygon[j] = polygon[j + 1];
                }

                count -= 1;
                removed = true;
                break;
            }
        }
        while (removed && count >= 3);
    }

    private static float SideOfEdge(in ClipPoint edgeStart, in ClipPoint edgeEnd, in ClipPoint point)
    {
        return Cross2D(edgeEnd - edgeStart, point - edgeStart);
    }

    private static ClipPoint IntersectSegmentsAgainstEdge(in ClipPoint edgeStart, in ClipPoint edgeEnd,
        in ClipPoint start, in ClipPoint end, float startSide, float endSide, float distanceEpsilonSq)
    {
        float denominator = startSide - endSide;

        if (MathF.Abs(denominator) <= distanceEpsilonSq)
        {
            return MathF.Abs(startSide) <= MathF.Abs(endSide) ? start : end;
        }

        float t = startSide / denominator;
        t = Math.Clamp(t, 0.0f, 1.0f);

        return start + t * (end - start);
    }

    private static int ClipConvexPolygon(ClipPoint[] subject, int subjectCount,
        ClipPoint[] clip, int clipCount, ClipPoint[] buffer,
        float sideEpsilon, float distanceEpsilonSq, float areaEpsilon,
        List<ClipStep> steps)
    {
        ClipPoint[] input = subject;
        ClipPoint[] output = buffer;
        int inputCount = subjectCount;
        bool resultInSubject = true;

        for (int edge = 0; edge < clipCount; edge++)
        {
            if (inputCount == 0) return 0;

            ClipPoint[] stepInput = Copy(input, inputCount);
            ClipPoint edgeStart = clip[edge];
            ClipPoint edgeEnd = clip[(edge + 1) % clipCount];
            int outputCount = 0;

            ClipPoint start = input[inputCount - 1];
            float startSide = SideOfEdge(edgeStart, edgeEnd, start);
            bool startInside = startSide >= -sideEpsilon;

            for (int i = 0; i < inputCount; i++)
            {
                ClipPoint end = input[i];
                float endSide = SideOfEdge(edgeStart, edgeEnd, end);
                bool endInside = endSide >= -sideEpsilon;

                if (startInside != endInside)
                {
                    output[outputCount++] = IntersectSegmentsAgainstEdge(
                        edgeStart, edgeEnd, start, end, startSide, endSide, distanceEpsilonSq);
                }

                if (endInside)
                {
                    output[outputCount++] = end;
                }

                start = end;
                startSide = endSide;
                startInside = endInside;
            }

            CompactPolygon(output, ref outputCount, distanceEpsilonSq, areaEpsilon);

            steps.Add(new ClipStep(
                $"Clip edge {edge + 1}/{clipCount}",
                outputCount == 0
                    ? "This edge rejected the current polygon completely."
                    : $"This pass produced {outputCount} output point(s).",
                stepInput,
                Copy(output, outputCount),
                edge,
                edgeStart,
                edgeEnd));

            ClipPoint[] temporary = input;
            input = output;
            output = temporary;
            inputCount = outputCount;
            resultInSubject = !resultInSubject;
        }

        if (!resultInSubject)
        {
            Array.Copy(input, subject, inputCount);
        }

        return inputCount;
    }

    private static void StoreLinearIntersection(in ClipPoint start, in ClipPoint end,
        float distanceEpsilonSq, ClipPoint[] clipped, out int clippedCount)
    {
        clipped[0] = start;
        clippedCount = 1;

        if ((end - start).LengthSquared() <= distanceEpsilonSq) return;

        clipped[1] = end;
        clippedCount = 2;
    }

    private static bool ClipSegmentAgainstPolygon(in ClipPoint segmentStart, in ClipPoint segmentEnd,
        ClipPoint[] polygon, int polygonCount,
        float sideEpsilon, float distanceEpsilonSq, ClipPoint[] clipped, out int clippedCount)
    {
        float enter = 0.0f;
        float exit = 1.0f;
        ClipPoint delta = segmentEnd - segmentStart;

        for (int edge = 0; edge < polygonCount; edge++)
        {
            ClipPoint edgeStart = polygon[edge];
            ClipPoint edgeEnd = polygon[(edge + 1) % polygonCount];

            float startSide = SideOfEdge(edgeStart, edgeEnd, segmentStart) + sideEpsilon;
            float endSide = SideOfEdge(edgeStart, edgeEnd, segmentEnd) + sideEpsilon;

            bool startInside = startSide >= 0.0f;
            bool endInside = endSide >= 0.0f;

            if (!startInside && !endInside)
            {
                clippedCount = 0;
                return false;
            }

            if (startInside && endInside) continue;

            float denominator = startSide - endSide;

            if (MathF.Abs(denominator) <= distanceEpsilonSq)
            {
                clippedCount = 0;
                return false;
            }

            float t = startSide / denominator;
            t = Math.Clamp(t, 0.0f, 1.0f);

            if (!startInside)
            {
                enter = MathF.Max(enter, t);
            }
            else
            {
                exit = MathF.Min(exit, t);
            }

            if (exit < enter)
            {
                clippedCount = 0;
                return false;
            }
        }

        StoreLinearIntersection(segmentStart + enter * delta, segmentStart + exit * delta,
            distanceEpsilonSq, clipped, out clippedCount);

        return true;
    }

    private static bool IntersectSegments(in ClipPoint leftStart, in ClipPoint leftEnd,
        in ClipPoint rightStart, in ClipPoint rightEnd,
        float sideEpsilon, float distanceEpsilonSq, float areaEpsilon,
        ClipPoint[] clipped, out int clippedCount)
    {
        ClipPoint leftDelta = leftEnd - leftStart;
        ClipPoint rightDelta = rightEnd - rightStart;
        ClipPoint offset = rightStart - leftStart;

        float cross = Cross2D(leftDelta, rightDelta);
        const float parameterEpsilon = 1e-5f;

        if (MathF.Abs(cross) <= areaEpsilon)
        {
            if (MathF.Abs(Cross2D(offset, leftDelta)) > areaEpsilon)
            {
                clippedCount = 0;
                return false;
            }

            bool useLeft = leftDelta.LengthSquared() >= rightDelta.LengthSquared();
            ClipPoint baseStart = useLeft ? leftStart : rightStart;
            ClipPoint baseDelta = useLeft ? leftDelta : rightDelta;

            bool useXAxis = MathF.Abs(baseDelta.X) >= MathF.Abs(baseDelta.Y);
            float baseOrigin = useXAxis ? baseStart.X : baseStart.Y;
            float baseExtent = useXAxis ? baseDelta.X : baseDelta.Y;

            if (MathF.Abs(baseExtent) <= sideEpsilon)
            {
                clippedCount = 0;
                return false;
            }

            float leftMin = MathF.Min(useXAxis ? leftStart.X : leftStart.Y, useXAxis ? leftEnd.X : leftEnd.Y);
            float leftMax = MathF.Max(useXAxis ? leftStart.X : leftStart.Y, useXAxis ? leftEnd.X : leftEnd.Y);
            float rightMin = MathF.Min(useXAxis ? rightStart.X : rightStart.Y, useXAxis ? rightEnd.X : rightEnd.Y);
            float rightMax = MathF.Max(useXAxis ? rightStart.X : rightStart.Y, useXAxis ? rightEnd.X : rightEnd.Y);

            float overlapMin = MathF.Max(leftMin, rightMin);
            float overlapMax = MathF.Min(leftMax, rightMax);

            if (overlapMax + sideEpsilon < overlapMin)
            {
                clippedCount = 0;
                return false;
            }

            float t0 = (overlapMin - baseOrigin) / baseExtent;
            float t1 = (overlapMax - baseOrigin) / baseExtent;

            StoreLinearIntersection(baseStart + t0 * baseDelta, baseStart + t1 * baseDelta,
                distanceEpsilonSq, clipped, out clippedCount);

            return true;
        }

        float t = Cross2D(offset, rightDelta) / cross;
        float u = Cross2D(offset, leftDelta) / cross;

        if (t < -parameterEpsilon || t > 1.0f + parameterEpsilon ||
            u < -parameterEpsilon || u > 1.0f + parameterEpsilon)
        {
            clippedCount = 0;
            return false;
        }

        t = Math.Clamp(t, 0.0f, 1.0f);
        clipped[0] = leftStart + t * leftDelta;
        clippedCount = 1;

        return true;
    }

    private static bool TryClipLinearIntersection(ClipPoint[] left, int leftCount,
        ClipPoint[] right, int rightCount,
        float sideEpsilon, float distanceEpsilonSq, float areaEpsilon,
        ClipPoint[] clipped, out int clippedCount)
    {
        if (leftCount < 2 || rightCount < 2)
        {
            clippedCount = 0;
            return false;
        }

        if (leftCount == 2 && rightCount == 2)
        {
            return IntersectSegments(left[0], left[1], right[0], right[1],
                sideEpsilon, distanceEpsilonSq, areaEpsilon, clipped, out clippedCount);
        }

        if (leftCount == 2)
        {
            return ClipSegmentAgainstPolygon(left[0], left[1], right, rightCount,
                sideEpsilon, distanceEpsilonSq, clipped, out clippedCount);
        }

        if (rightCount == 2)
        {
            return ClipSegmentAgainstPolygon(right[0], right[1], left, leftCount,
                sideEpsilon, distanceEpsilonSq, clipped, out clippedCount);
        }

        clippedCount = 0;
        return false;
    }

    private static void ReducePolygon(ClipPoint[] polygon, ref int count)
    {
        if (count <= FinalVertexLimit) return;

        ClipPoint[] reduced = new ClipPoint[FinalVertexLimit];

        for (int i = 0; i < FinalVertexLimit; i++)
        {
            int index = ((2 * i + 1) * count) / (2 * FinalVertexLimit);
            reduced[i] = polygon[index];
        }

        Array.Copy(reduced, polygon, FinalVertexLimit);
        count = FinalVertexLimit;
    }
}
