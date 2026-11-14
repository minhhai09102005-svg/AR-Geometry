using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;

public class HandOcclusionProxy : MonoBehaviour
{
    private const float CaptureAge = 0.25f;
    private const float MissingGrace = 0.2f;

    private class HandMesh
    {
        public string hand;
        public GameObject obj;
        public Mesh mesh;
        public float updatedAt;
        public readonly MeshBuilder builder = new MeshBuilder();
    }

    private readonly HandMesh[] hands = new HandMesh[2];
    private Material material;
    private bool activeReported;
    private int accepted;
    private int rejectedAge;
    private int rejectedPose;
    private float latestAge;
    private float nextReport;

    public static void Ensure(ARCameraManager manager)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (manager != null && manager.GetComponent<HandOcclusionProxy>() == null)
            manager.gameObject.AddComponent<HandOcclusionProxy>();
#endif
    }

    private void OnEnable()
    {
        Shader shader = Resources.Load<Shader>("SmartGeoAR_HandDepth");
        if (shader == null)
        {
            Debug.LogWarning("[SmartGeoAR] Hand depth shader is missing.");
            enabled = false;
            return;
        }

        material = new Material(shader);
#if UNITY_ANDROID && !UNITY_EDITOR
        using (AndroidJavaClass player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
        using (AndroidJavaObject intent = activity.Call<AndroidJavaObject>("getIntent"))
        {
            if (intent.Call<bool>("getBooleanExtra", "smartgeoar_hand_mask_debug", false))
                material.SetFloat("_DebugColorMask", 15f);
        }
#endif
        for (int i = 0; i < hands.Length; i++)
        {
            HandMesh hand = new HandMesh();
            hand.obj = new GameObject("SmartGeoAR_HandDepth_" + i);
            // A scene root retains the capture pose while the phone moves.
            // Parenting this to the camera/workspace would move old pixels.
            hand.mesh = new Mesh { name = "SmartGeoAR hand depth" };
            hand.mesh.MarkDynamic();
            hand.obj.AddComponent<MeshFilter>().sharedMesh = hand.mesh;
            MeshRenderer renderer = hand.obj.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            hand.obj.SetActive(false);
            hands[i] = hand;
        }

        HandDataReceiver.PacketReceived += ReceiveHands;
        HandDataReceiver.ConnectionChanged += OnConnectionChanged;
    }

    private void ReceiveHands(HandDataReceiver.HandPacket packet)
    {
        if (packet == null || packet.camera_source != "phone_rear" || packet.hands == null)
            return;

        foreach (HandDataReceiver.HandData data in packet.hands)
        {
            if (data == null || string.IsNullOrEmpty(data.hand) ||
                data.spatial == null || !data.spatial.valid ||
                data.spatial.coordinate_system != "phone_camera_m" ||
                !ValidLandmarks(data.spatial.landmarks))
            {
                rejectedPose++;
                continue;
            }

            latestAge = PhoneCameraSender.CaptureAgeSeconds(data.spatial.camera_frame_id);
            if (!PhoneCameraSender.TryGetCapturePose(data.spatial.camera_frame_id,
                out Matrix4x4 pose, CaptureAge))
            {
                rejectedAge++;
                continue;
            }

            HandMesh hand = FindHand(data.hand);
            if (hand == null)
                continue;

            hand.hand = data.hand;
            hand.builder.Fill(hand.mesh, data.spatial.landmarks);
            hand.obj.transform.SetPositionAndRotation(pose.GetColumn(3), pose.rotation);
            hand.updatedAt = Time.unscaledTime;
            hand.obj.SetActive(true);
            accepted++;
            if (!activeReported)
            {
                activeReported = true;
                SpatialWorkspace.LogInteraction("Hand depth proxy active: 21 camera landmarks");
            }
        }
    }

    private HandMesh FindHand(string name)
    {
        foreach (HandMesh hand in hands)
        {
            if (hand.hand == name)
                return hand;
        }
        foreach (HandMesh hand in hands)
        {
            if (!hand.obj.activeSelf || Time.unscaledTime - hand.updatedAt > MissingGrace)
                return hand;
        }
        return null;
    }

    private void LateUpdate()
    {
        foreach (HandMesh hand in hands)
        {
            if (hand != null && Time.unscaledTime - hand.updatedAt > MissingGrace)
            {
                hand.obj.SetActive(false);
                hand.hand = null;
            }
        }

        if (Time.unscaledTime >= nextReport)
        {
            nextReport = Time.unscaledTime + 2f;
            int visible = 0;
            foreach (HandMesh hand in hands)
                if (hand != null && hand.obj.activeSelf) visible++;
            SpatialWorkspace.LogInteraction("Hand mask: active=" + visible +
                "; accepted=" + accepted + "; rejectedAge=" + rejectedAge +
                "; rejectedPose=" + rejectedPose + "; age=" + latestAge.ToString("F3") +
                "; shader=" + material.shader.isSupported + "; api=" + SystemInfo.graphicsDeviceType);
            accepted = rejectedAge = rejectedPose = 0;
        }
    }

    private void OnConnectionChanged(bool connected)
    {
        if (connected)
            return;

        foreach (HandMesh hand in hands)
        {
            hand.obj.SetActive(false);
            hand.hand = null;
        }
    }

    // The returned mesh uses the camera pose at image capture: x right,
    // y up, z forward, in metres. Callers own and must destroy this mesh.
    public static Mesh BuildCameraMesh(HandDataReceiver.SpatialPoint[] landmarks)
    {
        if (!ValidLandmarks(landmarks))
            return null;

        Mesh mesh = new Mesh { name = "SmartGeoAR hand depth" };
        new MeshBuilder().Fill(mesh, landmarks);
        return mesh;
    }

    private static bool ValidLandmarks(HandDataReceiver.SpatialPoint[] points)
    {
        if (points == null || points.Length != 21)
            return false;

        foreach (HandDataReceiver.SpatialPoint point in points)
        {
            if (point == null || !Finite(point.x) || !Finite(point.y) ||
                !Finite(point.z) || point.z < 0.12f || point.z > 2.5f)
                return false;
        }
        return true;
    }

    private static bool Finite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private class MeshBuilder
    {
        private static readonly int[] Palm = { 0, 1, 5, 9, 13, 17 };
        private static readonly int[] Fingers = { 1, 5, 9, 13, 17 };
        private readonly List<Vector3> vertices = new List<Vector3>(400);
        private readonly List<int> triangles = new List<int>(900);
        private readonly Vector3[] points = new Vector3[21];

        public void Fill(Mesh mesh, HandDataReceiver.SpatialPoint[] landmarks)
        {
            vertices.Clear();
            triangles.Clear();
            for (int i = 0; i < points.Length; i++)
            {
                HandDataReceiver.SpatialPoint point = landmarks[i];
                points[i] = new Vector3(point.x, point.y, point.z);
            }

            float palmWidth = Mathf.Clamp(Vector3.Distance(points[5], points[17]), 0.035f, 0.12f);
            float skin = Mathf.Clamp(palmWidth * 0.07f, 0.003f, 0.006f);
            for (int i = 0; i < points.Length; i++)
            {
                // Move toward the camera along the measured image ray,
                // preserving alignment with the real hand in the image.
                points[i] *= (points[i].z - skin) / points[i].z;
            }

            AddPalm();
            foreach (int first in Fingers)
            {
                for (int joint = 0; joint < 4; joint++)
                {
                    float radius = palmWidth * Mathf.Lerp(0.11f, 0.075f, joint / 3f);
                    AddDisc(points[first + joint], radius);
                    if (joint < 3)
                    {
                        float nextRadius = palmWidth * Mathf.Lerp(0.11f, 0.075f, (joint + 1) / 3f);
                        AddSegment(points[first + joint], points[first + joint + 1], radius, nextRadius);
                    }
                }
            }

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
        }

        private void AddPalm()
        {
            Vector3 center = Vector3.zero;
            foreach (int index in Palm)
                center += points[index];
            center /= Palm.Length;

            int start = vertices.Count;
            vertices.Add(center);
            foreach (int index in Palm)
            {
                Vector3 point = points[index];
                point.x = Mathf.Lerp(center.x, point.x, 0.94f);
                point.y = Mathf.Lerp(center.y, point.y, 0.94f);
                vertices.Add(point);
            }
            for (int i = 0; i < Palm.Length; i++)
                Triangle(start, start + 1 + i, start + 1 + (i + 1) % Palm.Length);
        }

        private void AddDisc(Vector3 point, float radius)
        {
            const int sides = 8;
            int start = vertices.Count;
            vertices.Add(point);
            for (int i = 0; i < sides; i++)
            {
                float angle = i * Mathf.PI * 2f / sides;
                vertices.Add(point + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius);
            }
            for (int i = 0; i < sides; i++)
                Triangle(start, start + 1 + i, start + 1 + (i + 1) % sides);
        }

        private void AddSegment(Vector3 from, Vector3 to, float fromRadius, float toRadius)
        {
            Vector3 normal = new Vector3(-(to.y - from.y), to.x - from.x, 0f);
            if (normal.sqrMagnitude < 0.00000001f)
                return;
            normal.Normalize();
            int start = vertices.Count;
            vertices.Add(from + normal * fromRadius);
            vertices.Add(from - normal * fromRadius);
            vertices.Add(to + normal * toRadius);
            vertices.Add(to - normal * toRadius);
            Triangle(start, start + 1, start + 2);
            Triangle(start + 2, start + 1, start + 3);
        }

        private void Triangle(int a, int b, int c)
        {
            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);
        }
    }

    private void OnDisable()
    {
        HandDataReceiver.PacketReceived -= ReceiveHands;
        HandDataReceiver.ConnectionChanged -= OnConnectionChanged;
        for (int i = 0; i < hands.Length; i++)
        {
            if (hands[i] == null)
                continue;
            Destroy(hands[i].obj);
            Destroy(hands[i].mesh);
            hands[i] = null;
        }
        if (material != null)
            Destroy(material);
    }
}
