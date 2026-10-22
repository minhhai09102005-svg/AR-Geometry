using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class MockVertexOverlay : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RectTransform overlayArea;
    [SerializeField] private GameObject vertexLabelPrefab;

    private readonly List<GameObject> labels =
        new List<GameObject>();

    private Coroutine routine;

    public void StartMockVertices(int vertexCount)
    {
        ClearVertices();

        routine = StartCoroutine(
            SpawnVertices(vertexCount)
        );
    }

    public void StopMockVertices()
    {
        if (routine != null)
        {
            StopCoroutine(routine);
            routine = null;
        }

        ClearVertices();
    }

    private IEnumerator SpawnVertices(int count)
    {
        string[] names =
        {
            "A", "B", "C", "D",
            "E", "F", "G", "H"
        };

        Vector2[] positions =
        {
            new Vector2(-220,  250),
            new Vector2( 220,  250),
            new Vector2( 220, -150),
            new Vector2(-220, -150),

            new Vector2(-140,  380),
            new Vector2( 300,  380),
            new Vector2( 300,  -20),
            new Vector2(-140,  -20)
        };

        for (int i = 0; i < count; i++)
        {
            GameObject obj =
                Instantiate(
                    vertexLabelPrefab,
                    overlayArea
                );

            RectTransform rect =
                obj.GetComponent<RectTransform>();

            rect.anchoredPosition =
                positions[i];

            TMP_Text text =
                obj.GetComponent<TMP_Text>();

            text.text = names[i];

            labels.Add(obj);

            yield return new WaitForSeconds(0.5f);
        }

        routine = null;
    }

    private void ClearVertices()
    {
        foreach (GameObject obj in labels)
        {
            if (obj != null)
                Destroy(obj);
        }

        labels.Clear();
    }
}