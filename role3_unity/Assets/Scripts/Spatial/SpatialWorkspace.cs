using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

public class SpatialWorkspace : MonoBehaviour
{
    public const float UIUnit = 0.0004f;
    private const float DefaultDistance = 0.75f;

    [SerializeField] private float distance = 0.75f;

    private static SpatialWorkspace instance;

    private readonly List<Material> materials = new List<Material>();

    private HandCoordinateMapper mapper;
    private SpatialHandInteractor hands;
    private TMP_Text calibrationText;
    private GameObject controlsRoot;
    private bool waitingForTracking;
    private int trackingFrames;
    private bool hasARSession;
    private bool pendingHandPlacement;

    public Camera ViewCamera { get; private set; }

    public static SpatialWorkspace Ensure()
    {
        if (instance == null)
        {
            instance = FindAnyObjectByType<SpatialWorkspace>();

            if (instance == null)
                instance = new GameObject("SmartGeoAR_SpatialWorkspace")
                    .AddComponent<SpatialWorkspace>();
        }

        return instance;
    }

    private void Awake()
    {
        instance = this;
        ViewCamera = Camera.main;

        mapper = gameObject.AddComponent<HandCoordinateMapper>();
        hands = gameObject.AddComponent<SpatialHandInteractor>();
        PhoneCameraSender.Ensure();

#if UNITY_ANDROID && !UNITY_EDITOR
        // The workspace floats in front of the user; debug plane meshes
        // from the old tap-to-place scene are not part of this interface.
        foreach (ARPlaneManager planes in FindObjectsByType<ARPlaneManager>(FindObjectsSortMode.None))
            planes.enabled = false;
#endif

        // The serialized camera pose is replaced by ARCore after Awake.
        // Wait a few tracked frames before fixing the workspace in the room.
        hasARSession = FindAnyObjectByType<ARSession>() != null;
        waitingForTracking = hasARSession;

        PlaceInFront();
        MakeControls();

        if (FindAnyObjectByType<HandDataReceiver>() == null)
        {
            new GameObject("SmartGeoAR_BackendClient")
                .AddComponent<HandDataReceiver>();
        }
    }

    private void LateUpdate()
    {
        if (ViewCamera == null)
            ViewCamera = Camera.main;

        if (hasARSession && ARSession.state != ARSessionState.SessionTracking)
        {
            waitingForTracking = true;
            trackingFrames = 0;
        }

        if (waitingForTracking)
        {
            UpdatePlacement();
            trackingFrames = ARSession.state == ARSessionState.SessionTracking
                ? trackingFrames + 1 : 0;

            if (trackingFrames >= 15)
            {
                waitingForTracking = false;
                PlaceWorkspace(pendingHandPlacement);
            }
        }

        if (!waitingForTracking && pendingHandPlacement &&
            TryGetHandDepth(out _))
            PlaceWorkspace(true);

        if (calibrationText != null)
            calibrationText.text = mapper.IsCalibrated
                ? "CĂN TAY" : "CHỤM TAY ĐỂ CÂN";
    }

    public void PlaceInFront()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        // If a screen tap temporarily takes the hand out of the image,
        // finish placing when the hand becomes visible again.
        pendingHandPlacement = true;
#endif
        PlaceWorkspace(pendingHandPlacement);
    }

    private void PlaceWorkspace(bool matchHand)
    {
        if (ViewCamera == null)
            ViewCamera = Camera.main;

        if (ViewCamera == null)
            return;

        if (matchHand && TryGetHandDepth(out float depth))
        {
            distance = DistanceBeyondHand(depth);
            // Preserve the readable angular layout at a reachable depth.
            transform.localScale = Vector3.one * (distance / DefaultDistance);
            pendingHandPlacement = false;
        }

        if (hasARSession && ARSession.state != ARSessionState.SessionTracking)
        {
            waitingForTracking = true;
            trackingFrames = 0;
        }

        hands.CancelTracking();

        UpdatePlacement();
        UpdateControls();
        mapper.ResetCalibration();
        LogInteraction("Workspace placed: " + transform.position.ToString("F3") +
            "; camera=" + ViewCamera.transform.position.ToString("F3") +
            "; distance=" + distance.ToString("F3") + "; AR=" + ARSession.state);
    }

    public static float DistanceBeyondHand(float handDepth)
    {
        return Mathf.Clamp(handDepth + 0.15f, 0.35f, 1f);
    }

    private bool TryGetHandDepth(out float depth)
    {
        depth = 0f;
        if (ViewCamera == null || (hasARSession && ARSession.state != ARSessionState.SessionTracking) ||
            !hands.TryGetPhoneHandPosition(out Vector3 hand))
            return false;

        depth = ViewCamera.transform.InverseTransformPoint(hand).z;
        return depth >= 0.12f && depth <= 0.85f;
    }

    private void ShiftDistance(float amount)
    {
        pendingHandPlacement = false;
        distance = Mathf.Clamp(distance + amount, 0.35f, 1.2f);
        hands.CancelTracking();
        mapper.ResetCalibration();
        UpdatePlacement();
        UpdateControls();
        LogInteraction("Workspace distance: " + distance.ToString("F2"));
    }

    public void UseCamera(Camera camera)
    {
        ViewCamera = camera;
        hasARSession = FindAnyObjectByType<ARSession>() != null;
        PhoneCameraSender.Ensure(camera.GetComponent<ARCameraManager>());
        foreach (Canvas canvas in GetComponentsInChildren<Canvas>(true))
            canvas.worldCamera = camera;
        if (controlsRoot != null)
        {
            controlsRoot.transform.SetParent(camera.transform, false);
            controlsRoot.GetComponent<Canvas>().worldCamera = camera;
        }
        waitingForTracking = true;
        trackingFrames = 0;
        PlaceInFront();
    }

    private void UpdateControls()
    {
        if (controlsRoot == null || ViewCamera == null)
            return;

        float depth = Mathf.Clamp(distance, 0.4f, 0.65f);
        controlsRoot.transform.localPosition = Vector3.forward * depth;
        controlsRoot.transform.localScale = Vector3.one * (UIUnit * depth / DefaultDistance);

        PhoneDepthOcclusion occlusion = ViewCamera.GetComponent<PhoneDepthOcclusion>();
        if (occlusion != null)
            occlusion.ConfigureControls(controlsRoot);
    }

    private void UpdatePlacement()
    {
        if (ViewCamera == null)
            return;

        transform.SetPositionAndRotation(
            ViewCamera.transform.position + ViewCamera.transform.forward * distance,
            ViewCamera.transform.rotation
        );
    }

    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    public static void LogInteraction(string message)
    {
        Debug.Log("[SmartGeoAR] " + message);
    }

    public void ConfigureCanvas(Canvas target)
    {
        if (target == null)
            return;

        CanvasScaler scaler = target.GetComponent<CanvasScaler>();

        if (scaler != null)
            scaler.enabled = false;

        target.renderMode = RenderMode.WorldSpace;
        target.worldCamera = ViewCamera;
        target.scaleFactor = 1f;

        RectTransform rect = target.transform as RectTransform;
        rect.SetParent(transform, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(1600f, 900f);
        rect.localPosition = Vector3.zero;
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one * UIUnit;
    }

    public Material MakeMaterial(Color color)
    {
        Material template = Resources.Load<Material>("SmartGeoAR_Unlit");
        Shader shader = template != null
            ? template.shader
            : Shader.Find("Universal Render Pipeline/Unlit");

        if (shader == null)
            shader = Shader.Find("Unlit/Color");

        if (shader == null)
            return null;

        Material material = template != null ? new Material(template) : new Material(shader);

        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);

        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);

        materials.Add(material);
        return material;
    }

    private void MakeControls()
    {
        GameObject root = new GameObject(
            "SpatialControls",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(GraphicRaycaster)
        );

        Canvas controls = root.GetComponent<Canvas>();
        ConfigureCanvas(controls);
        controls.sortingOrder = 110;
        controlsRoot = root;

        // Keep recovery/calibration reachable when the world-space board
        // leaves the view as the phone turns or moves.
        if (ViewCamera != null)
        {
            root.transform.SetParent(ViewCamera.transform, false);
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        MakeButton(root.transform, "KÍNH VR", new Vector2(570f, 380f),
            PhoneHeadsetDisplay.Toggle);
        MakeButton(root.transform, "GẦN HƠN", new Vector2(-330f, -400f), () => ShiftDistance(-0.1f));
        MakeButton(root.transform, "ĐẶT LẠI", new Vector2(0f, -400f), PlaceInFront);
        MakeButton(root.transform, "XA HƠN", new Vector2(330f, -400f), () => ShiftDistance(0.1f));
#else
        MakeButton(root.transform, "ĐẶT LẠI", new Vector2(240f, -400f), () =>
        {
            PlaceInFront();
        });

        calibrationText = MakeButton(
            root.transform, "CĂN TAY", new Vector2(570f, -400f), () =>
            {
                hands.CancelTracking();
                mapper.ResetCalibration();
            }
        );
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
            label, typeof(RectTransform), typeof(Image), typeof(Button)
        );

        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(300f, 66f);
        rect.anchoredPosition = position;

        Image image = obj.GetComponent<Image>();
        image.color = new Color32(25, 65, 86, 245);

        Button button = obj.GetComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(callback);

        GameObject textObject = new GameObject("Text", typeof(RectTransform));
        textObject.transform.SetParent(rect, false);

        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
        text.text = label;
        text.fontSize = 22f;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        return text;
    }

    private void OnDestroy()
    {
        if (controlsRoot != null)
            Destroy(controlsRoot);

        foreach (Material material in materials)
        {
            if (material != null)
                Destroy(material);
        }

        if (instance == this)
            instance = null;
    }
}
