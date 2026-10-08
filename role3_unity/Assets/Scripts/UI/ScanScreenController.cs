using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class ScanScreenController : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TMP_Text shapeNameText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text vertexText;
    [SerializeField] private TMP_Text edgeText;
    [SerializeField] private TMP_Text guideText;
    [SerializeField] private TMP_Text scanButtonText;

    [Header("Detection")]
    [SerializeField] private MockDetectionProvider mockDetection;

    private int expectedVertices;
    private int expectedEdges;

    private bool isScanning;

    private void Start()
    {
        SetupShape();
        ResetUI();
    }

    private void OnEnable()
    {
        if (mockDetection != null)
        {
            mockDetection.OnDetectionUpdated += UpdateDetectionUI;
        }
    }

    private void OnDisable()
    {
        if (mockDetection != null)
        {
            mockDetection.OnDetectionUpdated -= UpdateDetectionUI;
        }
    }

    private void SetupShape()
    {
        switch (GameSession.SelectedShape)
        {
            case ShapeType.Cube:
                shapeNameText.text = "HÌNH LẬP PHƯƠNG";
                expectedVertices = 8;
                expectedEdges = 12;
                break;

            case ShapeType.RectangularPrism:
                shapeNameText.text = "HÌNH HỘP CHỮ NHẬT";
                expectedVertices = 8;
                expectedEdges = 12;
                break;

            case ShapeType.SquarePyramid:
                shapeNameText.text = "HÌNH CHÓP TỨ GIÁC ĐỀU";
                expectedVertices = 5;
                expectedEdges = 8;
                break;
        }
    }

    private void ResetUI()
    {
        isScanning = false;

        statusText.text = "Trạng thái: Chưa quét";
        vertexText.text =
            $"Đỉnh phát hiện: 0 / {expectedVertices}";

        edgeText.text =
            $"Cạnh phát hiện: 0 / {expectedEdges}";

        guideText.text =
            "Đưa mô hình vào trong khung hình";

        scanButtonText.text =
            "BẮT ĐẦU QUÉT";
    }

    public void ToggleScan()
    {
        if (!isScanning)
            StartScan();
        else
            StopScan();
    }

    private void StartScan()
    {
        isScanning = true;

        scanButtonText.text =
            "DỪNG QUÉT";

        guideText.text =
            "Giữ mô hình ổn định";

        statusText.text =
            "Trạng thái: Đang khởi động...";

        mockDetection.StartMockDetection(
            expectedVertices,
            expectedEdges
        );
    }

    private void StopScan()
    {
        isScanning = false;

        mockDetection.StopMockDetection();

        statusText.text =
            "Trạng thái: Đã dừng quét";

        guideText.text =
            "Đưa mô hình vào trong khung hình";

        scanButtonText.text =
            "BẮT ĐẦU QUÉT";
    }

    private void UpdateDetectionUI(
        int vertices,
        int edges,
        string status)
    {
        statusText.text =
            "Trạng thái: " + status;

        vertexText.text =
            $"Đỉnh phát hiện: {vertices} / {expectedVertices}";

        edgeText.text =
            $"Cạnh phát hiện: {edges} / {expectedEdges}";

        if (vertices == expectedVertices &&
            edges == expectedEdges)
        {
            guideText.text =
                "Đã phát hiện đủ mô hình!";

            scanButtonText.text =
                "QUÉT LẠI";

            isScanning = false;
        }
    }

    public void BackToMenu()
    {
        if (mockDetection != null)
            mockDetection.StopMockDetection();

        SceneManager.LoadScene("MainMenu");
    }
}