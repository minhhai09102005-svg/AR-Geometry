using System.Collections.Generic;
using UnityEngine;

public class MockEdgeOverlay : MonoBehaviour
{
    [SerializeField] private RectTransform overlayArea;
    [SerializeField] private GameObject edgePrefab;

    private readonly List<GameObject> edges =
        new List<GameObject>();

    public void DrawEdge(Vector2 a, Vector2 b)
    {
        if (overlayArea == null || edgePrefab == null)
        {
            Debug.LogError(
                "MockEdgeOverlay: Chưa gán Overlay Area hoặc Edge Prefab!"
            );
            return;
        }

        GameObject line =
            Instantiate(edgePrefab, overlayArea);

        RectTransform rect =
            line.GetComponent<RectTransform>();

        // Bắt cạnh dùng tâm DetectionOverlay làm gốc
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);

        Vector2 middle = (a + b) / 2f;
        Vector2 direction = b - a;

        rect.anchoredPosition = middle;

        rect.sizeDelta =
            new Vector2(direction.magnitude, 6f);

        float angle =
            Mathf.Atan2(
                direction.y,
                direction.x
            ) * Mathf.Rad2Deg;

        rect.localRotation =
            Quaternion.Euler(0f, 0f, angle);

        rect.localScale = Vector3.one;

        edges.Add(line);
    }

    public void ClearEdges()
    {
        foreach (GameObject edge in edges)
        {
            if (edge != null)
            {
                Destroy(edge);
            }
        }

        edges.Clear();
    }

    // =============================
    // HÌNH LẬP PHƯƠNG
    // =============================
    public void DrawCube()
    {
        ClearEdges();

        // Mặt trước
        Vector2 A = new Vector2(-200, 100);
        Vector2 B = new Vector2(120, 100);
        Vector2 C = new Vector2(120, -140);
        Vector2 D = new Vector2(-200, -140);

        // Mặt sau
        Vector2 E = new Vector2(-100, 210);
        Vector2 F = new Vector2(220, 210);
        Vector2 G = new Vector2(220, -30);
        Vector2 H = new Vector2(-100, -30);

        // 4 cạnh mặt trước
        DrawEdge(A, B);
        DrawEdge(B, C);
        DrawEdge(C, D);
        DrawEdge(D, A);

        // 4 cạnh mặt sau
        DrawEdge(E, F);
        DrawEdge(F, G);
        DrawEdge(G, H);
        DrawEdge(H, E);

        // 4 cạnh nối
        DrawEdge(A, E);
        DrawEdge(B, F);
        DrawEdge(C, G);
        DrawEdge(D, H);
    }
}