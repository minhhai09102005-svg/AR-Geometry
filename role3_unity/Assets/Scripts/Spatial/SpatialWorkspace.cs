
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

public class SpatialWorkspace : MonoBehaviour
{
    public const float UIUnit = 0.0004f;

    private const float DefaultDistance = 0.75f;

    [SerializeField]
    private float distance = DefaultDistance;

    // ==========================================
    // PC DISPLAY SETTINGS
    // ==========================================

    [Header("PC Display")]

    [SerializeField, Range(1f, 1.6f)]
    private float pcLayoutScale = 1.4f;

    [SerializeField]
    private Vector2 pcReferenceResolution =
        new Vector2(1600f, 900f);

    [SerializeField]
    private Vector2 pcTrayPosition =
        new Vector2(-700f, 30f);

    [SerializeField]
    private Vector3 pcModelOffset =
        new Vector3(0.025f, -0.012f, 0f);

    private RectTransform pcTrackedTray;
    private Transform pcTrackedModel;
    private RectTransform pcStatusBackground;

    private float nextPcLayoutCheck;

    // ==========================================
    // WORKSPACE
    // ==========================================

    private static SpatialWorkspace instance;

    private readonly List<Material> materials =
        new List<Material>();

    private HandCoordinateMapper mapper;
    private SpatialHandInteractor hands;

    private TMP_Text calibrationText;
    private GameObject controlsRoot;

    private bool waitingForTracking;
    private int trackingFrames;
    private bool hasARSession;
    private bool pendingHandPlacement;

    public Camera ViewCamera
    {
        get;
        private set;
    }

    // ==========================================
    // INITIALIZATION
    // ==========================================

    public static SpatialWorkspace Ensure()
    {
        if (instance == null)
        {
            instance =
                FindAnyObjectByType<SpatialWorkspace>();

            if (instance == null)
            {
                instance = new GameObject(
                    "SmartGeoAR_SpatialWorkspace"
                ).AddComponent<SpatialWorkspace>();
            }
        }

        return instance;
    }

    private void Awake()
    {
        instance = this;

        ViewCamera = Camera.main;

        mapper =
            gameObject.AddComponent<HandCoordinateMapper>();

        hands =
            gameObject.AddComponent<SpatialHandInteractor>();

        PhoneCameraSender.Ensure();

#if UNITY_ANDROID && !UNITY_EDITOR

        foreach (
            ARPlaneManager planes in
            FindObjectsByType<ARPlaneManager>(
                FindObjectsSortMode.None
            )
        )
        {
            planes.enabled = false;
        }

        hasARSession =
            FindAnyObjectByType<ARSession>() != null;

#else

        hasARSession = false;

#endif

        waitingForTracking = hasARSession;

        PlaceInFront();

        MakeControls();

        if (
            FindAnyObjectByType<HandDataReceiver>()
            == null
        )
        {
            new GameObject(
                "SmartGeoAR_BackendClient"
            ).AddComponent<HandDataReceiver>();
        }
    }

    // ==========================================
    // UPDATE
    // ==========================================

    private void LateUpdate()
    {
        if (ViewCamera == null)
        {
            ViewCamera = Camera.main;
        }

        if (
            hasARSession &&
            ARSession.state !=
                ARSessionState.SessionTracking
        )
        {
            waitingForTracking = true;
            trackingFrames = 0;
        }

        if (waitingForTracking)
        {
            UpdatePlacement();

            trackingFrames =
                ARSession.state ==
                    ARSessionState.SessionTracking
                    ? trackingFrames + 1
                    : 0;

            if (trackingFrames >= 15)
            {
                waitingForTracking = false;

                PlaceWorkspace(
                    pendingHandPlacement
                );
            }
        }

        if (
            !waitingForTracking &&
            pendingHandPlacement &&
            TryGetHandDepth(out _)
        )
        {
            PlaceWorkspace(true);
        }

        if (calibrationText != null)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN

            calibrationText.text =
                mapper.IsCalibrated
                    ? "CAN TAY"
                    : "CHUM TAY DE CAN";

#else

            calibrationText.text =
                mapper.IsCalibrated
                    ? "CĂN TAY"
                    : "CHỤM TAY ĐỂ CÂN";

#endif
        }

        // Only the Windows application uses
        // the expanded desktop layout.
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN

        UpdateDesktopPresentation();

#endif
    }

    // ==========================================
    // PC RESPONSIVE SCALE
    // ==========================================

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN

    private float CalculatePcScale()
    {
        float referenceWidth =
            Mathf.Max(
                1f,
                pcReferenceResolution.x
            );

        float referenceHeight =
            Mathf.Max(
                1f,
                pcReferenceResolution.y
            );

        float widthRatio =
            Screen.width / referenceWidth;

        float heightRatio =
            Screen.height / referenceHeight;

        float fit =
            Mathf.Min(
                widthRatio,
                heightRatio
            );

        // Do not make the model too large
        // on small desktop windows.
        return Mathf.Clamp(
            pcLayoutScale * fit,
            1f,
            1.42f
        );
    }

    private void UpdateDesktopPresentation()
    {
        // Do not change the MainMenu or
        // the Shape Drop game.
        //
        // ScanScreenController creates this
        // object only for the rod assembly UI.
        if (
            pcTrackedTray == null &&
            Time.unscaledTime <
                nextPcLayoutCheck
        )
        {
            return;
        }

        if (pcTrackedTray == null)
        {
            nextPcLayoutCheck =
                Time.unscaledTime + 0.25f;

            GameObject trayObject =
                GameObject.Find("KhoThanhNoi");

            if (trayObject == null)
            {
                transform.localScale =
                    Vector3.one;

                return;
            }

            RectTransform tray =
                trayObject.GetComponent<RectTransform>();

            if (tray == null)
                return;

            ConfigurePcAssemblyLayout(tray);
        }

        // Apply the same scale to both
        // the UI and the geometry model.
        //
        // The hand mapping still uses the
        // current camera and workspace plane.
        float scale = CalculatePcScale();

        transform.localScale =
            Vector3.one * scale;

        ConfigurePcModelPosition();
    }

    // ==========================================
    // PC ASSEMBLY LAYOUT
    // ==========================================

    private void ConfigurePcAssemblyLayout(
        RectTransform tray
    )
    {
        if (tray == null)
            return;

        if (pcTrackedTray == tray)
            return;

        pcTrackedTray = tray;

        // Move the inventory farther left,
        // away from the central 3D structure.
        tray.anchoredPosition =
            pcTrayPosition;

        // Slightly widen the inventory panel.
        // Preserve its height and existing rods.
        tray.sizeDelta = new Vector2(
            326f,
            tray.sizeDelta.y + 16f
        );

        // Keep the cards inside the wider tray.
        foreach (
            RectTransform child in
            tray.GetComponentsInChildren<RectTransform>(
                true
            )
        )
        {
            if (
                child == tray ||
                !child.name.StartsWith("Thanh_")
            )
            {
                continue;
            }

            child.sizeDelta = new Vector2(
                292f,
                child.sizeDelta.y
            );
        }

        Canvas canvas =
            tray.GetComponentInParent<Canvas>();

        if (canvas != null)
        {
            ConfigurePcStatusPanel(canvas);
            ConfigurePcTextPositions(canvas);
        }

        LogInteraction(
            "PC assembly layout configured"
        );
    }

    // ==========================================
    // PC STATUS BACKGROUND
    // ==========================================

    private void ConfigurePcStatusPanel(
        Canvas canvas
    )
    {
        if (
            canvas == null ||
            pcStatusBackground != null
        )
        {
            return;
        }

        GameObject obj = new GameObject(
            "PC_StatusBackground",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image)
        );

        RectTransform rect =
            obj.GetComponent<RectTransform>();

        rect.SetParent(
            canvas.transform,
            false
        );

        rect.anchorMin =
            rect.anchorMax =
                new Vector2(0.5f, 0.5f);

        rect.pivot =
            new Vector2(0.5f, 0.5f);

        // Right-side status area.
        rect.anchoredPosition =
            new Vector2(590f, 245f);

        rect.sizeDelta =
            new Vector2(410f, 255f);

        Image image =
            obj.GetComponent<Image>();

        image.color =
            new Color(
                0.025f,
                0.075f,
                0.14f,
                0.88f
            );

        // Do not block mouse or hand input.
        image.raycastTarget = false;

        // Draw behind existing text and buttons.
        rect.SetAsFirstSibling();

        pcStatusBackground = rect;
    }

    // ==========================================
    // PC TEXT ALIGNMENT
    // ==========================================

    private void ConfigurePcTextPositions(
        Canvas canvas
    )
    {
        TMP_Text[] texts =
            canvas.GetComponentsInChildren<TMP_Text>(
                true
            );

        foreach (TMP_Text text in texts)
        {
            if (text == null)
                continue;

            // Do not modify the inventory labels.
            if (
                pcTrackedTray != null &&
                text.transform.IsChildOf(
                    pcTrackedTray
                )
            )
            {
                continue;
            }

            RectTransform rect =
                text.rectTransform;

            Vector2 position =
                rect.anchoredPosition;

            // Main shape title:
            // Center it and leave more top margin.
            if (
                Mathf.Abs(position.x + 110f) < 3f &&
                Mathf.Abs(position.y - 395f) < 3f
            )
            {
                rect.anchoredPosition =
                    new Vector2(0f, 350f);

                text.fontSize = Mathf.Max(
                    text.fontSize,
                    35f
                );

                continue;
            }

            // Existing right-side status texts
            // are at x=565, y=310/230/170.
            if (
                Mathf.Abs(position.x - 565f) < 3f &&
                position.y > 140f &&
                position.y < 340f
            )
            {
                rect.anchoredPosition =
                    new Vector2(
                        590f,
                        position.y
                    );

                text.fontSize = Mathf.Max(
                    text.fontSize,
                    25f
                );
            }
        }
    }

    // ==========================================
    // SEPARATE 3D MODEL FROM INVENTORY
    // ==========================================

    private void ConfigurePcModelPosition()
    {
        Transform model = FindCurrentModel();

        if (model == null)
        {
            pcTrackedModel = null;
            return;
        }

        // Each model is adjusted only once.
        // Restarting the exercise creates a
        // new Transform, which is adjusted again.
        if (pcTrackedModel == model)
            return;

        model.localPosition += pcModelOffset;

        pcTrackedModel = model;
    }

    private Transform FindCurrentModel()
    {
        string[] names =
        {
            "SmartGeoAR_cube",
            "SmartGeoAR_rectangular_prism",
            "SmartGeoAR_square_pyramid"
        };

        foreach (string name in names)
        {
            Transform found =
                transform.Find(name);

            if (found != null)
                return found;
        }

        return null;
    }

#endif

    // ==========================================
    // WORKSPACE PLACEMENT
    // ==========================================

    public void PlaceInFront()
    {
#if UNITY_ANDROID && !UNITY_EDITOR

        pendingHandPlacement = true;

#endif

        PlaceWorkspace(pendingHandPlacement);
    }

    private void PlaceWorkspace(bool matchHand)
    {
        if (ViewCamera == null)
        {
            ViewCamera = Camera.main;
        }

        if (ViewCamera == null)
            return;

        if (
            matchHand &&
            TryGetHandDepth(out float depth)
        )
        {
            distance =
                DistanceBeyondHand(depth);

            transform.localScale =
                Vector3.one *
                (distance / DefaultDistance);

            pendingHandPlacement = false;
        }

        if (
            hasARSession &&
            ARSession.state !=
                ARSessionState.SessionTracking
        )
        {
            waitingForTracking = true;
            trackingFrames = 0;
        }

        if (hands != null)
            hands.CancelTracking();

        UpdatePlacement();
        UpdateControls();

        if (mapper != null)
            mapper.ResetCalibration();

        LogInteraction(
            "Workspace placed: " +
            transform.position.ToString("F3") +
            "; distance=" +
            distance.ToString("F3")
        );
    }

    public static float DistanceBeyondHand(
        float handDepth
    )
    {
        return Mathf.Clamp(
            handDepth + 0.15f,
            0.35f,
            1f
        );
    }

    private bool TryGetHandDepth(
        out float depth
    )
    {
        depth = 0f;

        if (
            ViewCamera == null ||
            (
                hasARSession &&
                ARSession.state !=
                    ARSessionState.SessionTracking
            ) ||
            hands == null ||
            !hands.TryGetPhoneHandPosition(
                out Vector3 hand
            )
        )
        {
            return false;
        }

        depth =
            ViewCamera.transform
                .InverseTransformPoint(hand).z;

        return
            depth >= 0.12f &&
            depth <= 0.85f;
    }

    private void ShiftDistance(
        float amount
    )
    {
        pendingHandPlacement = false;

        distance = Mathf.Clamp(
            distance + amount,
            0.35f,
            1.2f
        );

        if (hands != null)
            hands.CancelTracking();

        if (mapper != null)
            mapper.ResetCalibration();

        UpdatePlacement();
        UpdateControls();

        LogInteraction(
            "Workspace distance: " +
            distance.ToString("F2")
        );
    }

    // ==========================================
    // CAMERA
    // ==========================================

    public void UseCamera(Camera camera)
    {
        if (camera == null)
            return;

        ViewCamera = camera;

#if UNITY_ANDROID && !UNITY_EDITOR

        hasARSession =
            FindAnyObjectByType<ARSession>() != null;

#else

        hasARSession = false;

#endif

        PhoneCameraSender.Ensure(
            camera.GetComponent<ARCameraManager>()
        );

        foreach (
            Canvas canvas in
            GetComponentsInChildren<Canvas>(true)
        )
        {
            canvas.worldCamera = camera;
        }

        if (controlsRoot != null)
        {
            controlsRoot.transform.SetParent(
                camera.transform,
                false
            );

            controlsRoot
                .GetComponent<Canvas>()
                .worldCamera = camera;
        }

        waitingForTracking = true;
        trackingFrames = 0;

        PlaceInFront();
    }

    // ==========================================
    // CONTROLS
    // ==========================================

    private void UpdateControls()
    {
        if (
            controlsRoot == null ||
            ViewCamera == null
        )
        {
            return;
        }

        float depth = Mathf.Clamp(
            distance,
            0.4f,
            0.65f
        );

        controlsRoot.transform.localPosition =
            Vector3.forward * depth;

        float scale =
            UIUnit * depth / DefaultDistance;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN

        // Slightly enlarge the PC buttons.
        scale *= 1.25f;

#endif

        controlsRoot.transform.localScale =
            Vector3.one * scale;

        PhoneDepthOcclusion occlusion =
            ViewCamera
                .GetComponent<PhoneDepthOcclusion>();

        if (occlusion != null)
        {
            occlusion.ConfigureControls(
                controlsRoot
            );
        }
    }

    private void UpdatePlacement()
    {
        if (ViewCamera == null)
            return;

        transform.SetPositionAndRotation(
            ViewCamera.transform.position +
                ViewCamera.transform.forward *
                distance,
            ViewCamera.transform.rotation
        );
    }

    // ==========================================
    // LOGGING
    // ==========================================

    [System.Diagnostics.Conditional(
        "DEVELOPMENT_BUILD"
    )]
    [System.Diagnostics.Conditional(
        "UNITY_EDITOR"
    )]
    public static void LogInteraction(
        string message
    )
    {
        Debug.Log(
            "[SmartGeoAR] " + message
        );
    }

    // ==========================================
    // WORLD-SPACE CANVAS
    // ==========================================

    public void ConfigureCanvas(
        Canvas target
    )
    {
        if (target == null)
            return;

        CanvasScaler scaler =
            target.GetComponent<CanvasScaler>();

        if (scaler != null)
            scaler.enabled = false;

        target.renderMode =
            RenderMode.WorldSpace;

        target.worldCamera = ViewCamera;

        target.scaleFactor = 1f;

        RectTransform rect =
            target.transform as RectTransform;

        rect.SetParent(
            transform,
            false
        );

        rect.anchorMin =
            rect.anchorMax =
                new Vector2(0.5f, 0.5f);

        rect.pivot =
            new Vector2(0.5f, 0.5f);

        rect.sizeDelta =
            new Vector2(1600f, 900f);

        rect.localPosition =
            Vector3.zero;

        rect.localRotation =
            Quaternion.identity;

        rect.localScale =
            Vector3.one * UIUnit;
    }

    // ==========================================
    // MATERIALS
    // ==========================================

    public Material MakeMaterial(
        Color color
    )
    {
        Material template =
            Resources.Load<Material>(
                "SmartGeoAR_Unlit"
            );

        Shader shader =
            template != null
                ? template.shader
                : Shader.Find(
                    "Universal Render Pipeline/Unlit"
                );

        if (shader == null)
        {
            shader = Shader.Find(
                "Unlit/Color"
            );
        }

        if (shader == null)
            return null;

        Material material =
            template != null
                ? new Material(template)
                : new Material(shader);

        if (
            material.HasProperty("_BaseColor")
        )
        {
            material.SetColor(
                "_BaseColor",
                color
            );
        }

        if (
            material.HasProperty("_Color")
        )
        {
            material.SetColor(
                "_Color",
                color
            );
        }

        materials.Add(material);

        return material;
    }

    // ==========================================
    // CREATE CONTROLS
    // ==========================================

    private void MakeControls()
    {
        GameObject root = new GameObject(
            "SpatialControls",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(GraphicRaycaster)
        );

        Canvas controls =
            root.GetComponent<Canvas>();

        ConfigureCanvas(controls);

        controls.sortingOrder = 110;

        controlsRoot = root;

        if (ViewCamera != null)
        {
            root.transform.SetParent(
                ViewCamera.transform,
                false
            );
        }

#if UNITY_ANDROID && !UNITY_EDITOR

        MakeButton(
            root.transform,
            "KÍNH VR",
            new Vector2(570f, 380f),
            PhoneHeadsetDisplay.Toggle
        );

        MakeButton(
            root.transform,
            "GẦN HƠN",
            new Vector2(-330f, -400f),
            () => ShiftDistance(-0.1f)
        );

        MakeButton(
            root.transform,
            "ĐẶT LẠI",
            new Vector2(0f, -400f),
            PlaceInFront
        );

        MakeButton(
            root.transform,
            "XA HƠN",
            new Vector2(330f, -400f),
            () => ShiftDistance(0.1f)
        );

#else

        // Place controls at the bottom-right,
        // away from the central learning area.

        MakeButton(
            root.transform,
            "DAT LAI",
            new Vector2(310f, -430f),
            PlaceInFront
        );

        calibrationText = MakeButton(
            root.transform,
            "CAN TAY",
            new Vector2(660f, -430f),
            () =>
            {
                if (hands != null)
                    hands.CancelTracking();

                if (mapper != null)
                    mapper.ResetCalibration();
            }
        );

        if (calibrationText != null)
        {
            calibrationText.fontSize = 19f;
        }

#endif

        UpdateControls();
    }

    private static TMP_Text MakeButton(
        Transform parent,
        string label,
        Vector2 position,
        UnityEngine.Events.UnityAction callback
    )
    {
        GameObject obj = new GameObject(
            label,
            typeof(RectTransform),
            typeof(Image),
            typeof(Button)
        );

        RectTransform rect =
            obj.GetComponent<RectTransform>();

        rect.SetParent(
            parent,
            false
        );

        rect.anchorMin =
            rect.anchorMax =
                new Vector2(0.5f, 0.5f);

        rect.pivot =
            new Vector2(0.5f, 0.5f);

        rect.sizeDelta =
            new Vector2(300f, 66f);

        rect.anchoredPosition =
            position;

        Image image =
            obj.GetComponent<Image>();

        image.color =
            new Color32(
                25,
                65,
                86,
                245
            );

        Button button =
            obj.GetComponent<Button>();

        button.targetGraphic = image;

        button.onClick.AddListener(
            callback
        );

        GameObject textObject =
            new GameObject(
                "Text",
                typeof(RectTransform)
            );

        textObject.transform.SetParent(
            rect,
            false
        );

        TextMeshProUGUI text =
            textObject.AddComponent<TextMeshProUGUI>();

        text.rectTransform.anchorMin =
            Vector2.zero;

        text.rectTransform.anchorMax =
            Vector2.one;

        text.rectTransform.offsetMin =
            Vector2.zero;

        text.rectTransform.offsetMax =
            Vector2.zero;

        text.text = label;
        text.fontSize = 22f;
        text.color = Color.white;

        text.alignment =
            TextAlignmentOptions.Center;

        text.raycastTarget = false;

        return text;
    }

    // ==========================================
    // CLEANUP
    // ==========================================

    private void OnDestroy()
    {
        if (controlsRoot != null)
        {
            Destroy(controlsRoot);
        }

        foreach (Material material in materials)
        {
            if (material != null)
            {
                Destroy(material);
            }
        }

        if (instance == this)
        {
            instance = null;
        }
    }
}
