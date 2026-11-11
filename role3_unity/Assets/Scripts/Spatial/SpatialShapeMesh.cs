using System.Collections.Generic;
using UnityEngine;

public static class SpatialShapeMesh
{
    // Same order and silhouettes as ShapeDropFeature.Shape / InsideShape.
    public static Mesh Make(
        int shape, Vector2 size, float depth, bool hole, Vector2 panelSize)
    {
        Vector2[] outline = Outline(shape);

        for (int i = 0; i < outline.Length; i++)
            outline[i] = Vector2.Scale(outline[i], size) * 0.5f;

        Vector2[] outer = null;

        if (hole)
            outline = HoleOutline(outline, panelSize, out outer);

        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();

        Vector3 frontCentre = new Vector3(0f, 0f, -depth * 0.5f);
        Vector3 backCentre = new Vector3(0f, 0f, depth * 0.5f);

        for (int i = 0; i < outline.Length; i++)
        {
            int next = (i + 1) % outline.Length;
            Vector2 a = outline[i];
            Vector2 b = outline[next];

            Vector3 af = new Vector3(a.x, a.y, frontCentre.z);
            Vector3 bf = new Vector3(b.x, b.y, frontCentre.z);
            Vector3 ab = new Vector3(a.x, a.y, backCentre.z);
            Vector3 bb = new Vector3(b.x, b.y, backCentre.z);

            if (hole)
            {
                Vector3 outerAf = new Vector3(outer[i].x, outer[i].y, af.z);
                Vector3 outerBf = new Vector3(outer[next].x, outer[next].y, bf.z);
                Vector3 outerAb = new Vector3(outerAf.x, outerAf.y, ab.z);
                Vector3 outerBb = new Vector3(outerBf.x, outerBf.y, bb.z);

                Quad(vertices, triangles, outerBf, outerAf, af, bf);
                Quad(vertices, triangles, outerAb, outerBb, bb, ab);
                Quad(vertices, triangles, af, ab, bb, bf);
                Quad(vertices, triangles, outerAf, outerBf, outerBb, outerAb);
            }
            else
            {
                Triangle(vertices, triangles, frontCentre, bf, af);
                Triangle(vertices, triangles, backCentre, ab, bb);
                Quad(vertices, triangles, af, bf, bb, ab);
            }
        }

        Mesh mesh = new Mesh();
        mesh.name = hole ? "ShapeHole3D" : "ShapePiece3D";
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Vector2[] HoleOutline(
        Vector2[] shape, Vector2 panelSize, out Vector2[] outer)
    {
        // Include both shape corners and panel corners. Each angular sector
        // is a quad, leaving a real opening through the rectangular tile.
        List<float> angles = new List<float>();

        foreach (Vector2 point in shape)
            angles.Add(Mathf.Atan2(point.y, point.x));

        foreach (Vector2 corner in new[]
        {
            new Vector2(-1f, -1f), new Vector2(1f, -1f),
            new Vector2(1f, 1f), new Vector2(-1f, 1f)
        })
        {
            Vector2 point = Vector2.Scale(corner, panelSize);
            float angle = Mathf.Atan2(point.y, point.x);

            if (!angles.Exists(value => Mathf.Abs(value - angle) < 0.0001f))
                angles.Add(angle);
        }

        angles.Sort();
        Vector2[] inner = new Vector2[angles.Count];
        outer = new Vector2[angles.Count];

        for (int i = 0; i < angles.Count; i++)
        {
            Vector2 direction = new Vector2(Mathf.Cos(angles[i]), Mathf.Sin(angles[i]));
            float distance = float.PositiveInfinity;

            for (int edge = 0; edge < shape.Length; edge++)
            {
                Vector2 a = shape[edge];
                Vector2 segment = shape[(edge + 1) % shape.Length] - a;
                float denominator = Cross(direction, segment);

                if (Mathf.Abs(denominator) < 0.000001f)
                    continue;

                float t = Cross(a, segment) / denominator;
                float u = Cross(a, direction) / denominator;

                if (t > 0f && u >= -0.0001f && u <= 1.0001f)
                    distance = Mathf.Min(distance, t);
            }

            inner[i] = direction * distance;
            float outerDistance = Mathf.Min(
                panelSize.x * 0.5f / Mathf.Max(0.000001f, Mathf.Abs(direction.x)),
                panelSize.y * 0.5f / Mathf.Max(0.000001f, Mathf.Abs(direction.y))
            );
            outer[i] = direction * outerDistance;
        }

        return inner;
    }

    private static float Cross(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }

    private static Vector2[] Outline(int shape)
    {
        if (shape == 1)
        {
            return new[]
            {
                new Vector2(-0.88f, -0.84f),
                new Vector2(0.88f, -0.84f),
                new Vector2(0f, 0.87f)
            };
        }

        if (shape == 3)
        {
            Vector2[] circle = new Vector2[32];

            for (int i = 0; i < circle.Length; i++)
            {
                float angle = i * Mathf.PI * 2f / circle.Length;
                circle[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 0.85f;
            }

            return circle;
        }

        float x = shape == 2 ? 0.94f : 0.84f;
        float y = shape == 2 ? 0.61f : 0.84f;

        return new[]
        {
            new Vector2(-x, -y), new Vector2(x, -y),
            new Vector2(x, y), new Vector2(-x, y)
        };
    }

    private static void Triangle(
        List<Vector3> vertices,
        List<int> triangles,
        Vector3 a,
        Vector3 b,
        Vector3 c
    )
    {
        int index = vertices.Count;
        vertices.Add(a);
        vertices.Add(b);
        vertices.Add(c);
        triangles.Add(index);
        triangles.Add(index + 1);
        triangles.Add(index + 2);
    }

    private static void Quad(
        List<Vector3> vertices,
        List<int> triangles,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d
    )
    {
        Triangle(vertices, triangles, a, b, c);
        Triangle(vertices, triangles, a, c, d);
    }
}
