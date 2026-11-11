using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class PhoneDepthOcclusion : MonoBehaviour
{
    // Reserve a camera-only layer for recovery controls. Real-world depth
    // may hide a model, but must not hide the buttons used to recover it.
    private const int ControlsLayer = 30;

    // ARCore depth currently fails on the test phone. HandOcclusionProxy
    // supplies hand depth without requesting that environment-depth stream.
    [SerializeField] private bool useEnvironmentDepth;

    private AROcclusionManager occlusion;
    private Camera viewCamera;
    private Camera controlsCamera;
    private UniversalAdditionalCameraData cameraData;
    private bool restoreControlsLayer;
    private bool depthReady;
    private bool renderingReady;
    private bool unsupportedReported;

    public static void Ensure(ARCameraManager manager)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (manager != null && manager.GetComponent<PhoneDepthOcclusion>() == null)
            manager.gameObject.AddComponent<PhoneDepthOcclusion>();
#endif
    }

    private void OnEnable()
    {
        viewCamera = GetComponent<Camera>();
        occlusion = GetComponent<AROcclusionManager>();
        if (occlusion == null)
            occlusion = gameObject.AddComponent<AROcclusionManager>();

        RequestDepth();
        // An enabled manager makes ARBackgroundRendererFeature request a
        // depth prepass even when its requested depth mode is Disabled.
        occlusion.enabled = useEnvironmentDepth;
    }

    private void RequestDepth()
    {
        occlusion.requestedEnvironmentDepthMode = useEnvironmentDepth
            ? EnvironmentDepthMode.Fastest : EnvironmentDepthMode.Disabled;
        occlusion.requestedOcclusionPreferenceMode = useEnvironmentDepth
            ? OcclusionPreferenceMode.PreferEnvironmentOcclusion : OcclusionPreferenceMode.NoOcclusion;
        occlusion.environmentDepthTemporalSmoothingRequested = useEnvironmentDepth;
    }

    public void ConfigureControls(GameObject controls)
    {
        if (viewCamera == null)
            viewCamera = GetComponent<Camera>();

        if (controlsCamera == null)
        {
            cameraData = viewCamera.GetUniversalAdditionalCameraData();
            if (cameraData.cameraStack == null)
                return;

            GameObject obj = new GameObject("SmartGeoAR_ControlsCamera");
            obj.transform.SetParent(viewCamera.transform, false);
            controlsCamera = obj.AddComponent<Camera>();
            controlsCamera.CopyFrom(viewCamera);
            controlsCamera.clearFlags = CameraClearFlags.Depth;
            controlsCamera.cullingMask = 1 << ControlsLayer;
            controlsCamera.GetUniversalAdditionalCameraData().renderType = CameraRenderType.Overlay;
            cameraData.cameraStack.Add(controlsCamera);
            restoreControlsLayer = (viewCamera.cullingMask & (1 << ControlsLayer)) != 0;
            viewCamera.cullingMask &= ~(1 << ControlsLayer);
        }

        foreach (Transform child in controls.GetComponentsInChildren<Transform>(true))
            child.gameObject.layer = ControlsLayer;
    }

    public void ConfigurePointer(GameObject pointer)
    {
        // The indicator stays visible; grabbing still uses its real 3D
        // position and collider distance, not the screen overlap.
        if (controlsCamera != null)
            pointer.layer = ControlsLayer;
    }

    private void LateUpdate()
    {
        if (controlsCamera != null)
        {
            controlsCamera.nearClipPlane = viewCamera.nearClipPlane;
            controlsCamera.farClipPlane = viewCamera.farClipPlane;
            controlsCamera.projectionMatrix = viewCamera.projectionMatrix;
            controlsCamera.rect = viewCamera.rect;
        }

        if (!useEnvironmentDepth || ARSession.state != ARSessionState.SessionTracking || occlusion == null)
            return;

        if (occlusion.requestedEnvironmentDepthMode != EnvironmentDepthMode.Fastest)
            RequestDepth();

        if (!depthReady && occlusion.TryGetEnvironmentDepthTexture(out Texture texture))
        {
            depthReady = true;
            SpatialWorkspace.LogInteraction("ARCore depth ready: " + texture.width + "x" + texture.height);
        }
        else if (!unsupportedReported &&
            occlusion.descriptor?.environmentDepthImageSupported == Supported.Unsupported)
        {
            unsupportedReported = true;
            Debug.LogWarning("[SmartGeoAR] ARCore depth is unavailable on this device.");
        }

        ARCameraBackground background = GetComponent<ARCameraBackground>();
        if (!renderingReady && depthReady && background != null && background.material != null &&
            background.material.IsKeywordEnabled("ARCORE_ENVIRONMENT_DEPTH_ENABLED"))
        {
            renderingReady = true;
            SpatialWorkspace.LogInteraction("ARCore depth rendering active");
        }
    }

    private void OnDisable()
    {
        if (controlsCamera != null)
        {
            if (cameraData != null && cameraData.cameraStack != null)
                cameraData.cameraStack.Remove(controlsCamera);
            Destroy(controlsCamera.gameObject);
        }
        if (viewCamera != null && restoreControlsLayer)
            viewCamera.cullingMask |= 1 << ControlsLayer;
    }
}
