using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class PhoneCameraSender : MonoBehaviour
{
    [Serializable]
    public class CameraIntrinsics
    {
        public float fx;
        public float fy;
        public float cx;
        public float cy;
    }

    [Serializable]
    private class CameraPacket
    {
        public string source = "role3_unity";
        public string type = "phone_camera_frame";
        public int frame_id;
        public int width;
        public int height;
        public string jpeg;
        public CameraIntrinsics intrinsics;
    }

    private struct CapturedFrame
    {
        public Matrix4x4 cameraToWorld;
        public float capturedAt;
    }

    private static readonly Dictionary<int, CapturedFrame> frames =
        new Dictionary<int, CapturedFrame>();
    private static readonly Queue<int> frameOrder = new Queue<int>();
    private static int nextFrameId;

    private ARCameraManager cameraManager;
    private ARCameraBackground background;
    private Camera viewCamera;
    private RenderTexture target;
    private Texture2D pixels;
    private CommandBuffer commands;
    private Task<bool> sending;
    private int waitingFrame = -1;
    private float sentAt;
    private float nextCapture;
    private float nextWarning;
    private bool newCameraFrame;

    public static void Ensure(ARCameraManager manager = null)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (manager == null)
            manager = FindAnyObjectByType<ARCameraManager>();

        if (manager != null)
        {
            PhoneDepthOcclusion.Ensure(manager);
            HandOcclusionProxy.Ensure(manager);
            PhoneHeadsetDisplay.Ensure(manager);
            if (manager.GetComponent<PhoneCameraSender>() == null)
                manager.gameObject.AddComponent<PhoneCameraSender>();
        }
#endif
    }

    private void OnEnable()
    {
        cameraManager = GetComponent<ARCameraManager>();
        background = GetComponent<ARCameraBackground>();
        viewCamera = GetComponent<Camera>();
        commands = new CommandBuffer { name = "SmartGeoAR rear camera frame" };
        cameraManager.requestedFacingDirection = CameraFacingDirection.World;
        cameraManager.frameReceived += OnCameraFrame;
        HandDataReceiver.PacketReceived += OnHands;
        HandDataReceiver.ConnectionChanged += OnConnection;
    }

    private void OnCameraFrame(ARCameraFrameEventArgs args)
    {
        newCameraFrame = true;
    }

    private void OnHands(HandDataReceiver.HandPacket packet)
    {
        if (packet.camera_source == "phone_rear" && packet.frame_id == waitingFrame)
            waitingFrame = -1;
    }

    private void OnConnection(bool connected)
    {
        waitingFrame = -1;
        frames.Clear();
        frameOrder.Clear();
    }

    private void LateUpdate()
    {
        if (!HandDataReceiver.IsConnected || ARSession.state != ARSessionState.SessionTracking ||
            !newCameraFrame || Time.unscaledTime < nextCapture ||
            background == null || background.material == null ||
            cameraManager.currentFacingDirection != CameraFacingDirection.World)
            return;

        if (sending != null && !sending.IsCompleted)
            return;

        if (waitingFrame >= 0 && Time.unscaledTime - sentAt < 0.75f)
            return;

        newCameraFrame = false;
        nextCapture = Time.unscaledTime + 1f / 15f;

        try
        {
            CaptureAndSend();
        }
        catch (Exception error)
        {
            waitingFrame = -1;
            if (Time.unscaledTime >= nextWarning)
            {
                nextWarning = Time.unscaledTime + 3f;
                Debug.LogWarning("[SmartGeoAR] Rear camera frame: " + error.Message);
            }
        }
    }

    public static CameraIntrinsics IntrinsicsForProjection(Matrix4x4 projection, int width, int height)
    {
        return new CameraIntrinsics
        {
            fx = Mathf.Abs(projection.m00) * width * 0.5f,
            fy = Mathf.Abs(projection.m11) * height * 0.5f,
            cx = (1f - projection.m02) * width * 0.5f,
            cy = (1f + projection.m12) * height * 0.5f
        };
    }

    private void CaptureAndSend()
    {
        int width = 640;
        int height = Mathf.Max(2, Mathf.RoundToInt(width * (float)Screen.height / Screen.width));

        if (target == null || target.width != width || target.height != height)
        {
            ReleaseImages();
            target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            target.Create();
            pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
        }

        // Blit only ARCore's camera background, before virtual objects/UI.
        // Its material applies the same rotation/crop as the phone display.
        Material material = background.material;
        Texture texture = material.HasProperty("_MainTex") ? material.GetTexture("_MainTex") : null;
        RenderTexture previous = RenderTexture.active;
        Matrix4x4 pose = viewCamera.transform.localToWorldMatrix;
        CameraIntrinsics intrinsics = IntrinsicsForProjection(viewCamera.projectionMatrix, width, height);
        byte[] jpeg;

        try
        {
            Graphics.SetRenderTarget(target);
            commands.Clear();
            commands.ClearRenderTarget(true, true, Color.black);
            PhoneHeadsetDisplay.AppendCameraBackgroundHook(commands);
            commands.Blit(texture, BuiltinRenderTextureType.CurrentActive, material);
            Graphics.ExecuteCommandBuffer(commands);
            pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
            pixels.Apply(false, false);
            jpeg = pixels.EncodeToJPG(65);
        }
        finally
        {
            RenderTexture.active = previous;
        }

        int id = ++nextFrameId;
        sentAt = Time.unscaledTime;
        frames[id] = new CapturedFrame { cameraToWorld = pose, capturedAt = sentAt };
        frameOrder.Enqueue(id);
        while (frameOrder.Count > 32)
            frames.Remove(frameOrder.Dequeue());

        string json = JsonUtility.ToJson(new CameraPacket
        {
            frame_id = id,
            width = width,
            height = height,
            jpeg = Convert.ToBase64String(jpeg),
            intrinsics = intrinsics
        });

        waitingFrame = id;
        sending = HandDataReceiver.SendPhoneCameraJsonAsync(json);
        if (id % 30 == 1)
            SpatialWorkspace.LogInteraction("Rear camera sent: " + id + " " + width + "x" + height);
    }

    public static bool TryMap(int frameId, Vector3 cameraPoint, out Vector3 worldPoint)
    {
        worldPoint = Vector3.zero;
        if (cameraPoint.z <= 0f || !TryGetCapturePose(frameId, out Matrix4x4 pose))
            return false;

        // Use the pose at image capture, not the phone pose when Wi-Fi replies.
        worldPoint = pose.MultiplyPoint3x4(cameraPoint);
        return true;
    }

    public static bool TryGetCapturePose(int frameId, out Matrix4x4 pose, float maxAge = 0.65f)
    {
        pose = Matrix4x4.identity;
        if (!frames.TryGetValue(frameId, out CapturedFrame frame) ||
            Time.unscaledTime - frame.capturedAt > maxAge)
            return false;

        pose = frame.cameraToWorld;
        return true;
    }

    public static float CaptureAgeSeconds(int frameId)
    {
        return frames.TryGetValue(frameId, out CapturedFrame frame)
            ? Time.unscaledTime - frame.capturedAt : float.PositiveInfinity;
    }

    private void ReleaseImages()
    {
        if (target != null)
        {
            target.Release();
            Destroy(target);
        }
        if (pixels != null)
            Destroy(pixels);
    }

    private void OnDisable()
    {
        if (cameraManager != null)
            cameraManager.frameReceived -= OnCameraFrame;
        HandDataReceiver.PacketReceived -= OnHands;
        HandDataReceiver.ConnectionChanged -= OnConnection;
        frames.Clear();
        frameOrder.Clear();
        waitingFrame = -1;
        commands?.Dispose();
        ReleaseImages();
    }
}
