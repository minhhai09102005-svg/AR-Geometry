
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class ScanScreenController : MonoBehaviour
{
    [Header("Existing UI")]
    [SerializeField] private TMP_Text shapeNameText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text vertexText;
    [SerializeField] private TMP_Text edgeText;
    [SerializeField] private TMP_Text guideText;
    [SerializeField] private TMP_Text scanButtonText;

    [Header("Existing mock references")]
    [SerializeField] private MockDetectionProvider mockDetection;
    [SerializeField] private MockVertexOverlay mockVertexOverlay;
    [SerializeField] private MockEdgeOverlay mockEdgeOverlay;

    [Header("Hand interaction")]
    [SerializeField] private float vertexGrabPixels = 35f;
    [SerializeField] private float edgeGrabPixels = 20f;
    [SerializeField] private float inventorySnapPixels = 75f;

    [Header("3D Rotation")]
    [SerializeField] private float rotationSensitivity = 0.24f;

    private class RodGroup
    {
        public string label;
        public float length;
        public int total;
        public int remaining;
        public Color color;
        public RectTransform slot;
        public TMP_Text countLabel;
    }

    [Serializable]
    private class Role2Result
    {
        public string source;
        public string type;
        public string session_id;
        public string shape;
        public string status;
        public bool is_correct;
        public string message;
        public int frame_id;
        public int vertex_count;
        public int edge_count;
    }

    private enum ControlMode
    {
        None,
        Rotate,
        DragRod
    }

    private enum ControlSource
    {
        None,
        MouseTouch,
        Hand
    }

    private readonly List<RodGroup> rods =
        new List<RodGroup>();

    private readonly Dictionary<string, Vector3> positions =
        new Dictionary<string, Vector3>();

    private readonly Dictionary<string, TMP_Text> labels =
        new Dictionary<string, TMP_Text>();

    private readonly Dictionary<string, List<LineRenderer>> guides =
        new Dictionary<string, List<LineRenderer>>();

    private readonly Dictionary<string, GameObject> installed =
        new Dictionary<string, GameObject>();

    private readonly Dictionary<string, int> rodTypeByEdge =
        new Dictionary<string, int>();

    private readonly List<ARRaycastHit> arHits =
        new List<ARRaycastHit>();

    private readonly List<RaycastResult> uiHits =
        new List<RaycastResult>();

    private readonly Vector3[] screenCorners =
        new Vector3[4];

    private string[] vertexIds;
    private string[][] expectedEdges;

    private string sessionId = "";
    private string highlightedEdge;
    private string activeHandName;

    private int frameId;
    private int selectedRod = -1;

    private bool running;
    private float lastHandPacketTime;

    private Vector2 previousPointer;

    private ControlMode mode = ControlMode.None;
    private ControlSource source = ControlSource.None;

    private Canvas canvas;
    private Camera viewCamera;
    private ARRaycastManager arRaycaster;

    private GameObject modelRoot;

    private RectTransform trayPanel;
    private RectTransform dragIcon;
    private Image dragIconImage;

    private Material nodeMaterial;
    private Material guideMaterial;
    private Material[] rodMaterials;

    private float modelWidth;
    private float modelDepth;
    private float modelHeight;

    private void OnEnable()
    {
        HandDataReceiver.PacketReceived += ReceiveHands;
        HandDataReceiver.GeometryResultReceived += ApplyGeometryResultJson;
        HandDataReceiver.ConnectionChanged += OnConnectionChanged;
    }

    private void OnDisable()
    {
        HandDataReceiver.PacketReceived -= ReceiveHands;
        HandDataReceiver.GeometryResultReceived -= ApplyGeometryResultJson;
        HandDataReceiver.ConnectionChanged -= OnConnectionChanged;
    }

    private void Start()
    {
        viewCamera = Camera.main;
        canvas = FindAnyObjectByType<Canvas>();
        arRaycaster = FindAnyObjectByType<ARRaycastManager>();

        if (mockDetection != null)
            mockDetection.StopMockDetection();

        if (mockVertexOverlay != null)
            mockVertexOverlay.StopMockVertices();

        if (mockEdgeOverlay != null)
            mockEdgeOverlay.ClearEdges();

        GameObject overlay = GameObject.Find("DetectionOverlay");

        if (overlay != null)
            overlay.SetActive(false);

        ARObjectPlacement oldPlacement =
            FindAnyObjectByType<ARObjectPlacement>();

        if (oldPlacement != null)
            oldPlacement.enabled = false;

        ConfigureShape();
        MakeMaterials();
        BuildTray();
        ResetScreen();

        if (FindAnyObjectByType<HandDataReceiver>() == null)
        {
            new GameObject("SmartGeoAR_BackendClient")
                .AddComponent<HandDataReceiver>();
        }
    }

    private void OnDestroy()
    {
        ClearModel();

        if (nodeMaterial != null)
            Destroy(nodeMaterial);

        if (guideMaterial != null)
            Destroy(guideMaterial);

        if (rodMaterials != null)
        {
            foreach (Material material in rodMaterials)
            {
                if (material != null)
                    Destroy(material);
            }
        }
    }

    // =====================================================
    // SHAPE CONFIGURATION
    // =====================================================

    private void ConfigureShape()
    {
        modelWidth = 0.32f;
        modelDepth = 0.32f;
        modelHeight = 0.32f;

        string[][] boxEdges =
        {
            new[] { "A", "B" },
            new[] { "B", "C" },
            new[] { "C", "D" },
            new[] { "D", "A" },
            new[] { "E", "F" },
            new[] { "F", "G" },
            new[] { "G", "H" },
            new[] { "H", "E" },
            new[] { "A", "E" },
            new[] { "B", "F" },
            new[] { "C", "G" },
            new[] { "D", "H" }
        };

        rods.Clear();

        if (GameSession.SelectedShape == ShapeType.Cube)
        {
            shapeNameText.text = "HÌNH LẬP PHƯƠNG";

            vertexIds = new[]
            {
                "A", "B", "C", "D",
                "E", "F", "G", "H"
            };

            expectedEdges = boxEdges;

            AddRod(
                "Cạnh a",
                modelWidth,
                12,
                new Color(0.10f, 0.93f, 0.79f)
            );
        }
        else if (GameSession.SelectedShape == ShapeType.RectangularPrism)
        {
            shapeNameText.text = "HÌNH HỘP CHỮ NHẬT";

            modelWidth = 0.42f;
            modelDepth = 0.26f;
            modelHeight = 0.34f;

            vertexIds = new[]
            {
                "A", "B", "C", "D",
                "E", "F", "G", "H"
            };

            expectedEdges = boxEdges;

            AddRod(
                "Cạnh dài", modelWidth, 4,
                new Color(0.12f, 0.87f, 0.96f)
            );

            AddRod(
                "Cạnh rộng", modelDepth, 4,
                new Color(1f, 0.68f, 0.25f)
            );

            AddRod(
                "Cạnh cao", modelHeight, 4,
                new Color(0.60f, 0.77f, 1f)
            );
        }
        else
        {
            shapeNameText.text = "HÌNH CHÓP TỨ GIÁC ĐỀU";

            modelWidth = 0.30f;
            modelDepth = 0.30f;
            modelHeight = 0.36f;

            vertexIds = new[]
            {
                "A", "B", "C", "D", "S"
            };

            expectedEdges = new[]
            {
                new[] { "A", "B" },
                new[] { "B", "C" },
                new[] { "C", "D" },
                new[] { "D", "A" },
                new[] { "S", "A" },
                new[] { "S", "B" },
                new[] { "S", "C" },
                new[] { "S", "D" }
            };

            AddRod(
                "Cạnh đáy",
                modelWidth,
                4,
                new Color(0.10f, 0.93f, 0.79f)
            );

            float sideLength = Mathf.Sqrt(
                modelHeight * modelHeight +
                modelWidth * modelWidth / 2f
            );

            AddRod(
                "Cạnh bên",
                sideLength,
                4,
                new Color(1f, 0.66f, 0.24f)
            );
        }
    }

    private void AddRod(
        string label,
        float length,
        int count,
        Color color
    )
    {
        rods.Add(new RodGroup
        {
            label = label,
            length = length,
            total = count,
            remaining = count,
            color = color
        });
    }

    private string ShapeCode()
    {
        if (GameSession.SelectedShape == ShapeType.RectangularPrism)
            return "rectangular_prism";

        if (GameSession.SelectedShape == ShapeType.SquarePyramid)
            return "square_pyramid";

        return "cube";
    }

    // =====================================================
    // MATERIALS
    // =====================================================

    private void MakeMaterials()
    {
        Material template =
            Resources.Load<Material>("SmartGeoAR_Unlit");

        Shader shader = template != null
            ? template.shader
            : Shader.Find("Universal Render Pipeline/Unlit");

        if (shader == null)
        {
            UnityEngine.Debug.LogError(
                "SmartGeoAR: Missing Unlit shader"
            );
            return;
        }

        nodeMaterial = template != null
            ? new Material(template)
            : new Material(shader);

        guideMaterial = template != null
            ? new Material(template)
            : new Material(shader);

        SetColor(
            nodeMaterial,
            new Color(0.05f, 0.94f, 0.81f)
        );

        SetColor(
            guideMaterial,
            new Color(0.47f, 0.50f, 0.55f)
        );

        rodMaterials = new Material[rods.Count];

        for (int i = 0; i < rods.Count; i++)
        {
            rodMaterials[i] = template != null
                ? new Material(template)
                : new Material(shader);

            SetColor(rodMaterials[i], rods[i].color);
        }
    }

    private static void SetColor(Material material, Color color)
    {
        if (material == null)
            return;

        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);

        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
    }

    // =====================================================
    // UI
    // =====================================================

    private static RectTransform MakePanel(
        string name,
        Transform parent,
        Vector2 size,
        Color color
    )
    {
        GameObject go = new GameObject(
            name,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image)
        );

        go.transform.SetParent(parent, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;

        Image image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;

        return rect;
    }

    private static TextMeshProUGUI AddText(
        Transform parent,
        string content,
        Vector2 offset,
        Vector2 size,
        float fontSize,
        Color color
    )
    {
        GameObject go = new GameObject(
            "Text",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI)
        );

        go.transform.SetParent(parent, false);

        RectTransform rect = go.GetComponent<RectTransform>();

        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = offset;
        rect.sizeDelta = size;

        TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();

        text.text = content;
        text.fontSize = fontSize;
        text.alignment = TextAlignmentOptions.Center;
        text.color = color;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;

        return text;
    }

    private void BuildTray()
    {
        if (canvas == null)
            return;

        float height = 122f + rods.Count * 86f;

        trayPanel = MakePanel(
            "KhoThanhNoi",
            canvas.transform,
            new Vector2(292f, height),
            new Color(0.03f, 0.10f, 0.18f, 0.87f)
        );

        trayPanel.anchorMin = new Vector2(1f, 0.5f);
        trayPanel.anchorMax = new Vector2(1f, 0.5f);
        trayPanel.pivot = new Vector2(1f, 0.5f);
        trayPanel.anchoredPosition = new Vector2(-14f, 0f);

        AddText(
            trayPanel,
            "KHO THANH NỐI",
            new Vector2(0f, height / 2f - 30f),
            new Vector2(265f, 36f),
            23f,
            Color.white
        );

        AddText(
            trayPanel,
            "Chụm tay vào thanh rồi kéo tới cạnh",
            new Vector2(0f, height / 2f - 59f),
            new Vector2(280f, 33f),
            14f,
            new Color(0.76f, 0.85f, 0.94f)
        );

        float maxLength = 0.01f;

        foreach (RodGroup rod in rods)
            maxLength = Mathf.Max(maxLength, rod.length);

        for (int i = 0; i < rods.Count; i++)
        {
            RodGroup rod = rods[i];

            RectTransform card = MakePanel(
                "Thanh_" + i,
                trayPanel,
                new Vector2(265f, 75f),
                new Color(0.16f, 0.22f, 0.32f, 0.93f)
            );

            card.anchoredPosition = new Vector2(
                0f,
                height / 2f - 110f - i * 86f
            );

            rod.slot = card;

            AddText(
                card,
                rod.label,
                new Vector2(-18f, 19f),
                new Vector2(170f, 28f),
                20f,
                Color.white
            );

            rod.countLabel = AddText(
                card,
                "",
                new Vector2(100f, 19f),
                new Vector2(57f, 28f),
                19f,
                rod.color
            );

            RectTransform stick = MakePanel(
                "MinhHoaThanh",
                card,
                new Vector2(
                    55f + 105f * rod.length / maxLength,
                    11f
                ),
                rod.color
            );

            stick.anchoredPosition = new Vector2(-32f, -14f);
        }

        AddText(
            trayPanel,
            "Nắm đỉnh/cạnh để xoay 3D",
            new Vector2(0f, -height / 2f + 22f),
            new Vector2(275f, 33f),
            15f,
            Color.white
        );

        dragIcon = MakePanel(
            "ThanhDangGap",
            canvas.transform,
            new Vector2(125f, 14f),
            Color.white
        );

        dragIconImage = dragIcon.GetComponent<Image>();
        dragIcon.gameObject.SetActive(false);

        RefreshInventory();
    }

    private void RefreshInventory()
    {
        foreach (RodGroup rod in rods)
        {
            if (rod.countLabel != null)
                rod.countLabel.text =
                    rod.remaining + "/" + rod.total;
        }
    }

    private bool SetUIPosition(
        RectTransform rect,
        Vector2 screenPoint
    )
    {
        if (rect == null || canvas == null)
            return false;

        RectTransform canvasRect =
            canvas.transform as RectTransform;

        Camera uiCamera =
            canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : canvas.worldCamera;

        Vector2 local;

        if (!RectTransformUtility
            .ScreenPointToLocalPointInRectangle(
                canvasRect,
                screenPoint,
                uiCamera,
                out local
            ))
            return false;

        rect.anchoredPosition = local;
        return true;
    }

    private bool InTray(Vector2 point)
    {
        return trayPanel != null &&
            RectTransformUtility.RectangleContainsScreenPoint(
                trayPanel,
                point,
                null
            );
    }

    private int GetRodAt(Vector2 point, bool isHand)
    {
        int bestIndex = -1;
        float bestDistance = float.PositiveInfinity;

        for (int i = 0; i < rods.Count; i++)
        {
            RodGroup rod = rods[i];

            if (rod.remaining <= 0 || rod.slot == null)
                continue;

            if (RectTransformUtility.RectangleContainsScreenPoint(
                rod.slot,
                point,
                null
            ))
                return i;

            if (!isHand)
                continue;

            rod.slot.GetWorldCorners(screenCorners);

            Vector2 bottomLeft =
                RectTransformUtility.WorldToScreenPoint(
                    null, screenCorners[0]
                );

            Vector2 topRight =
                RectTransformUtility.WorldToScreenPoint(
                    null, screenCorners[2]
                );

            float left = Mathf.Min(bottomLeft.x, topRight.x);
            float right = Mathf.Max(bottomLeft.x, topRight.x);
            float bottom = Mathf.Min(bottomLeft.y, topRight.y);
            float top = Mathf.Max(bottomLeft.y, topRight.y);

            float dx = Mathf.Max(
                left - point.x,
                point.x - right,
                0f
            );

            float dy = Mathf.Max(
                bottom - point.y,
                point.y - top,
                0f
            );

            if (dx <= inventorySnapPixels && dy <= 24f)
            {
                float distance = dx * dx + dy * dy;

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = i;
                }
            }
        }

        return bestIndex;
    }

    private bool OnButton(Vector2 point)
    {
        if (EventSystem.current == null)
            return false;

        PointerEventData pointer =
            new PointerEventData(EventSystem.current);

        pointer.position = point;
        uiHits.Clear();

        EventSystem.current.RaycastAll(pointer, uiHits);

        foreach (RaycastResult hit in uiHits)
        {
            if (hit.gameObject != null &&
                hit.gameObject.GetComponentInParent<Button>() != null)
                return true;
        }

        return false;
    }

    // =====================================================
    // SCAN
    // =====================================================

    private void ResetScreen()
    {
        running = false;

        statusText.text = "Trạng thái: Chưa bắt đầu";
        vertexText.text = "Đỉnh: 0/" + vertexIds.Length;
        edgeText.text = "Cạnh đã lắp: 0/" + expectedEdges.Length;
        guideText.text = "Bấm BẮT ĐẦU QUÉT để dựng khung 3D";
        scanButtonText.text = "BẮT ĐẦU QUÉT";
    }

    public void ToggleScan()
    {
        if (running)
            StopScan();

        StartScan();
    }

    private void StartScan()
    {
        ClearModel();

        foreach (RodGroup rod in rods)
            rod.remaining = rod.total;

        RefreshInventory();

        sessionId = Guid.NewGuid().ToString("N");
        frameId = 0;
        running = true;

        if (viewCamera == null)
            viewCamera = Camera.main;

        if (viewCamera == null)
        {
            running = false;
            guideText.text = "Không tìm thấy Main Camera";
            return;
        }

        modelRoot = new GameObject(
            "SmartGeoAR_" + ShapeCode()
        );

        bool onPlane = false;

        Vector2 aim = new Vector2(
            Screen.width * 0.43f,
            Screen.height * 0.5f
        );

        if (arRaycaster != null &&
            arRaycaster.enabled &&
            arRaycaster.Raycast(
                aim,
                arHits,
                TrackableType.PlaneWithinPolygon
            ) &&
            arHits.Count > 0)
        {
            modelRoot.transform.position =
                arHits[0].pose.position;

            onPlane = true;
        }
        else
        {
            modelRoot.transform.position =
                viewCamera.transform.position +
                viewCamera.transform.forward * 0.85f -
                viewCamera.transform.right * 0.15f -
                viewCamera.transform.up * 0.08f;
        }

        modelRoot.transform.rotation =
            Quaternion.Euler(
                16f,
                viewCamera.transform.eulerAngles.y + 25f,
                0f
            );

        MakeModelPoints();
        MakeGuides();
        MakeVertexLabels();

        vertexText.text =
            "Đỉnh: " + vertexIds.Length + "/" + vertexIds.Length;

        edgeText.text =
            "Cạnh đã lắp: 0/" + expectedEdges.Length;

        statusText.text = onPlane
            ? "Trạng thái: Đã đặt khung AR"
            : "Trạng thái: Khung 3D trước camera";

        guideText.text =
            "Gắp thanh để lắp; nắm cạnh để xoay tự do";

        scanButtonText.text = "LÀM LẠI";

        SendGraphToRole2();
    }

    private void StopScan()
    {
        ClearModel();
        ResetScreen();
    }

    // =====================================================
    // MODEL GEOMETRY
    // =====================================================

    private void MakeModelPoints()
    {
        positions.Clear();

        positions["A"] = new Vector3(
            -modelWidth / 2f, 0f, -modelDepth / 2f
        );

        positions["B"] = new Vector3(
            modelWidth / 2f, 0f, -modelDepth / 2f
        );

        positions["C"] = new Vector3(
            modelWidth / 2f, 0f, modelDepth / 2f
        );

        positions["D"] = new Vector3(
            -modelWidth / 2f, 0f, modelDepth / 2f
        );

        if (GameSession.SelectedShape == ShapeType.SquarePyramid)
        {
            positions["S"] = new Vector3(
                0f, modelHeight, 0f
            );
        }
        else
        {
            positions["E"] =
                positions["A"] + Vector3.up * modelHeight;

            positions["F"] =
                positions["B"] + Vector3.up * modelHeight;

            positions["G"] =
                positions["C"] + Vector3.up * modelHeight;

            positions["H"] =
                positions["D"] + Vector3.up * modelHeight;
        }

        foreach (string id in vertexIds)
        {
            GameObject point =
                GameObject.CreatePrimitive(PrimitiveType.Sphere);

            point.name = "Dinh_" + id;

            point.transform.SetParent(
                modelRoot.transform,
                false
            );

            point.transform.localPosition = positions[id];
            point.transform.localScale = Vector3.one * 0.046f;

            point.GetComponent<Renderer>()
                .sharedMaterial = nodeMaterial;
        }

        rodTypeByEdge.Clear();

        foreach (string[] pair in expectedEdges)
        {
            float length = Vector3.Distance(
                positions[pair[0]],
                positions[pair[1]]
            );

            int nearest = 0;
            float best = float.PositiveInfinity;

            for (int i = 0; i < rods.Count; i++)
            {
                float difference =
                    Mathf.Abs(rods[i].length - length);

                if (difference < best)
                {
                    best = difference;
                    nearest = i;
                }
            }

            rodTypeByEdge[EdgeKey(pair[0], pair[1])] = nearest;
        }
    }

    private void MakeGuides()
    {
        guides.Clear();

        foreach (string[] pair in expectedEdges)
        {
            string key = EdgeKey(pair[0], pair[1]);

            List<LineRenderer> segments =
                new List<LineRenderer>();

            Vector3 a = positions[pair[0]];
            Vector3 b = positions[pair[1]];

            for (int i = 0; i < 9; i++)
            {
                float start = i / 9f;
                float end = Mathf.Min(start + 0.055f, 1f);

                GameObject go =
                    new GameObject("NetDut_" + key + "_" + i);

                go.transform.SetParent(
                    modelRoot.transform,
                    false
                );

                LineRenderer line =
                    go.AddComponent<LineRenderer>();

                line.useWorldSpace = false;
                line.positionCount = 2;

                line.SetPosition(
                    0,
                    Vector3.Lerp(a, b, start)
                );

                line.SetPosition(
                    1,
                    Vector3.Lerp(a, b, end)
                );

                line.startWidth = 0.004f;
                line.endWidth = 0.004f;
                line.sharedMaterial = guideMaterial;

                segments.Add(line);
            }

            guides[key] = segments;
        }
    }

    private void MakeVertexLabels()
    {
        if (canvas == null)
            return;

        foreach (string id in vertexIds)
        {
            TextMeshProUGUI label = AddText(
                canvas.transform,
                id,
                Vector2.zero,
                new Vector2(48f, 48f),
                25f,
                Color.white
            );

            label.fontStyle = FontStyles.Bold;
            labels[id] = label;
        }
    }

    private void UpdateVertexLabels()
    {
        if (modelRoot == null ||
            canvas == null ||
            viewCamera == null)
            return;

        foreach (string id in vertexIds)
        {
            if (!labels.ContainsKey(id))
                continue;

            Vector3 world =
                modelRoot.transform.TransformPoint(
                    positions[id] + Vector3.up * 0.042f
                );

            Vector3 screen =
                viewCamera.WorldToScreenPoint(world);

            labels[id].gameObject.SetActive(screen.z > 0f);

            if (screen.z > 0f)
            {
                SetUIPosition(
                    labels[id].rectTransform,
                    screen
                );
            }
        }
    }

    // =====================================================
    // HIT TESTING
    // =====================================================

    private static string EdgeKey(string a, string b)
    {
        return string.CompareOrdinal(a, b) < 0
            ? a + "-" + b
            : b + "-" + a;
    }

    private bool ProjectVertex(
        string id,
        out Vector2 point
    )
    {
        point = Vector2.zero;

        if (modelRoot == null ||
            viewCamera == null ||
            !positions.ContainsKey(id))
            return false;

        Vector3 world =
            modelRoot.transform.TransformPoint(positions[id]);

        Vector3 screen =
            viewCamera.WorldToScreenPoint(world);

        if (screen.z <= 0f)
            return false;

        point = new Vector2(screen.x, screen.y);
        return true;
    }

    private static float DistanceToSegment(
        Vector2 p,
        Vector2 a,
        Vector2 b
    )
    {
        Vector2 ab = b - a;

        float t = Mathf.Clamp01(
            Vector2.Dot(p - a, ab) /
            Mathf.Max(0.0001f, ab.sqrMagnitude)
        );

        return Vector2.Distance(
            p,
            a + t * ab
        );
    }

    private bool IsOnStructure(Vector2 point)
    {
        foreach (string id in vertexIds)
        {
            Vector2 vertex;

            if (ProjectVertex(id, out vertex) &&
                Vector2.Distance(point, vertex) <= vertexGrabPixels)
                return true;
        }

        foreach (string[] pair in expectedEdges)
        {
            Vector2 a;
            Vector2 b;

            if (ProjectVertex(pair[0], out a) &&
                ProjectVertex(pair[1], out b) &&
                DistanceToSegment(point, a, b) <= edgeGrabPixels)
                return true;
        }

        return false;
    }

    private string ClosestOpenEdge(Vector2 point)
    {
        float best = Mathf.Max(
            55f,
            Screen.height * 0.09f
        );

        string selected = null;

        foreach (string[] pair in expectedEdges)
        {
            string key = EdgeKey(pair[0], pair[1]);

            if (installed.ContainsKey(key))
                continue;

            Vector2 a;
            Vector2 b;

            if (!ProjectVertex(pair[0], out a) ||
                !ProjectVertex(pair[1], out b))
                continue;

            float distance = DistanceToSegment(point, a, b);

            if (distance < best)
            {
                best = distance;
                selected = key;
            }
        }

        return selected;
    }

    private void SetHighlight(string edge)
    {
        if (highlightedEdge == edge)
            return;

        if (highlightedEdge != null &&
            guides.ContainsKey(highlightedEdge))
        {
            foreach (LineRenderer line in guides[highlightedEdge])
            {
                line.startColor = Color.white;
                line.endColor = Color.white;
            }
        }

        highlightedEdge = edge;

        if (edge == null || !guides.ContainsKey(edge))
            return;

        Color color =
            rodTypeByEdge[edge] == selectedRod
                ? Color.green
                : Color.red;

        foreach (LineRenderer line in guides[edge])
        {
            line.startColor = color;
            line.endColor = color;
        }
    }

    private void UpdateGhost(Vector2 point)
    {
        if (selectedRod < 0 || dragIcon == null)
            return;

        SetUIPosition(dragIcon, point);

        string edge = ClosestOpenEdge(point);

        SetHighlight(edge);

        dragIconImage.color =
            edge != null && rodTypeByEdge[edge] == selectedRod
                ? Color.green
                : rods[selectedRod].color;
    }

    // =====================================================
    // INSTALL ROD
    // =====================================================

    private void InstallRod(string edge, int index)
    {
        if (installed.ContainsKey(edge))
            return;

        string[] pair = edge.Split('-');

        Vector3 a = positions[pair[0]];
        Vector3 b = positions[pair[1]];
        Vector3 direction = b - a;

        GameObject cylinder =
            GameObject.CreatePrimitive(PrimitiveType.Cylinder);

        cylinder.name = "ThanhDaLap_" + edge;

        cylinder.transform.SetParent(
            modelRoot.transform,
            false
        );

        cylinder.transform.localPosition = (a + b) * 0.5f;

        cylinder.transform.localRotation =
            Quaternion.FromToRotation(
                Vector3.up,
                direction
            );

        cylinder.transform.localScale = new Vector3(
            0.018f,
            direction.magnitude * 0.5f,
            0.018f
        );

        cylinder.GetComponent<Renderer>()
            .sharedMaterial = rodMaterials[index];

        installed[edge] = cylinder;
        rods[index].remaining--;

        RefreshInventory();

        foreach (LineRenderer line in guides[edge])
            line.gameObject.SetActive(false);

        frameId++;

        edgeText.text =
            "Cạnh đã lắp: " +
            installed.Count + "/" + expectedEdges.Length;

        statusText.text = "ĐANG KIỂM TRA...";

        guideText.text =
            "Đã lắp cạnh " + edge + ". Đang kiểm tra...";

        SendGraphToRole2();
    }

    // =====================================================
    // ROTATION AROUND GEOMETRIC CENTER
    // =====================================================

    private Vector3 GetLocalGeometricCenter()
    {
        if (vertexIds == null || vertexIds.Length == 0)
            return Vector3.zero;

        Vector3 center = Vector3.zero;
        int count = 0;

        foreach (string id in vertexIds)
        {
            Vector3 vertex;

            if (positions.TryGetValue(id, out vertex))
            {
                center += vertex;
                count++;
            }
        }

        if (count == 0)
            return Vector3.zero;

        return center / count;
    }

    private void RotateModelAroundCenter(Vector2 screenDelta)
    {
        if (modelRoot == null || viewCamera == null)
            return;

        Transform model = modelRoot.transform;

        Vector3 localCenter = GetLocalGeometricCenter();

        Vector3 fixedWorldCenter =
            model.TransformPoint(localCenter);

        Quaternion yaw = Quaternion.AngleAxis(
            -screenDelta.x * rotationSensitivity,
            viewCamera.transform.up
        );

        Quaternion pitch = Quaternion.AngleAxis(
            screenDelta.y * rotationSensitivity,
            viewCamera.transform.right
        );

        model.rotation = yaw * pitch * model.rotation;

        Vector3 centerAfterRotation =
            model.TransformPoint(localCenter);

        model.position +=
            fixedWorldCenter - centerAfterRotation;
    }

    // =====================================================
    // POINTER INTERACTION
    // =====================================================

    private void BeginPointer(
        Vector2 point,
        ControlSource kind
    )
    {
        if (!running ||
            source != ControlSource.None ||
            modelRoot == null ||
            OnButton(point))
            return;

        bool isHand = kind == ControlSource.Hand;
        int rodIndex = GetRodAt(point, isHand);

        if (rodIndex >= 0)
        {
            source = kind;
            mode = ControlMode.DragRod;
            selectedRod = rodIndex;

            if (dragIcon != null)
            {
                dragIcon.gameObject.SetActive(true);

                dragIcon.sizeDelta = new Vector2(
                    100f + rods[rodIndex].length * 140f,
                    14f
                );

                dragIconImage.color = rods[rodIndex].color;
                UpdateGhost(point);
            }

            guideText.text =
                "Đang gắp " + rods[rodIndex].label;

            return;
        }

        if (InTray(point))
            return;

        if (!IsOnStructure(point))
            return;

        source = kind;
        mode = ControlMode.Rotate;
        previousPointer = point;
    }

    private void MovePointer(
        Vector2 point,
        ControlSource kind
    )
    {
        if (source != kind || modelRoot == null)
            return;

        if (mode == ControlMode.DragRod)
        {
            UpdateGhost(point);
            return;
        }

        if (mode != ControlMode.Rotate)
            return;

        Vector2 delta = point - previousPointer;

        RotateModelAroundCenter(delta);

        previousPointer = point;
    }

    private void EndPointer(
        Vector2 point,
        ControlSource kind
    )
    {
        if (source != kind)
            return;

        if (mode == ControlMode.DragRod &&
            selectedRod >= 0)
        {
            string edge = ClosestOpenEdge(point);

            if (edge == null)
            {
                guideText.text =
                    "Chưa thả đúng cạnh nét đứt";
            }
            else if (rodTypeByEdge[edge] != selectedRod)
            {
                guideText.text =
                    "Sai loại thanh, chọn thanh khác";
            }
            else
            {
                InstallRod(edge, selectedRod);
            }
        }

        ResetPointer();
    }

    private void ResetPointer()
    {
        if (dragIcon != null)
            dragIcon.gameObject.SetActive(false);

        SetHighlight(null);

        selectedRod = -1;
        mode = ControlMode.None;
        source = ControlSource.None;
    }

    // =====================================================
    // MOUSE / TOUCH
    // =====================================================

    private void Update()
    {
        if (!running)
            return;

        if (viewCamera == null)
            viewCamera = Camera.main;

        UpdateVertexLabels();

        if (lastHandPacketTime > 0f &&
            Time.unscaledTime - lastHandPacketTime > 0.6f)
        {
            if (source == ControlSource.Hand)
                ResetPointer();

            activeHandName = null;
            lastHandPacketTime = 0f;
        }

        if (Touchscreen.current != null &&
            (
                Touchscreen.current.primaryTouch.press.isPressed ||
                Touchscreen.current.primaryTouch.press.wasReleasedThisFrame
            ))
        {
            var touch = Touchscreen.current.primaryTouch;
            Vector2 point = touch.position.ReadValue();

            if (touch.press.wasPressedThisFrame)
                BeginPointer(point, ControlSource.MouseTouch);

            if (touch.press.isPressed)
                MovePointer(point, ControlSource.MouseTouch);

            if (touch.press.wasReleasedThisFrame)
                EndPointer(point, ControlSource.MouseTouch);
        }
        else if (Mouse.current != null)
        {
            Vector2 point = Mouse.current.position.ReadValue();

            if (Mouse.current.leftButton.wasPressedThisFrame)
                BeginPointer(point, ControlSource.MouseTouch);

            if (Mouse.current.leftButton.isPressed)
                MovePointer(point, ControlSource.MouseTouch);

            if (Mouse.current.leftButton.wasReleasedThisFrame)
                EndPointer(point, ControlSource.MouseTouch);
        }
    }

    // =====================================================
    // ROLE 1 HAND TRACKING
    // =====================================================

    private void ReceiveHands(
        HandDataReceiver.HandPacket packet
    )
    {
        if (!running ||
            packet == null ||
            packet.hands == null ||
            packet.coordinate_system != "normalized_image")
            return;

        HandDataReceiver.HandData hand = null;

        if (!string.IsNullOrEmpty(activeHandName))
        {
            foreach (
                HandDataReceiver.HandData candidate
                in packet.hands
            )
            {
                if (candidate != null &&
                    candidate.hand == activeHandName)
                {
                    hand = candidate;
                    break;
                }
            }
        }
        else
        {
            foreach (
                HandDataReceiver.HandData candidate
                in packet.hands
            )
            {
                if (candidate != null &&
                    candidate.interaction != null)
                {
                    hand = candidate;
                    break;
                }
            }
        }

        if (hand == null || hand.interaction == null)
            return;

        Vector2 point = new Vector2(
            Mathf.Clamp01(hand.interaction.x) * Screen.width,
            (1f - Mathf.Clamp01(hand.interaction.y)) * Screen.height
        );

        lastHandPacketTime = Time.unscaledTime;

        if (hand.gesture == "PINCH_START")
        {
            if (string.IsNullOrEmpty(activeHandName))
                activeHandName = hand.hand;

            if (activeHandName == hand.hand)
                BeginPointer(point, ControlSource.Hand);
        }
        else if (hand.gesture == "PINCH_HOLD" &&
                 activeHandName == hand.hand)
        {
            if (source == ControlSource.None)
                BeginPointer(point, ControlSource.Hand);
            else if (source == ControlSource.Hand)
                MovePointer(point, ControlSource.Hand);
        }
        else if (hand.gesture == "PINCH_END" &&
                 activeHandName == hand.hand)
        {
            if (source == ControlSource.Hand)
                EndPointer(point, ControlSource.Hand);

            activeHandName = null;
        }
    }

    // =====================================================
    // GEOMETRY VALIDATION
    // =====================================================

    private void SendGraphToRole2()
    {
        if (!running || modelRoot == null)
            return;

        bool sent =
            HandDataReceiver.SendGeometryJson(
                GetGeometryInputJson()
            );

        if (!sent)
        {
            statusText.text = "ĐANG KẾT NỐI...";
            guideText.text = "Đang đợi bộ xử lý...";
        }
    }

    private void OnConnectionChanged(bool connected)
    {
        if (!running || modelRoot == null)
            return;

        if (connected)
        {
            statusText.text = "ĐANG KIỂM TRA...";
            SendGraphToRole2();
        }
        else
        {
            statusText.text = "MẤT KẾT NỐI";
            guideText.text = "Đang kết nối lại...";
        }
    }

    private static string FloatJson(float value)
    {
        return value.ToString(
            "R",
            CultureInfo.InvariantCulture
        );
    }

    public string GetGeometryInputJson()
    {
        if (modelRoot == null)
            return "{}";

        StringBuilder sb = new StringBuilder();

        sb.Append("{\"source\":\"role3_unity\",");
        sb.Append("\"version\":\"1.0\",");
        sb.Append("\"session_id\":\"");
        sb.Append(sessionId);
        sb.Append("\",\"frame_id\":");
        sb.Append(frameId);

        sb.Append(",\"timestamp\":");

        sb.Append(
            (
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() /
                1000.0
            ).ToString("R", CultureInfo.InvariantCulture)
        );

        sb.Append(",\"target_shape\":\"");
        sb.Append(ShapeCode());
        sb.Append("\",\"coordinate_system\":\"world_3d\",");
        sb.Append("\"vertices\":[");

        for (int i = 0; i < vertexIds.Length; i++)
        {
            if (i > 0)
                sb.Append(",");

            string id = vertexIds[i];

            Vector3 p =
                modelRoot.transform.TransformPoint(positions[id]);

            sb.Append("{\"id\":\"");
            sb.Append(id);
            sb.Append("\",\"x\":");
            sb.Append(FloatJson(p.x));
            sb.Append(",\"y\":");
            sb.Append(FloatJson(p.y));
            sb.Append(",\"z\":");
            sb.Append(FloatJson(p.z));
            sb.Append(",\"confidence\":1.0}");
        }

        sb.Append("],\"detected_edges\":[");

        List<string> edges =
            new List<string>(installed.Keys);

        edges.Sort(StringComparer.Ordinal);

        for (int i = 0; i < edges.Count; i++)
        {
            if (i > 0)
                sb.Append(",");

            string[] pair = edges[i].Split('-');

            sb.Append("[\"");
            sb.Append(pair[0]);
            sb.Append("\",\"");
            sb.Append(pair[1]);
            sb.Append("\"]");
        }

        sb.Append("]}");

        return sb.ToString();
    }

    public void ApplyGeometryResultJson(string json)
    {
        if (!running ||
            modelRoot == null ||
            string.IsNullOrEmpty(json))
            return;

        Role2Result result;

        try
        {
            result = JsonUtility.FromJson<Role2Result>(json);
        }
        catch (Exception)
        {
            return;
        }

        if (result == null)
            return;

        if (result.type == "error")
        {
            statusText.text = "LỖI KIỂM TRA";

            guideText.text = CleanMessage(result.message);
            return;
        }

        if (result.source != "role2_geometry_reasoning" ||
            result.session_id != sessionId ||
            result.frame_id != frameId ||
            result.shape != ShapeCode())
            return;

        switch (result.status)
        {
            case "valid":
                statusText.text = "CHÍNH XÁC";

                guideText.text =
                    "Chúc mừng! Mô hình đã hoàn thành chính xác.";
                break;

            case "incomplete":
                statusText.text = "CHƯA HOÀN THÀNH";

                guideText.text =
                    "Còn thiếu " +
                    Mathf.Max(
                        0,
                        expectedEdges.Length - result.edge_count
                    ) +
                    " cạnh";
                break;

            case "invalid":
                statusText.text = "LẮP SAI";

                string message = CleanMessage(result.message);

                guideText.text = message.Length > 110
                    ? message.Substring(0, 110) + "..."
                    : message;
                break;

            default:
                statusText.text =
                    string.IsNullOrEmpty(result.status)
                        ? "ĐANG KIỂM TRA"
                        : result.status.ToUpperInvariant();

                guideText.text = CleanMessage(result.message);
                break;
        }
    }

    // Remove internal service names from user-facing text.
    private static string CleanMessage(string message)
    {
        if (string.IsNullOrEmpty(message))
            return "";

        return message
            .Replace("Role 2:", "")
            .Replace("ROLE 2:", "")
            .Replace("role 2:", "")
            .Trim();
    }

    // =====================================================
    // CLEANUP / NAVIGATION
    // =====================================================

    private void ClearModel()
    {
        if (modelRoot != null)
            Destroy(modelRoot);

        modelRoot = null;

        foreach (TMP_Text label in labels.Values)
        {
            if (label != null)
                Destroy(label.gameObject);
        }

        labels.Clear();
        positions.Clear();
        guides.Clear();
        installed.Clear();
        rodTypeByEdge.Clear();

        if (dragIcon != null)
            dragIcon.gameObject.SetActive(false);

        lastHandPacketTime = 0f;
        activeHandName = null;
        highlightedEdge = null;
        selectedRod = -1;
        sessionId = "";

        source = ControlSource.None;
        mode = ControlMode.None;
    }

    public void BackToMenu()
    {
        StopScan();

        if (mockDetection != null)
            mockDetection.StopMockDetection();

        if (mockVertexOverlay != null)
            mockVertexOverlay.StopMockVertices();

        if (mockEdgeOverlay != null)
            mockEdgeOverlay.ClearEdges();

        GameSession.ReturnToShapeSelection = true;
        SceneManager.LoadScene("MainMenu");
    }
}
