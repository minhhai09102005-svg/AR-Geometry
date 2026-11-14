
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;

public class HandDataReceiver : MonoBehaviour
{
    // ==========================================
    // DATA STRUCTURES
    // ==========================================

    [Serializable]
    public class InteractionData
    {
        public float x;
        public float y;
    }

    [Serializable]
    public class SpatialPoint
    {
        public float x;
        public float y;
        public float z;
    }

    [Serializable]
    public class SpatialData
    {
        public bool valid;
        public string coordinate_system;
        public bool estimated;

        public SpatialPoint point;
        public SpatialPoint index_tip;
        public SpatialPoint[] landmarks;

        public int camera_frame_id;
    }

    [Serializable]
    public class HandData
    {
        public string hand;
        public string gesture;
        public InteractionData interaction;
        public float pinch_ratio;
        public SpatialData spatial;
    }

    [Serializable]
    public class HandPacket
    {
        public string version;
        public string source;
        public string camera_source;
        public string coordinate_system;

        public int frame_id;
        public HandData[] hands;
    }

    [Serializable]
    private class Envelope
    {
        public string source;
        public string type;
        public string message;
        public string jpeg;
    }

    public static event Action<HandPacket>
        PacketReceived;

    public static event Action<string>
        GeometryResultReceived;

    public static event Action<bool>
        ConnectionChanged;

    // ==========================================
    // SETTINGS
    // ==========================================

#if UNITY_ANDROID && !UNITY_EDITOR
    private const string HOST = "192.168.1.9";
#else
    private const string HOST = "127.0.0.1";
#endif

    private const int PORT = 5053;

    // Target 30 FPS for webcam display.
    private const float CameraInterval = 1f / 30f;

    private static HandDataReceiver instance;

    private readonly object socketLock =
        new object();

    private readonly object messageLock =
        new object();

    // Geometry results must not be discarded.
    private readonly Queue<string> importantMessages =
        new Queue<string>();

    // Preserve pinch transitions even when
    // older normal tracking frames are skipped.
    private readonly Queue<string> gestureEvents =
        new Queue<string>();

    private string latestHandPacket;
    private string latestCameraPacket;

    private TcpClient tcpClient;
    private NetworkStream stream;
    private Thread networkThread;

    private volatile bool running;
    private volatile bool connected;

    private bool previousConnection;
    private bool cameraReady;

    private float nextCameraUpdate;

    private Texture2D cameraTexture;
    private Material cameraMaterial;
    private GameObject cameraQuad;

    private int displayedCameraFrames;
    private int receivedHandFrames;
    private float lastFpsReport;

    public static bool IsConnected
    {
        get
        {
            return instance != null &&
                   instance.connected;
        }
    }

    // ==========================================
    // INITIALIZATION
    // ==========================================

    private void Awake()
    {
        if (
            instance != null &&
            instance != this
        )
        {
            enabled = false;
            return;
        }

        instance = this;
    }

    private void Start()
    {
        if (!enabled)
            return;

        running = true;

#if UNITY_ANDROID && !UNITY_EDITOR
        PhoneCameraSender.Ensure();
#endif

        networkThread = new Thread(NetworkLoop)
        {
            IsBackground = true,
            Name = "SmartGeoAR_TCP"
        };

        networkThread.Start();

        lastFpsReport = Time.unscaledTime;
    }

    // ==========================================
    // NETWORK RECEIVER THREAD
    // ==========================================

    private void NetworkLoop()
    {
        while (running)
        {
            TcpClient current = null;

            try
            {
                current = new TcpClient();

                current.NoDelay = true;

                Task task =
                    current.ConnectAsync(
                        HOST,
                        PORT
                    );

                if (!task.Wait(2500))
                {
                    // Observe a possible late fault.
                    task.ContinueWith(
                        t =>
                        {
                            var ignored = t.Exception;
                        },
                        TaskContinuationOptions
                            .OnlyOnFaulted
                    );

                    throw new IOException(
                        "Connection timeout"
                    );
                }

                if (!running)
                    break;

                lock (socketLock)
                {
                    tcpClient = current;
                    stream = current.GetStream();

                    stream.ReadTimeout = 10000;
                    stream.WriteTimeout = 1500;

                    connected = true;
                }

                using (
                    StreamReader reader =
                        new StreamReader(
                            current.GetStream(),
                            Encoding.UTF8
                        )
                )
                {
                    string line;

                    while (
                        running &&
                        (line = reader.ReadLine()) != null
                    )
                    {
                        if (
                            line.Length == 0 ||
                            line.Length > 1800000
                        )
                        {
                            continue;
                        }

                        ClassifyPacket(line);
                    }
                }
            }
            catch (
                SocketException
            )
            {
            }
            catch (
                IOException
            )
            {
            }
            catch (
                AggregateException
            )
            {
            }
            catch (
                ObjectDisposedException
            )
            {
            }
            catch (Exception error)
            {
                if (running)
                {
                    Debug.LogWarning(
                        "[SmartGeoAR] Network: " +
                        error.Message
                    );
                }
            }
            finally
            {
                lock (socketLock)
                {
                    connected = false;

                    if (tcpClient == current)
                    {
                        stream = null;
                        tcpClient = null;
                    }
                }

                try
                {
                    current?.Close();
                }
                catch
                {
                }

                lock (messageLock)
                {
                    latestHandPacket = null;
                    latestCameraPacket = null;

                    gestureEvents.Clear();
                }
            }

            if (running)
            {
                Thread.Sleep(350);
            }
        }
    }

    // ==========================================
    // CLASSIFY WITHOUT DECODING JPEG
    // ==========================================

    private void ClassifyPacket(string json)
    {
        if (string.IsNullOrEmpty(json))
            return;

        bool cameraPacket =
            json.IndexOf(
                "\"source\":\"role1_camera\"",
                StringComparison.Ordinal
            ) >= 0;

        bool handPacket =
            json.IndexOf(
                "\"source\":\"role1_hand_tracking\"",
                StringComparison.Ordinal
            ) >= 0;

        lock (messageLock)
        {
            if (cameraPacket)
            {
                // Replace old camera image.
                latestCameraPacket = json;
                return;
            }

            if (handPacket)
            {
                bool gestureTransition =
                    json.IndexOf(
                        "\"PINCH_START\"",
                        StringComparison.Ordinal
                    ) >= 0 ||
                    json.IndexOf(
                        "\"PINCH_END\"",
                        StringComparison.Ordinal
                    ) >= 0;

                if (gestureTransition)
                {
                    // Transitions must be delivered.
                    if (gestureEvents.Count >= 16)
                    {
                        gestureEvents.Dequeue();
                    }

                    gestureEvents.Enqueue(json);
                }
                else
                {
                    // Regular OPEN / HOLD frames:
                    // retain only the latest one.
                    latestHandPacket = json;
                }

                return;
            }

            if (importantMessages.Count >= 64)
            {
                importantMessages.Dequeue();
            }

            importantMessages.Enqueue(json);
        }
    }

    // ==========================================
    // UNITY MAIN THREAD
    // ==========================================

    private void Update()
    {
        if (connected != previousConnection)
        {
            previousConnection = connected;

            ConnectionChanged?.Invoke(connected);

            Debug.Log(
                connected
                    ? "[SmartGeoAR] Backend CONNECTED"
                    : "[SmartGeoAR] Backend DISCONNECTED"
            );
        }

        ProcessControlMessages();
        ProcessHandMessages();

#if !UNITY_ANDROID || UNITY_EDITOR
        ProcessLatestCameraFrame();
#endif

        ReportFps();
    }

    // ==========================================
    // CONTROL AND GEOMETRY RESULTS
    // ==========================================

    private void ProcessControlMessages()
    {
        for (int i = 0; i < 12; i++)
        {
            string json = null;

            lock (messageLock)
            {
                if (importantMessages.Count > 0)
                {
                    json = importantMessages.Dequeue();
                }
            }

            if (json == null)
                break;

            try
            {
                Envelope envelope =
                    JsonUtility.FromJson<Envelope>(
                        json
                    );

                if (envelope == null)
                    continue;

                if (
                    envelope.source ==
                    "role2_geometry_reasoning"
                )
                {
                    GeometryResultReceived?.Invoke(
                        json
                    );
                }
                else if (
                    envelope.source ==
                    "smartgeoar_backend"
                )
                {
                    if (envelope.type == "hello")
                    {
                        Debug.Log(
                            "[SmartGeoAR] Backend ready"
                        );
                    }
                    else if (
                        envelope.type == "camera_error"
                    )
                    {
                        Debug.LogError(
                            "[SmartGeoAR] Camera: " +
                            envelope.message
                        );
                    }
                }
            }
            catch (Exception error)
            {
                Debug.LogWarning(
                    "[SmartGeoAR] Control: " +
                    error.Message
                );
            }
        }
    }

    // ==========================================
    // PROCESS BOTH HANDS
    // ==========================================

    private void ProcessHandMessages()
    {
        // Deliver pinch transitions first.
        // A fast hand movement must not
        // lose its PINCH_START / PINCH_END event.
        for (int i = 0; i < 8; i++)
        {
            string json = null;

            lock (messageLock)
            {
                if (gestureEvents.Count > 0)
                {
                    json = gestureEvents.Dequeue();
                }
            }

            if (json == null)
                break;

            DispatchHands(json);
        }

        // Then update both cursors using
        // the newest available hand positions.
        string latest;

        lock (messageLock)
        {
            latest = latestHandPacket;
            latestHandPacket = null;
        }

        if (!string.IsNullOrEmpty(latest))
        {
            DispatchHands(latest);
        }
    }

    private void DispatchHands(string json)
    {
        try
        {
            HandPacket packet =
                JsonUtility.FromJson<HandPacket>(
                    json
                );

            if (
                packet == null ||
                packet.hands == null
            )
            {
                return;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            if (
                packet.camera_source !=
                "phone_rear"
            )
            {
                return;
            }
#endif

            // One event contains both hands.
            PacketReceived?.Invoke(packet);

            receivedHandFrames++;
        }
        catch (Exception error)
        {
            Debug.LogWarning(
                "[SmartGeoAR] Hands: " +
                error.Message
            );
        }
    }

    // ==========================================
    // CAMERA DISPLAY AT UP TO 30 FPS
    // ==========================================

    private void ProcessLatestCameraFrame()
    {
        float now = Time.unscaledTime;

        if (now < nextCameraUpdate)
            return;

        nextCameraUpdate =
            now + CameraInterval;

        string latest;

        lock (messageLock)
        {
            latest = latestCameraPacket;
            latestCameraPacket = null;
        }

        if (string.IsNullOrEmpty(latest))
            return;

        try
        {
            Envelope envelope =
                JsonUtility.FromJson<Envelope>(
                    latest
                );

            if (
                envelope == null ||
                envelope.source !=
                    "role1_camera" ||
                string.IsNullOrEmpty(
                    envelope.jpeg
                )
            )
            {
                return;
            }

            ShowCameraFrame(
                envelope.jpeg
            );
        }
        catch (Exception error)
        {
            Debug.LogWarning(
                "[SmartGeoAR] Video: " +
                error.Message
            );
        }
    }

    private void ShowCameraFrame(
        string encoded
    )
    {
        byte[] bytes;

        try
        {
            bytes = Convert.FromBase64String(
                encoded
            );
        }
        catch (FormatException)
        {
            return;
        }

        if (cameraTexture == null)
        {
            cameraTexture = new Texture2D(
                2,
                2,
                TextureFormat.RGB24,
                false
            );

            cameraTexture.wrapMode =
                TextureWrapMode.Clamp;

            cameraTexture.filterMode =
                FilterMode.Bilinear;
        }

        if (!ImageConversion.LoadImage(
            cameraTexture,
            bytes,
            false
        ))
        {
            return;
        }

        if (cameraQuad == null)
        {
            CreateCameraBackground();
        }

        if (cameraMaterial == null)
            return;

        displayedCameraFrames++;

        if (!cameraReady)
        {
            cameraReady = true;

            Debug.Log(
                "[SmartGeoAR] Camera ready: " +
                cameraTexture.width +
                "x" +
                cameraTexture.height
            );
        }
    }

    // ==========================================
    // CAMERA BACKGROUND
    // ==========================================

    private void CreateCameraBackground()
    {
        Camera targetCamera = Camera.main;

        if (targetCamera == null)
            return;

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        ARCameraBackground background =
            targetCamera.GetComponent<
                ARCameraBackground
            >();

        if (background != null)
        {
            background.enabled = false;
        }
#endif

        Material template =
            Resources.Load<Material>(
                "SmartGeoAR_Unlit"
            );

        if (template != null)
        {
            cameraMaterial =
                new Material(template);
        }
        else
        {
            Shader shader = Shader.Find(
                "Universal Render Pipeline/Unlit"
            );

            if (shader == null)
            {
                shader = Shader.Find(
                    "Unlit/Texture"
                );
            }

            if (shader == null)
                return;

            cameraMaterial = new Material(
                shader
            );
        }

        cameraMaterial.name =
            "SmartGeoAR_CameraBackground";

        if (
            cameraMaterial.HasProperty("_BaseColor")
        )
        {
            cameraMaterial.SetColor(
                "_BaseColor",
                Color.white
            );
        }

        if (
            cameraMaterial.HasProperty("_Color")
        )
        {
            cameraMaterial.SetColor(
                "_Color",
                Color.white
            );
        }

        if (cameraMaterial.HasProperty("_Cull"))
        {
            cameraMaterial.SetFloat(
                "_Cull",
                0f
            );
        }

        cameraMaterial.renderQueue = 2000;

        // Bind texture only once.
        // Later LoadImage updates it in place.
        cameraMaterial.mainTexture =
            cameraTexture;

        if (cameraMaterial.HasProperty("_BaseMap"))
        {
            cameraMaterial.SetTexture(
                "_BaseMap",
                cameraTexture
            );
        }

        cameraQuad =
            GameObject.CreatePrimitive(
                PrimitiveType.Quad
            );

        cameraQuad.name =
            "SmartGeoAR_CameraBehindModel";

        Collider collider =
            cameraQuad.GetComponent<Collider>();

        if (collider != null)
        {
            Destroy(collider);
        }

        cameraQuad.transform.SetParent(
            targetCamera.transform,
            false
        );

        float depth = Mathf.Clamp(
            2.5f,
            targetCamera.nearClipPlane + 0.1f,
            targetCamera.farClipPlane * 0.8f
        );

        float height;

        if (targetCamera.orthographic)
        {
            height =
                targetCamera.orthographicSize * 2f;
        }
        else
        {
            height =
                2f * depth *
                Mathf.Tan(
                    targetCamera.fieldOfView *
                    Mathf.Deg2Rad * 0.5f
                );
        }

        float width =
            height * targetCamera.aspect;

        cameraQuad.transform.localPosition =
            Vector3.forward * depth;

        cameraQuad.transform.localRotation =
            Quaternion.identity;

        cameraQuad.transform.localScale =
            new Vector3(
                width * 1.01f,
                height * 1.01f,
                1f
            );

        MeshRenderer renderer =
            cameraQuad.GetComponent<MeshRenderer>();

        renderer.sharedMaterial =
            cameraMaterial;

        renderer.shadowCastingMode =
            ShadowCastingMode.Off;

        renderer.receiveShadows = false;
    }

    // ==========================================
    // FPS DIAGNOSTICS
    // ==========================================

    private void ReportFps()
    {
        float now = Time.unscaledTime;
        float elapsed = now - lastFpsReport;

        if (elapsed < 5f)
            return;

        if (Debug.isDebugBuild)
        {
            Debug.Log(
                "[SmartGeoAR FPS] video=" +
                (
                    displayedCameraFrames /
                    elapsed
                ).ToString("F1") +
                "; hands=" +
                (
                    receivedHandFrames /
                    elapsed
                ).ToString("F1")
            );
        }

        displayedCameraFrames = 0;
        receivedHandFrames = 0;

        lastFpsReport = now;
    }

    // ==========================================
    // SEND GEOMETRY REQUESTS
    // ==========================================

    public static bool SendGeometryJson(
        string json
    )
    {
        return instance != null &&
               instance.SendLine(json);
    }

    public static Task<bool>
        SendPhoneCameraJsonAsync(
            string json
        )
    {
        if (instance == null)
        {
            return Task.FromResult(false);
        }

        HandDataReceiver receiver =
            instance;

        return Task.Run(
            () => receiver.SendLine(json)
        );
    }

    private bool SendLine(string json)
    {
        if (string.IsNullOrEmpty(json))
            return false;

        byte[] bytes = Encoding.UTF8.GetBytes(
            json.TrimEnd('\r', '\n') + "\n"
        );

        lock (socketLock)
        {
            if (
                !connected ||
                stream == null
            )
            {
                return false;
            }

            try
            {
                stream.Write(
                    bytes,
                    0,
                    bytes.Length
                );

                return true;
            }
            catch
            {
                connected = false;

                try
                {
                    tcpClient?.Close();
                }
                catch
                {
                }

                return false;
            }
        }
    }

    // ==========================================
    // CLEANUP
    // ==========================================

    private void StopReceiver()
    {
        running = false;

        lock (socketLock)
        {
            connected = false;

            try
            {
                tcpClient?.Close();
            }
            catch
            {
            }

            tcpClient = null;
            stream = null;
        }

        if (
            networkThread != null &&
            networkThread.IsAlive
        )
        {
            networkThread.Join(500);
        }

        networkThread = null;

        lock (messageLock)
        {
            latestHandPacket = null;
            latestCameraPacket = null;

            gestureEvents.Clear();
            importantMessages.Clear();
        }
    }

    private void OnDisable()
    {
        if (instance == this)
        {
            StopReceiver();
        }
    }

    private void OnDestroy()
    {
        if (instance != this)
            return;

        StopReceiver();

        if (cameraQuad != null)
            Destroy(cameraQuad);

        if (cameraMaterial != null)
            Destroy(cameraMaterial);

        if (cameraTexture != null)
            Destroy(cameraTexture);

        instance = null;
    }

    private void OnApplicationQuit()
    {
        if (instance == this)
        {
            StopReceiver();
        }
    }
}
