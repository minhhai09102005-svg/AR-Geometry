using System;
using System.Runtime.InteropServices;
using AOT;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

// ARCore remains the tracking/camera provider. Only the display changes:
// virtual geometry is rendered per eye; the single rear camera stays mono.
[DefaultExecutionOrder(100)]
public class PhoneHeadsetDisplay : MonoBehaviour
{
    private const string Preference = "SmartGeoAR.Headset";
    private const int ControlsLayer = 30;

    [SerializeField, Range(0.05f, 0.075f)] private float interpupillaryDistance = 0.060f;
    [SerializeField, Range(40f, 90f)] private float verticalFieldOfView = 60f;
    [SerializeField, Range(0.5f, 1f)] private float renderScale = 0.8f;
    // A provisional fit for testing a viewer without a QR profile.
    [SerializeField] private Vector2 lensDistortion = new Vector2(0.22f, 0.24f);
    [SerializeField, Range(-0.15f, 0.15f)] private float lensCenterAdjustment;

    private static PhoneHeadsetDisplay instance;
    private static bool requested;
    private static bool preferenceRead;
    private static XRCameraSubsystem renderSubsystem;
    private static int lastBackgroundFrame = -1;
    // Keep the reverse-P/Invoke delegate alive for IL2CPP and the render thread.
    private static readonly Action<int> beforeBackgroundRender = BeforeBackgroundRender;
    private static readonly IntPtr beforeBackgroundRenderPointer =
        Marshal.GetFunctionPointerForDelegate(beforeBackgroundRender);

    private Camera viewCamera;
    private ARCameraManager cameraManager;
    private ARCameraBackground background;
    private readonly Camera[] eyes = new Camera[2];
    private readonly Camera[] controls = new Camera[2];
    private readonly RenderTexture[] eyeTextures = new RenderTexture[2];
    private RenderTexture cameraTexture;
    private Camera outputCamera;
    private Canvas outputCanvas;
    private Material composite;
    private CommandBuffer commands;
    private bool rendering;
    private bool originalCameraEnabled;
    private int originalSleepTimeout;
    private int originalFrameRate;
    private int displayWidth;
    private int displayHeight;

    public static bool IsActive => requested;

    public static void Ensure(ARCameraManager manager)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (manager != null && manager.GetComponent<PhoneHeadsetDisplay>() == null)
            manager.gameObject.AddComponent<PhoneHeadsetDisplay>();
#endif
    }

    public static void Toggle()
    {
        SetActive(!requested);
    }

    public static void SetActive(bool value)
    {
        requested = value;
        preferenceRead = true;
        PlayerPrefs.SetInt(Preference, value ? 1 : 0);
        PlayerPrefs.Save();
        if (!value && instance != null)
            instance.StopRendering();
    }

    private void Awake()
    {
        instance = this;
        viewCamera = GetComponent<Camera>();
        cameraManager = GetComponent<ARCameraManager>();
        background = GetComponent<ARCameraBackground>();
        if (!preferenceRead)
        {
            requested = PlayerPrefs.GetInt(Preference, 0) != 0;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var intent = activity.Call<AndroidJavaObject>("getIntent"))
                {
                    if (intent.Call<bool>("hasExtra", "smartgeoar_headset"))
                        requested = intent.Call<bool>("getBooleanExtra", "smartgeoar_headset", false);
                }
            }
            catch (Exception error)
            {
                Debug.LogWarning("[SmartGeoAR] Headset launch option: " + error.Message);
            }
#endif
            preferenceRead = true;
        }
    }

    private void Update()
    {
        if (requested && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            SetActive(false);
    }

    private void OnEnable()
    {
        instance = this;
    }

    public static Vector3 EyeLocalOffset(bool rightEye, float ipd = 0.060f)
    {
        return Vector3.right * (rightEye ? 0.5f : -0.5f) * Mathf.Clamp(ipd, 0f, 0.08f);
    }

    public static Matrix4x4 EyeProjection(float verticalFov, float aspect, float near, float far)
    {
        near = Mathf.Max(0.01f, near);
        return Matrix4x4.Perspective(Mathf.Clamp(verticalFov, 30f, 100f),
            Mathf.Max(0.1f, aspect), near, Mathf.Max(near + 0.1f, far));
    }

    public static Vector2 UndistortEyeUv(Vector2 uv, Vector2 distortion)
    {
        Vector2 point = (uv - Vector2.one * 0.5f) * 2f;
        float radius = point.sqrMagnitude;
        point *= 1f + distortion.x * radius + distortion.y * radius * radius;
        return point * 0.5f + Vector2.one * 0.5f;
    }

    public static float LensCenterShift(float screenWidthPixels, float dpi, float lensDistance = 0.060f)
    {
        float widthMillimetres = dpi >= 80f && dpi <= 1000f
            ? screenWidthPixels / dpi * 25.4f : 155f;
        // Reported DPI can be a logical density rather than a physical one.
        // Keep the provisional fit within the size of a phone display.
        widthMillimetres = Mathf.Clamp(widthMillimetres, 120f, 180f);
        return Mathf.Clamp(0.5f - lensDistance * 1000f / widthMillimetres, -0.18f, 0.18f);
    }

    private float CurrentLensShift()
    {
        return Mathf.Clamp(LensCenterShift(Screen.width, Screen.dpi, interpupillaryDistance) +
            lensCenterAdjustment, -0.2f, 0.2f);
    }

    // Touch is useful before mounting/removing the phone. Hand interaction
    // still uses world positions and does not pass through this conversion.
    public static bool TryMapScreenPoint(Vector2 screenPoint, out Vector2 centerPoint)
    {
        centerPoint = screenPoint;
        if (!requested || instance == null || !instance.rendering)
            return false;

        int eye = screenPoint.x >= Screen.width * 0.5f ? 1 : 0;
        Vector2 uv = new Vector2(screenPoint.x / Screen.width * 2f - eye,
            screenPoint.y / Screen.height);
        uv.x -= (eye == 0 ? 1f : -1f) * instance.CurrentLensShift();
        uv = UndistortEyeUv(uv, instance.lensDistortion);
        if (uv.x < 0f || uv.x > 1f || uv.y < 0f || uv.y > 1f)
            return false;

        Camera eyeCamera = instance.eyes[eye];
        Ray ray = eyeCamera.ViewportPointToRay(new Vector3(uv.x, uv.y, 0f));
        SpatialWorkspace workspace = FindAnyObjectByType<SpatialWorkspace>();
        Plane plane = workspace != null
            ? new Plane(workspace.transform.forward, workspace.transform.position)
            : new Plane(instance.viewCamera.transform.forward,
                instance.viewCamera.transform.position + instance.viewCamera.transform.forward * 0.75f);
        if (!plane.Raycast(ray, out float distance))
            return false;

        Vector3 point = instance.viewCamera.WorldToScreenPoint(ray.GetPoint(distance));
        if (point.z <= 0f)
            return false;
        centerPoint = new Vector2(point.x, point.y);
        return true;
    }

    private void LateUpdate()
    {
        if (!requested || viewCamera == null || background == null || background.material == null)
            return;

        try
        {
            if (displayWidth != Screen.width || displayHeight != Screen.height)
                StopRendering();
            if (composite == null && !CreateRendering())
            {
                SetActive(false);
                Debug.LogWarning("[SmartGeoAR] Headset display shader is unavailable. Normal display restored.");
                return;
            }
            if (!RenderPipeline.SupportsRenderRequest(eyes[0],
                new RenderPipeline.StandardRequest()))
            {
                SetActive(false);
                Debug.LogWarning("[SmartGeoAR] Headset eye rendering is unavailable. Normal display restored.");
                return;
            }

            CaptureBackground();
            // Screen-space overlay UI must not be sampled into its own eye RT.
            outputCanvas.enabled = false;
            float aspect = Screen.width * 0.5f / Screen.height;
            Matrix4x4 projection = EyeProjection(verticalFieldOfView, aspect,
                viewCamera.nearClipPlane, viewCamera.farClipPlane);
            try
            {
                for (int i = 0; i < eyes.Length; i++)
                {
                    Camera eye = eyes[i];
                    Vector3 offset = viewCamera.transform.right *
                        EyeLocalOffset(i == 1, interpupillaryDistance).x;
                    eye.transform.SetPositionAndRotation(viewCamera.transform.position + offset,
                        viewCamera.transform.rotation);
                    eye.ResetWorldToCameraMatrix();
                    eye.ResetCullingMatrix();
                    eye.cullingMask = viewCamera.cullingMask & ~(1 << ControlsLayer);
                    eye.projectionMatrix = projection;
                    controls[i].projectionMatrix = projection;
                    controls[i].nearClipPlane = viewCamera.nearClipPlane;
                    controls[i].farClipPlane = viewCamera.farClipPlane;
                    Shader.SetGlobalVector("_SmartGeoAR_HandEyeOffset",
                        new Vector4(offset.x, offset.y, offset.z, 0f));
                    RenderPipeline.SubmitRenderRequest(eye, new RenderPipeline.StandardRequest
                    {
                        destination = eyeTextures[i],
                        isPreview = false
                    });
                }
            }
            finally
            {
                Shader.SetGlobalVector("_SmartGeoAR_HandEyeOffset", Vector4.zero);
            }

            Matrix4x4 cameraProjection = viewCamera.projectionMatrix;
            composite.SetVector("_EyeProjection", ProjectionValues(projection));
            composite.SetVector("_CameraProjection", ProjectionValues(cameraProjection));
            composite.SetVector("_Distortion", new Vector4(lensDistortion.x, lensDistortion.y, 0f, 0f));
            composite.SetFloat("_LensCenterShift", CurrentLensShift());
            if (!rendering)
            {
                originalCameraEnabled = viewCamera.enabled;
                originalSleepTimeout = Screen.sleepTimeout;
                originalFrameRate = Application.targetFrameRate;
                Screen.sleepTimeout = SleepTimeout.NeverSleep;
                Application.targetFrameRate = 60;
                viewCamera.enabled = false;
                outputCamera.enabled = true;
                rendering = true;
                SpatialWorkspace.LogInteraction("Headset stereo active: two eyes; IPD=" +
                    interpupillaryDistance.ToString("F3") + "; FOV=" + verticalFieldOfView.ToString("F0") +
                    "; lensShift=" + CurrentLensShift().ToString("F3") +
                    "; provisional lens fit; rear camera remains mono");
            }
            outputCanvas.enabled = true;
        }
        catch (Exception error)
        {
            SetActive(false);
            Debug.LogWarning("[SmartGeoAR] Headset display: " + error.Message + ". Normal display restored.");
        }
    }

    private static Vector4 ProjectionValues(Matrix4x4 projection)
    {
        return new Vector4(projection.m00, projection.m11, projection.m02, projection.m12);
    }

    private bool CreateRendering()
    {
        Shader shader = Resources.Load<Shader>("SmartGeoAR_HeadsetComposite");
        if (shader == null || !shader.isSupported)
            return false;

        displayWidth = Screen.width;
        displayHeight = Screen.height;
        float aspect = displayWidth * 0.5f / Mathf.Max(1, displayHeight);
        int width = Mathf.Clamp(Mathf.RoundToInt(displayWidth * 0.5f * renderScale), 256, 1536);
        int height = Mathf.Max(256, Mathf.RoundToInt(width / aspect));
        for (int i = 0; i < eyes.Length; i++)
        {
            eyeTextures[i] = NewTexture(width, height, 24);
            GameObject obj = new GameObject(i == 0 ? "SmartGeoAR_LeftEye" : "SmartGeoAR_RightEye");
            obj.transform.SetParent(viewCamera.transform, false);
            Camera eye = obj.AddComponent<Camera>();
            eye.CopyFrom(viewCamera);
            eye.enabled = false;
            eye.stereoTargetEye = StereoTargetEyeMask.None;
            eye.clearFlags = CameraClearFlags.SolidColor;
            eye.backgroundColor = Color.clear;
            eye.targetTexture = eyeTextures[i];
            eye.rect = new Rect(0f, 0f, 1f, 1f);
            eye.aspect = aspect;
            eye.allowHDR = false;
            eye.allowMSAA = false;
            UniversalAdditionalCameraData data = eye.GetUniversalAdditionalCameraData();
            data.renderType = CameraRenderType.Base;
            data.renderPostProcessing = false;
            data.requiresColorTexture = false;
            data.requiresDepthTexture = false;
            data.cameraStack.Clear();
            eyes[i] = eye;

            GameObject overlay = new GameObject("SmartGeoAR_EyeControls");
            overlay.transform.SetParent(eye.transform, false);
            Camera control = overlay.AddComponent<Camera>();
            control.CopyFrom(eye);
            // URP skips disabled overlays even during an explicit stack render.
            control.enabled = true;
            control.targetTexture = null;
            control.clearFlags = CameraClearFlags.Depth;
            control.cullingMask = 1 << ControlsLayer;
            UniversalAdditionalCameraData controlData = control.GetUniversalAdditionalCameraData();
            controlData.renderType = CameraRenderType.Overlay;
            controlData.renderPostProcessing = false;
            data.cameraStack.Add(control);
            controls[i] = control;
        }

        int cameraWidth = Mathf.Min(displayWidth, 1280);
        cameraTexture = NewTexture(cameraWidth,
            Mathf.Max(2, Mathf.RoundToInt(cameraWidth * (float)displayHeight / displayWidth)), 0);
        commands = new CommandBuffer { name = "SmartGeoAR headset camera background" };
        composite = new Material(shader);
        composite.SetTexture("_LeftEye", eyeTextures[0]);
        composite.SetTexture("_RightEye", eyeTextures[1]);
        composite.SetTexture("_CameraTex", cameraTexture);

        GameObject output = new GameObject("SmartGeoAR_HeadsetOutput");
        output.transform.SetParent(transform, false);
        outputCamera = output.AddComponent<Camera>();
        outputCamera.enabled = false;
        outputCamera.cullingMask = 0;
        outputCamera.clearFlags = CameraClearFlags.SolidColor;
        outputCamera.backgroundColor = Color.black;
        outputCamera.depth = viewCamera.depth + 50f;
        outputCamera.stereoTargetEye = StereoTargetEyeMask.None;
        outputCamera.GetUniversalAdditionalCameraData().renderPostProcessing = false;

        GameObject canvasObject = new GameObject("SmartGeoAR_HeadsetScreen",
            typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        outputCanvas = canvasObject.GetComponent<Canvas>();
        outputCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        outputCanvas.sortingOrder = 32760;
        outputCanvas.enabled = false;
        GameObject imageObject = new GameObject("StereoImage", typeof(RectTransform), typeof(RawImage));
        imageObject.transform.SetParent(canvasObject.transform, false);
        RectTransform rect = imageObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        RawImage image = imageObject.GetComponent<RawImage>();
        image.texture = Texture2D.whiteTexture;
        image.material = composite;
        image.raycastTarget = true;
        return true;
    }

    private static RenderTexture NewTexture(int width, int height, int depth)
    {
        var texture = new RenderTexture(width, height, depth, RenderTextureFormat.ARGB32)
        {
            name = "SmartGeoAR headset image",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        texture.Create();
        return texture;
    }

    private void CaptureBackground()
    {
        Material material = background.material;
        Texture texture = material.HasProperty("_MainTex") ? material.GetTexture("_MainTex") : null;
        RenderTexture previous = RenderTexture.active;
        try
        {
            Graphics.SetRenderTarget(cameraTexture);
            commands.Clear();
            commands.ClearRenderTarget(false, true, Color.black);
            AppendCameraBackgroundHook(commands);
            commands.Blit(texture, BuiltinRenderTextureType.CurrentActive, material);
            Graphics.ExecuteCommandBuffer(commands);
        }
        finally
        {
            RenderTexture.active = previous;
        }
    }

    // The AR camera no longer renders while using the headset. Replace its
    // background pass's render-thread hook before reading ARCore's texture.
    // The phone-frame sender can append this to its own capture buffer too.
    public static void AppendCameraBackgroundHook(CommandBuffer commandBuffer)
    {
        if (!requested || instance == null || instance.cameraManager == null)
            return;
        XRCameraSubsystem subsystem = instance.cameraManager.subsystem;
        if (subsystem == null || !subsystem.running)
            return;
        renderSubsystem = subsystem;
        commandBuffer.IssuePluginEvent(beforeBackgroundRenderPointer, Time.frameCount);
    }

    [MonoPInvokeCallback(typeof(Action<int>))]
    private static void BeforeBackgroundRender(int frame)
    {
        // Several consumers can sample the same image. Wait on its native
        // fence once, immediately before the first sample on the GPU thread.
        XRCameraSubsystem subsystem = renderSubsystem;
        if (subsystem == null || lastBackgroundFrame == frame)
            return;
        subsystem.OnBeforeBackgroundRender(0);
        lastBackgroundFrame = frame;
    }

    private void StopRendering()
    {
        if (rendering)
        {
            if (viewCamera != null)
                viewCamera.enabled = originalCameraEnabled;
            Screen.sleepTimeout = originalSleepTimeout;
            Application.targetFrameRate = originalFrameRate;
            rendering = false;
        }
        if (outputCanvas != null)
        {
            outputCanvas.enabled = false;
            Destroy(outputCanvas.gameObject);
        }
        if (outputCamera != null)
        {
            outputCamera.enabled = false;
            Destroy(outputCamera.gameObject);
        }
        for (int i = 0; i < eyes.Length; i++)
        {
            if (eyes[i] != null)
                Destroy(eyes[i].gameObject);
            Release(eyeTextures[i]);
            eyeTextures[i] = null;
            eyes[i] = controls[i] = null;
        }
        Release(cameraTexture);
        cameraTexture = null;
        if (composite != null)
            Destroy(composite);
        composite = null;
        outputCanvas = null;
        outputCamera = null;
        commands?.Dispose();
        commands = null;
    }

    private static void Release(RenderTexture texture)
    {
        if (texture == null)
            return;
        texture.Release();
        Destroy(texture);
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused)
            StopRendering();
    }

    private void OnDisable()
    {
        StopRendering();
        if (instance == this)
        {
            instance = null;
            renderSubsystem = null;
            lastBackgroundFrame = -1;
        }
    }
}
