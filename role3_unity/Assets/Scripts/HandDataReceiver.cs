
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    // =====================================================
    // ROLE 1 DATA
    // =====================================================

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
        public int frame_id;
        public string coordinate_system;
        public string camera_source;
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

    // =====================================================
    // EVENTS FOR SCAN SCREEN
    // =====================================================

    public static event Action<HandPacket> PacketReceived;

    public static event Action<string> GeometryResultReceived;

    public static event Action<bool> ConnectionChanged;

    private static HandDataReceiver instance;

    // Set this to the laptop's Wi-Fi IPv4 address before building the APK.
    private const string HOST = "192.168.1.9";
    private const int PORT = 5053;

    private readonly object socketLock = new object();
    private readonly object queueLock = new object();

    private readonly Queue<string> messageQueue =
        new Queue<string>();

    private Thread networkThread;

    private TcpClient tcpClient;
    private NetworkStream stream;

    private Process backendProcess;

    private volatile bool running;
    private volatile bool connected;

    private bool previousConnection;
    private bool receivedCameraFrame;

    // Webcam is rendered ONLY behind the 3D objects.
    private Texture2D cameraTexture;
    private Material cameraMaterial;
    private GameObject cameraQuad;

    public static bool IsConnected
    {
        get
        {
            return instance != null && instance.connected;
        }
    }

    // =====================================================
    // LIFECYCLE
    // =====================================================

    private void Awake()
    {
        if (instance != null && instance != this)
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

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        StartHiddenBackend();
#endif

        PhoneCameraSender.Ensure();

        networkThread = new Thread(NetworkLoop);
        networkThread.IsBackground = true;
        networkThread.Start();
    }

    // =====================================================
    // FIND PROJECT ROOT
    // =====================================================

    private static string FindProjectRoot()
    {
        DirectoryInfo directory =
            new DirectoryInfo(Application.dataPath);

        for (int i = 0; i < 8 && directory != null; i++)
        {
            string backend = Path.Combine(
                directory.FullName,
                "smartgeoar_backend.py"
            );

            if (File.Exists(backend))
                return directory.FullName;

            directory = directory.Parent;
        }

        return null;
    }

    // =====================================================
    // LAUNCH PYTHON IN BACKGROUND
    // =====================================================

    private void StartHiddenBackend()
    {
        string executable = null;
        string arguments = "";
        string workingDirectory = "";

        string root = FindProjectRoot();

        // Prefer current development Python code
        // when testing on this PC.
        if (!string.IsNullOrEmpty(root))
        {
            string script = Path.Combine(
                root,
                "smartgeoar_backend.py"
            );

            string[] candidates =
            {
                Path.Combine(
                    root, ".venv-cv", "Scripts", "pythonw.exe"
                ),
                Path.Combine(
                    root, ".venv-cv", "Scripts", "python.exe"
                ),
                Path.Combine(
                    root, ".venv", "Scripts", "pythonw.exe"
                ),
                Path.Combine(
                    root, ".venv", "Scripts", "python.exe"
                )
            };

            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    executable = candidate;
                    arguments = "\"" + script + "\" --camera-source webcam";
                    workingDirectory = root;
                    break;
                }
            }
        }

        // Fallback: packaged backend for distribution.
        if (string.IsNullOrEmpty(executable))
        {
            string buildFolder =
                Directory.GetParent(
                    Application.dataPath
                ).FullName;

            string bundled = Path.Combine(
                buildFolder,
                "SmartGeoAR_Backend.exe"
            );

            if (File.Exists(bundled))
            {
                executable = bundled;
                workingDirectory = buildFolder;
            }
        }

        if (string.IsNullOrEmpty(executable))
        {
            UnityEngine.Debug.LogError(
                "[SmartGeoAR] Python backend not found."
            );
            return;
        }

        try
        {
            ProcessStartInfo startInfo =
                new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

            backendProcess = Process.Start(startInfo);

            UnityEngine.Debug.Log(
                "[SmartGeoAR] Backend launched: " +
                executable
            );
        }
        catch (Exception error)
        {
            UnityEngine.Debug.LogError(
                "[SmartGeoAR] Backend launch failed: " +
                error.Message
            );
        }
    }

    // =====================================================
    // TCP CONNECTION
    // =====================================================

    private void NetworkLoop()
    {
        while (running)
        {
            TcpClient current = null;

            try
            {
                current = new TcpClient();
                current.NoDelay = true;

                Task connection = current.ConnectAsync(HOST, PORT);

                if (!connection.Wait(3000))
                {
                    // Closing the socket below may fault the pending task.
                    // Observe that failure without disposing its wait handle.
                    connection.ContinueWith(
                        task => { var ignored = task.Exception; },
                        TaskContinuationOptions.OnlyOnFaulted
                    );
                    throw new SocketException((int)SocketError.TimedOut);
                }

                if (!running)
                    break;

                lock (socketLock)
                {
                    tcpClient = current;
                    stream = current.GetStream();
                    // Hand packets arrive even when no hands are visible.
                    // A silent Wi-Fi connection must reconnect, not wait forever.
                    stream.ReadTimeout = 10000;
                    stream.WriteTimeout = 1500;
                    connected = true;
                }

                using (StreamReader reader = new StreamReader(
                    current.GetStream(),
                    Encoding.UTF8
                ))
                {
                    string line;

                    while (
                        running &&
                        (line = reader.ReadLine()) != null
                    )
                    {
                        // The old foreground-hand PNG stream
                        // is intentionally ignored.
                        // Hand tracking JSON still works.
                        if (line.Contains(
                            "\"role1_hand_foreground\""
                        ))
                        {
                            continue;
                        }

                        if (line.Length > 1800000)
                            continue;

                        lock (queueLock)
                        {
                            if (messageQueue.Count >= 100)
                            {
                                messageQueue.Dequeue();
                            }

                            messageQueue.Enqueue(line);
                        }
                    }
                }
            }
            catch (SocketException)
            {
            }
            catch (AggregateException error) when (
                error.InnerException is SocketException ||
                error.InnerException is ObjectDisposedException)
            {
            }
            catch (IOException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception error)
            {
                if (running)
                {
                    UnityEngine.Debug.LogWarning(
                        "[SmartGeoAR] TCP: " +
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
                catch (Exception)
                {
                }
            }

            if (running)
                Thread.Sleep(650);
        }
    }

    // =====================================================
    // PROCESS INCOMING DATA
    // =====================================================

    private void Update()
    {
        if (connected != previousConnection)
        {
            previousConnection = connected;

            ConnectionChanged?.Invoke(connected);

            UnityEngine.Debug.Log(
                connected
                ? "[SmartGeoAR] Backend CONNECTED"
                : "[SmartGeoAR] Backend DISCONNECTED"
            );
        }

        int processed = 0;

        while (processed++ < 50)
        {
            string json;

            lock (queueLock)
            {
                if (messageQueue.Count == 0)
                    break;

                json = messageQueue.Dequeue();
            }

            try
            {
                Envelope envelope =
                    JsonUtility.FromJson<Envelope>(json);

                if (envelope == null)
                    continue;

                switch (envelope.source)
                {
                    case "role1_camera":

#if UNITY_ANDROID && !UNITY_EDITOR
                        // ARCore supplies the phone's camera background.
                        // Do not cover it with the laptop webcam stream.
                        break;
#else

                        if (!string.IsNullOrEmpty(envelope.jpeg))
                        {
                            ShowCameraFrame(envelope.jpeg);
                        }

                        break;
#endif

                    case "role1_hand_tracking":

                        HandPacket handPacket =
                            JsonUtility.FromJson<HandPacket>(
                                json
                            );

                        if (
                            handPacket != null &&
                            handPacket.hands != null
                        )
                        {
#if UNITY_ANDROID && !UNITY_EDITOR
                            if (handPacket.camera_source != "phone_rear")
                                break;
#endif
                            PacketReceived?.Invoke(handPacket);
                        }

                        break;

                    case "role2_geometry_reasoning":

                        GeometryResultReceived?.Invoke(json);
                        break;

                    case "smartgeoar_backend":

                        if (envelope.type == "hello")
                        {
                            UnityEngine.Debug.Log(
                                "[SmartGeoAR] Backend ready"
                            );
                        }
                        else if (
                            envelope.type == "camera_error"
                        )
                        {
                            UnityEngine.Debug.LogError(
                                "[SmartGeoAR] Camera: " +
                                envelope.message
                            );
                        }

                        break;
                }
            }
            catch (Exception error)
            {
                UnityEngine.Debug.LogWarning(
                    "[SmartGeoAR] Invalid packet: " +
                    error.Message
                );
            }
        }
    }

    // =====================================================
    // WEBCAM FRAME
    // =====================================================

    private void ShowCameraFrame(string encoded)
    {
        byte[] bytes;

        try
        {
            bytes = Convert.FromBase64String(encoded);
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

            cameraTexture.wrapMode = TextureWrapMode.Clamp;
            cameraTexture.filterMode = FilterMode.Bilinear;
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
            CreateCameraBackground();

        if (cameraMaterial == null)
            return;

        cameraMaterial.mainTexture = cameraTexture;

        if (cameraMaterial.HasProperty("_BaseMap"))
        {
            cameraMaterial.SetTexture(
                "_BaseMap",
                cameraTexture
            );
        }

        if (!receivedCameraFrame)
        {
            receivedCameraFrame = true;

            UnityEngine.Debug.Log(
                "[SmartGeoAR] Camera background ready: " +
                cameraTexture.width + "x" +
                cameraTexture.height
            );
        }
    }

    // =====================================================
    // BACKGROUND QUAD
    //
    // CAMERA VIDEO --> BEHIND 3D MODEL
    // MODEL 3D     --> IN FRONT OF CAMERA VIDEO
    // CANVAS UI    --> IN FRONT OF BOTH
    //
    // NO FOREGROUND-HAND CANVAS IS CREATED.
    // =====================================================

    private void CreateCameraBackground()
    {
        Camera targetCamera = Camera.main;

        if (targetCamera == null)
        {
            UnityEngine.Debug.LogError(
                "[SmartGeoAR] Main Camera missing."
            );
            return;
        }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        ARCameraBackground arBackground =
            targetCamera.GetComponent<ARCameraBackground>();

        if (arBackground != null)
            arBackground.enabled = false;
#endif

        Material template =
            Resources.Load<Material>(
                "SmartGeoAR_Unlit"
            );

        if (template != null)
        {
            cameraMaterial = new Material(template);
        }
        else
        {
            Shader shader = Shader.Find(
                "Universal Render Pipeline/Unlit"
            );

            if (shader == null)
                shader = Shader.Find("Unlit/Texture");

            if (shader == null)
            {
                UnityEngine.Debug.LogError(
                    "[SmartGeoAR] Camera shader missing."
                );
                return;
            }

            cameraMaterial = new Material(shader);
        }

        cameraMaterial.name =
            "SmartGeoAR_BackgroundMaterial";

        if (cameraMaterial.HasProperty("_BaseColor"))
        {
            cameraMaterial.SetColor(
                "_BaseColor",
                Color.white
            );
        }

        if (cameraMaterial.HasProperty("_Color"))
        {
            cameraMaterial.SetColor(
                "_Color",
                Color.white
            );
        }

        // Render both sides of the background quad.
        if (cameraMaterial.HasProperty("_Cull"))
        {
            cameraMaterial.SetFloat("_Cull", 0f);
        }

        cameraMaterial.renderQueue = 2000;

        cameraQuad = GameObject.CreatePrimitive(
            PrimitiveType.Quad
        );

        cameraQuad.name =
            "SmartGeoAR_CameraBehindModel";

        cameraQuad.layer = 0;

        Collider collider =
            cameraQuad.GetComponent<Collider>();

        if (collider != null)
            Destroy(collider);

        cameraQuad.transform.SetParent(
            targetCamera.transform,
            false
        );

        float distance = Mathf.Clamp(
            2.5f,
            targetCamera.nearClipPlane + 0.1f,
            targetCamera.farClipPlane * 0.8f
        );

        float height;

        if (targetCamera.orthographic)
        {
            height = targetCamera.orthographicSize * 2f;
        }
        else
        {
            height =
                2f * distance *
                Mathf.Tan(
                    targetCamera.fieldOfView *
                    0.5f *
                    Mathf.Deg2Rad
                );
        }

        float width =
            height * targetCamera.aspect;

        cameraQuad.transform.localPosition =
            Vector3.forward * distance;

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

        renderer.sharedMaterial = cameraMaterial;

        renderer.shadowCastingMode =
            ShadowCastingMode.Off;

        renderer.receiveShadows = false;
    }

    // =====================================================
    // SEND GEOMETRY JSON TO ROLE 2
    // =====================================================

    public static bool SendGeometryJson(string json)
    {
        return instance != null &&
               instance.SendLine(json);
    }

    public static Task<bool> SendPhoneCameraJsonAsync(string json)
    {
        HandDataReceiver receiver = instance;
        if (receiver == null)
            return Task.FromResult(false);
        return Task.Run(() => receiver.SendLine(json));
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
            if (!connected || stream == null)
                return false;

            try
            {
                stream.Write(
                    bytes,
                    0,
                    bytes.Length
                );

                return true;
            }
            catch (Exception)
            {
                connected = false;

                try
                {
                    tcpClient?.Close();
                }
                catch (Exception)
                {
                }

                return false;
            }
        }
    }

    // =====================================================
    // CLEANUP
    // =====================================================

    private void StopBackend()
    {
        running = false;

        lock (socketLock)
        {
            connected = false;

            try
            {
                tcpClient?.Close();
            }
            catch (Exception)
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

        try
        {
            if (
                backendProcess != null &&
                !backendProcess.HasExited
            )
            {
                backendProcess.Kill();
            }
        }
        catch (Exception)
        {
        }

        try
        {
            backendProcess?.Dispose();
        }
        catch (Exception)
        {
        }

        backendProcess = null;
    }

    private void OnDisable()
    {
        if (instance == this)
            StopBackend();
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            StopBackend();
            instance = null;
        }

        if (cameraQuad != null)
            Destroy(cameraQuad);

        if (cameraMaterial != null)
            Destroy(cameraMaterial);

        if (cameraTexture != null)
            Destroy(cameraTexture);
    }

    private void OnApplicationQuit()
    {
        if (instance == this)
            StopBackend();
    }
}
