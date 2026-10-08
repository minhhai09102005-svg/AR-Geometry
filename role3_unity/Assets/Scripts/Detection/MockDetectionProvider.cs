using System;
using System.Collections;
using UnityEngine;

public class MockDetectionProvider : MonoBehaviour
{
    public event Action<int, int, string> OnDetectionUpdated;

    private Coroutine detectionCoroutine;

    public void StartMockDetection(int maxVertices, int maxEdges)
    {
        StopMockDetection();

        detectionCoroutine =
            StartCoroutine(MockRoutine(maxVertices, maxEdges));
    }

    public void StopMockDetection()
    {
        if (detectionCoroutine != null)
        {
            StopCoroutine(detectionCoroutine);
            detectionCoroutine = null;
        }
    }

    private IEnumerator MockRoutine(int maxVertices, int maxEdges)
    {
        OnDetectionUpdated?.Invoke(
            0,
            0,
            "Đang tìm mô hình..."
        );

        yield return new WaitForSeconds(1f);

        OnDetectionUpdated?.Invoke(
            Mathf.Max(1, maxVertices / 4),
            Mathf.Max(1, maxEdges / 4),
            "Đang nhận diện..."
        );

        yield return new WaitForSeconds(1f);

        OnDetectionUpdated?.Invoke(
            Mathf.Max(1, maxVertices / 2),
            Mathf.Max(1, maxEdges / 2),
            "Đang nhận diện..."
        );

        yield return new WaitForSeconds(1f);

        OnDetectionUpdated?.Invoke(
            Mathf.Max(1, maxVertices - 1),
            Mathf.Max(1, maxEdges - 2),
            "Gần hoàn tất..."
        );

        yield return new WaitForSeconds(1f);

        OnDetectionUpdated?.Invoke(
            maxVertices,
            maxEdges,
            "Đã nhận diện mô hình"
        );

        detectionCoroutine = null;
    }
}