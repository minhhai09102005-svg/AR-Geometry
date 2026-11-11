using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Object = UnityEngine.Object;

// Temporary device-verification harness. Remove this file after the APK is built.
public static class SmartGeoARHeadsetBuild
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int checks;
    private static readonly List<string> evidence = new List<string>();
    private static string EvidenceDirectory => Path.Combine(
        Directory.GetParent(Application.dataPath).FullName, "Logs", "Headset-2026-11-11", "OpticsFit");

    public static void Verify()
    {
        checks = 0;
        evidence.Clear();
        Directory.CreateDirectory(EvidenceDirectory);
        try
        {
            VerifyMesh();
            VerifyGpuDepth();
            VerifyShapeDrop();
            VerifyScan();
            VerifySceneDepth();
            SmartGeoARHeadsetVerification.Run(Check, EvidenceDirectory);
            Debug.Log("[SmartGeoAR checks] PASS: " + checks + " checks; GPU=" + SystemInfo.graphicsDeviceType);
            evidence.Add("PASS " + checks + "; GPU=" + SystemInfo.graphicsDeviceType);
        }
        catch (Exception error)
        {
            evidence.Add("FAIL " + error);
            throw;
        }
        finally
        {
            File.WriteAllLines(Path.Combine(EvidenceDirectory, "unity-headset-checks.txt"), evidence);
        }
    }

    public static void Build()
    {
        Verify();
        Check(EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android,
            "Build target is Android");
        string[] scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled)
            .Select(scene => scene.path).ToArray();
        Check(scenes.Length == 2 && scenes.Any(path => path.EndsWith("MainMenu.unity")) &&
            scenes.Any(path => path.EndsWith("ARMain.unity")), "Existing menu and AR scenes retained");
        string apk = Path.Combine(Directory.GetParent(Application.dataPath).FullName,
            "Builds", "Android", "SmartGeoAR-SpatialDeviceTest.apk");
        Directory.CreateDirectory(Path.GetDirectoryName(apk));
        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = apk,
            target = BuildTarget.Android,
            options = BuildOptions.Development
        });
        if (report.summary.result != BuildResult.Succeeded)
            throw new BuildFailedException("Android build failed: " + report.summary.result);
        Debug.Log("[SmartGeoAR checks] APK BUILT: " + apk + "; bytes=" + report.summary.totalSize);
    }

    private static void Check(bool condition, string description)
    {
        if (!condition)
            throw new BuildFailedException(description);
        checks++;
        evidence.Add("PASS " + description);
        Debug.Log("[SmartGeoAR checks] PASS " + description);
    }

    private static HandDataReceiver.SpatialPoint[] Hand()
    {
        Vector2[] xy =
        {
            new Vector2(0f, -.08f),
            new Vector2(-.055f, -.035f), new Vector2(-.080f, -.005f),
            new Vector2(-.095f, .020f), new Vector2(-.105f, .040f),
            new Vector2(-.040f, .030f), new Vector2(-.040f, .080f),
            new Vector2(-.040f, .120f), new Vector2(-.040f, .160f),
            new Vector2(0f, .040f), new Vector2(0f, .095f),
            new Vector2(0f, .140f), new Vector2(0f, .180f),
            new Vector2(.035f, .035f), new Vector2(.035f, .085f),
            new Vector2(.035f, .125f), new Vector2(.035f, .160f),
            new Vector2(.065f, .015f), new Vector2(.065f, .055f),
            new Vector2(.065f, .090f), new Vector2(.065f, .120f)
        };
        return xy.Select(point => new HandDataReceiver.SpatialPoint
            { x = point.x, y = point.y, z = .4f }).ToArray();
    }

    private static bool Covered(Mesh mesh, Vector2 imageRay)
    {
        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 av = vertices[triangles[i]];
            Vector3 bv = vertices[triangles[i + 1]];
            Vector3 cv = vertices[triangles[i + 2]];
            Vector2 a = new Vector2(av.x, av.y) / av.z;
            Vector2 b = new Vector2(bv.x, bv.y) / bv.z;
            Vector2 c = new Vector2(cv.x, cv.y) / cv.z;
            float s1 = Cross(b - a, imageRay - a);
            float s2 = Cross(c - b, imageRay - b);
            float s3 = Cross(a - c, imageRay - c);
            if ((s1 > 0f && s2 > 0f && s3 > 0f) || (s1 < 0f && s2 < 0f && s3 < 0f))
                return true;
        }
        return false;
    }

    private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

    private static void VerifyMesh()
    {
        Check(HandOcclusionProxy.BuildCameraMesh(null) == null, "No hand produces no occluder");
        Check(HandOcclusionProxy.BuildCameraMesh(new HandDataReceiver.SpatialPoint[20]) == null,
            "Incomplete landmark arrays rejected");
        var invalid = Hand();
        invalid[6] = null;
        Check(HandOcclusionProxy.BuildCameraMesh(invalid) == null, "Null joints rejected");
        invalid = Hand();
        invalid[8].x = float.NaN;
        Check(HandOcclusionProxy.BuildCameraMesh(invalid) == null, "Nonfinite joints rejected");
        invalid = Hand();
        invalid[8].z = .05f;
        Check(HandOcclusionProxy.BuildCameraMesh(invalid) == null, "Invalid near-camera pose rejected");
        invalid = Hand();
        invalid[8].z = 3f;
        Check(HandOcclusionProxy.BuildCameraMesh(invalid) == null, "Out-of-range far pose rejected");
        Mesh mesh = HandOcclusionProxy.BuildCameraMesh(Hand());
        try
        {
            Check(mesh != null && mesh.vertexCount > 30 && mesh.vertexCount < 2000 &&
                mesh.triangles.Length > 60, "Compact renderable hand mesh created");
            Check(mesh.vertices.All(point => Finite(point.x) && Finite(point.y) &&
                Finite(point.z) && point.z < .4f && point.z > .388f),
                "Skin is finite and a few millimetres before estimated joints");
            Check(Covered(mesh, new Vector2(0f, -.02f) / .4f), "Palm interior occludes");
            Check(Covered(mesh, new Vector2(-.04f, .11f) / .4f), "Index finger interior occludes");
            Check(!Covered(mesh, new Vector2(-.02f, .12f) / .4f), "Gap between index and middle remains open");
            Check(!Covered(mesh, new Vector2(.11f, -.05f) / .4f), "Outside hand remains unmasked");
        }
        finally
        {
            Object.DestroyImmediate(mesh);
        }
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static void VerifySceneDepth()
    {
        var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/ARMain.unity");
        try
        {
            AROcclusionManager[] managers = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<AROcclusionManager>(true)).ToArray();
            Check(managers.Length == 1, "AR scene contains exactly one serialized occlusion manager");
            Check(!managers[0].enabled && managers[0].requestedEnvironmentDepthMode == EnvironmentDepthMode.Disabled &&
                managers[0].requestedOcclusionPreferenceMode == OcclusionPreferenceMode.NoOcclusion,
                "AR scene disables manager and unreliable environment depth before camera startup");
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static Mesh Quad()
    {
        Mesh mesh = new Mesh { name = "Verification opaque board" };
        mesh.vertices = new[] { new Vector3(-.13f, -.16f, 0f), new Vector3(.13f, -.16f, 0f),
            new Vector3(.13f, .20f, 0f), new Vector3(-.13f, .20f, 0f) };
        mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void VerifyGpuDepth()
    {
        Check(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null,
            "Real graphics device available for depth rendering test");
        Shader shader = Resources.Load<Shader>("SmartGeoAR_HandDepth");
        Check(shader != null && shader.isSupported, "Packaged hand depth shader supported");
        Material handMaterial = new Material(shader);
        Vector4 originalHandEyeOffset = Shader.GetGlobalVector("_SmartGeoAR_HandEyeOffset");
        Shader.SetGlobalVector("_SmartGeoAR_HandEyeOffset", Vector4.zero);
        Material board = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        board.SetColor("_BaseColor", new Color(.1f, .3f, .8f, 1f));
        board.SetFloat("_Cull", 0f);
        board.SetFloat("_ZWrite", 1f);
        RenderTexture target = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32);
        target.Create();
        Texture2D pixels = new Texture2D(256, 256, TextureFormat.RGB24, false);
        Mesh hand = HandOcclusionProxy.BuildCameraMesh(Hand());
        Mesh quad = Quad();
        GameObject renderRoot = new GameObject("SmartGeoAR GPU verification");
        renderRoot.hideFlags = HideFlags.HideAndDontSave;
        GameObject cameraObject = new GameObject("Verification camera", typeof(Camera));
        cameraObject.transform.SetParent(renderRoot.transform, false);
        Camera camera = cameraObject.GetComponent<Camera>();
        camera.enabled = false;
        camera.transform.position = new Vector3(0f, .02f, 0f);
        camera.orthographic = true;
        camera.orthographicSize = .18f;
        camera.aspect = .26f / .36f;
        camera.nearClipPlane = .01f;
        camera.farClipPlane = 2f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.25f, .25f, .25f, 1f);
        camera.cullingMask = 1 << 29;
        camera.allowHDR = false;
        camera.allowMSAA = false;
        UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = false;
        data.renderShadows = false;
        GameObject handObject = new GameObject("Actual hand depth renderer", typeof(MeshFilter), typeof(MeshRenderer));
        handObject.transform.SetParent(renderRoot.transform, false);
        handObject.layer = 29;
        handObject.GetComponent<MeshFilter>().sharedMesh = hand;
        handObject.GetComponent<MeshRenderer>().sharedMaterial = handMaterial;
        GameObject boardObject = new GameObject("Actual opaque board renderer", typeof(MeshFilter), typeof(MeshRenderer));
        boardObject.transform.SetParent(renderRoot.transform, false);
        boardObject.layer = 29;
        boardObject.GetComponent<MeshFilter>().sharedMesh = quad;
        boardObject.GetComponent<MeshRenderer>().sharedMaterial = board;
        RenderTexture previous = RenderTexture.active;
        try
        {
            Check(handMaterial.renderQueue < board.renderQueue,
                "Hand depth renders before opaque geometry");
            Check(handMaterial.FindPass("HandDepth") >= 0 || handMaterial.passCount >= 2,
                "Depth material includes forward and depth-only passes");
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            Check(RenderPipeline.SupportsRenderRequest(camera, request),
                "Project URP supports real offscreen camera render requests");
            Action<string, bool, float> render = (name, mask, boardDepth) =>
            {
                handObject.SetActive(mask);
                boardObject.transform.position = new Vector3(0f, 0f, boardDepth);
                RenderPipeline.SubmitRenderRequest(camera, request);
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
                pixels.Apply();
                File.WriteAllBytes(Path.Combine(EvidenceDirectory, name + ".png"), pixels.EncodeToPNG());
            };
            Func<Vector2, Color> sample = point =>
            {
                Vector3 viewport = camera.WorldToViewportPoint(new Vector3(point.x, point.y, .4f));
                int x = Mathf.Clamp(Mathf.FloorToInt(viewport.x * 256), 0, 255);
                int y = Mathf.Clamp(Mathf.FloorToInt(viewport.y * 256), 0, 255);
                return pixels.GetPixel(x, y);
            };
            Func<Color, bool> blue = color => color.b > color.r + .15f;
            Func<Color, bool> background = color => Mathf.Abs(color.r - color.b) < .025f &&
                Mathf.Abs(color.r - color.g) < .025f && color.r > .1f;
            Vector2 palm = new Vector2(0f, -.02f);
            Vector2 finger = new Vector2(-.04f, .11f);
            Vector2 gap = new Vector2(-.02f, .12f);
            render("gpu-baseline-board", false, .7f);
            Check(blue(sample(palm)) && blue(sample(finger)), "GPU baseline opaque board covers camera background");
            render("gpu-hand-before-board", true, .7f);
            Check(background(sample(palm)) && background(sample(finger)),
                "GPU nearer palm and finger preserve camera background over board");
            Check(blue(sample(gap)) && blue(sample(new Vector2(.11f, -.05f))),
                "GPU finger gap and outside silhouette keep board visible");
            render("gpu-board-before-hand", true, .2f);
            Check(blue(sample(palm)) && blue(sample(finger)),
                "GPU board nearer than hand remains visible");
            Check(handMaterial.GetFloat("_DebugColorMask") == 0f,
                "Hand alignment debug colour is disabled by default");
            handMaterial.SetFloat("_DebugColorMask",15f);
            render("gpu-hand-debug-cyan", true, .7f);
            Func<Color,bool> cyan = color => color.r < .05f && color.g > .9f && color.b > .9f;
            Check(cyan(sample(palm)) && cyan(sample(finger)) && blue(sample(gap)),
                "Opt-in debug mode draws cyan hand silhouette without filling finger gaps");
            Shader.SetGlobalVector("_SmartGeoAR_HandEyeOffset",new Vector4(.05f,0f,0f,0f));
            render("gpu-hand-eye-offset", true, .7f);
            Check(blue(sample(finger)) && cyan(sample(finger + new Vector2(.05f,0f))),
                "Per-eye hand mask offset follows camera translation for the mono background");
        }
        finally
        {
            RenderTexture.active = previous;
            Shader.SetGlobalVector("_SmartGeoAR_HandEyeOffset",originalHandEyeOffset);
            Object.DestroyImmediate(renderRoot);
            target.Release();
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(pixels);
            Object.DestroyImmediate(handMaterial);
            Object.DestroyImmediate(board);
            Object.DestroyImmediate(hand);
            Object.DestroyImmediate(quad);
        }
    }

    private sealed class Fixture : IDisposable
    {
        public readonly GameObject root = new GameObject("SmartGeoAR verification");
        public readonly Camera camera;
        public readonly Canvas canvas;
        public Fixture()
        {
            root.hideFlags = HideFlags.HideAndDontSave;
            var cameraObject = Child("View");
            camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.nearClipPlane = .01f;
            camera.farClipPlane = 10f;
            camera.aspect = 1f;
            canvas = Child("Canvas", typeof(RectTransform)).AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = camera;
        }
        public GameObject Child(string name, params Type[] types)
        {
            var obj = new GameObject(name, types);
            obj.transform.SetParent(root.transform, false);
            return obj;
        }
        public T Controller<T>() where T : Component
        {
            GameObject obj = Child(typeof(T).Name);
            obj.SetActive(false);
            return obj.AddComponent<T>();
        }
        public TMP_Text Text()
        {
            GameObject obj = Child("Text", typeof(RectTransform));
            obj.SetActive(false);
            return obj.AddComponent<TextMeshProUGUI>();
        }
        public GrabbableObject Body(Vector3 position)
        {
            GameObject obj = Child("Body", typeof(RectTransform));
            obj.transform.position = position;
            BoxCollider collider = obj.AddComponent<BoxCollider>();
            collider.size = new Vector3(.035f, .035f, .02f);
            var body = obj.AddComponent<GrabbableObject>();
            body.RememberHome();
            return body;
        }
        public void Dispose()
        {
            foreach (ScanScreenController scan in root.GetComponentsInChildren<ScanScreenController>(true))
                Set(scan, "modelRoot", null);
            Object.DestroyImmediate(root);
        }
    }

    private static void Set(object target, string field, object value) =>
        target.GetType().GetField(field, Private).SetValue(target, value);
    private static object Get(object target, string field) => target.GetType().GetField(field, Private).GetValue(target);
    private static object Call(object target, string method, params object[] args)
    {
        try { return target.GetType().GetMethod(method, Private).Invoke(target, args); }
        catch (TargetInvocationException error) { throw error.InnerException ?? error; }
    }
    private static SpatialHandInteractor.HandFrame Frame(string name, string gesture, Vector3 point) =>
        new SpatialHandInteractor.HandFrame { hand = name, gesture = gesture, position = point,
            indexTip = point, hasDepth = true };

    private static void VerifyShapeDrop()
    {
        using (var fixture = new Fixture())
        {
            ShapeDropFeature drop = fixture.Controller<ShapeDropFeature>();
            Set(drop, "canvas", fixture.canvas);
            Set(drop, "gameScreen", true);
            Set(drop, "sessionId", "verification");
            TMP_Text feedback = fixture.Text();
            Set(drop, "feedbackText", feedback);
            Array pieces = (Array)Get(drop, "pieces");
            Array holes = (Array)Get(drop, "holes");
            Type pieceType = typeof(ShapeDropFeature).GetNestedType("Piece", BindingFlags.NonPublic);
            for (int i = 0; i < 4; i++)
            {
                GrabbableObject body = fixture.Body(new Vector3(i * .1f, 0f, .5f));
                object piece = Activator.CreateInstance(pieceType, true);
                pieceType.GetField("index").SetValue(piece, i);
                pieceType.GetField("rect").SetValue(piece, body.transform as RectTransform);
                pieceType.GetField("body").SetValue(piece, body);
                pieces.SetValue(piece, i);
                RectTransform hole = fixture.Child("Hole", typeof(RectTransform)).transform as RectTransform;
                hole.position = new Vector3(i * .1f, 0f, .5f);
                hole.sizeDelta = Vector2.one;
                holes.SetValue(hole, i);
            }
            Physics.SyncTransforms();
            Call(drop, "OnHandPacket", Frame("A", "PINCH_START", new Vector3(1f, .3f, .5f)));
            Check(Get(drop, "activeHand") == null && (int)Get(drop, "dragSource") == 0,
                "ShapeDrop unsuccessful pinch does not reserve hand ownership");
            Call(drop, "OnHandPacket", Frame("B", "PINCH_HOLD", new Vector3(0f, 0f, .5f)));
            Check((string)Get(drop, "activeHand") == "B" && (int)Get(drop, "dragSource") == 2,
                "ShapeDrop HOLD can acquire after missed START or another failed hand");
            Call(drop, "OnHandPacket", Frame("B", "OPEN", new Vector3(0f, 0f, .2f)));
            Check(Get(drop, "activeHand") == null && (int)Get(drop, "dragSource") == 0,
                "ShapeDrop OPEN recovers missed END and clears ownership");
            Check((int)Get(drop, "requestCounter") == 0 && feedback.text.Contains("sát"),
                "ShapeDrop OPEN uses current depth and rejects release away from board");
            Physics.SyncTransforms();
            Call(drop, "OnHandPacket", Frame("A", "PINCH_START", new Vector3(0f, 0f, .5f)));
            Call(drop, "OnHandPacket", Frame("A", "PINCH_END", new Vector3(0f, 0f, .2f)));
            Check((int)Get(drop, "requestCounter") == 0 && Get(drop, "activeHand") == null,
                "ShapeDrop END also tests current release depth rather than previous HOLD");
        }
    }

    private static void VerifyScan()
    {
        using (var fixture = new Fixture())
        {
            ScanScreenController scan = fixture.Controller<ScanScreenController>();
            Set(scan, "running", true);
            Set(scan, "canvas", fixture.canvas);
            Set(scan, "viewCamera", fixture.camera);
            GameObject model = fixture.Child("Model");
            Set(scan, "modelRoot", model);
            Set(scan, "expectedEdges", new[] { new[] { "A", "B" } });
            var positions = (IDictionary)Get(scan, "positions");
            positions["A"] = new Vector3(0f, -.02f, .7f);
            positions["B"] = new Vector3(0f, .02f, .7f);
            ((IDictionary)Get(scan, "rodTypeByEdge"))["A-B"] = 1; // Force wrong type to avoid network/install dependencies.
            TMP_Text guide = fixture.Text();
            Set(scan, "guideText", guide);
            Type rodType = typeof(ScanScreenController).GetNestedType("RodGroup", BindingFlags.NonPublic);
            object rod = Activator.CreateInstance(rodType, true);
            GrabbableObject body = fixture.Body(new Vector3(0f, 0f, .5f));
            rodType.GetField("body").SetValue(rod, body);
            rodType.GetField("slot").SetValue(rod, body.transform as RectTransform);
            rodType.GetField("remaining").SetValue(rod, 1);
            rodType.GetField("length").SetValue(rod, .1f);
            rodType.GetField("label").SetValue(rod, "test rod");
            ((IList)Get(scan, "rods")).Add(rod);
            Physics.SyncTransforms();
            Call(scan, "ReceiveHands", Frame("A", "PINCH_START", new Vector3(1f, .3f, .5f)));
            Check(string.IsNullOrEmpty((string)Get(scan, "activeHandName")),
                "Scan unsuccessful pinch does not reserve hand ownership");
            Call(scan, "ReceiveHands", Frame("B", "PINCH_HOLD", new Vector3(0f, 0f, .5f)));
            Check((string)Get(scan, "activeHandName") == "B" && Get(scan, "source").ToString() == "Hand",
                "Scan HOLD acquires a rod without receiving START");
            Call(scan, "ReceiveHands", Frame("B", "OPEN", new Vector3(0f, 0f, .7f)));
            Check(guide.text.StartsWith("Sai ") && (string)Get(scan, "activeHandName") == null &&
                Get(scan, "source").ToString() == "None",
                "Scan OPEN checks current release pose at edge and clears ownership");
            Physics.SyncTransforms();
            Call(scan, "ReceiveHands", Frame("A", "PINCH_HOLD", new Vector3(0f, 0f, .5f)));
            Call(scan, "ReceiveHands", Frame("A", "PINCH_END", new Vector3(0f, 0f, .7f)));
            Check(guide.text.StartsWith("Sai ") && Get(scan, "source").ToString() == "None",
                "Scan END also evaluates current release pose");
            // The production OnDestroy uses deferred Destroy; remove its test-only model reference first.
            Set(scan, "modelRoot", null);
        }
    }
}

internal static class SmartGeoARHeadsetVerification
{
    public static void Run(Action<bool,string> check, string evidenceDirectory)
    {
        Matrix4x4 projection = PhoneHeadsetDisplay.EyeProjection(60f, 1f, .05f, 10f);
        float focal = 1f / Mathf.Tan(30f * Mathf.Deg2Rad);
        check(Mathf.Abs(projection.m00 - focal) < .0001f && Mathf.Abs(projection.m11 - focal) < .0001f &&
            Mathf.Abs(projection.m02) < .0001f && Mathf.Abs(projection.m12) < .0001f,
            "Headset eye projection is symmetric with configured FOV");
        Matrix4x4 wide = PhoneHeadsetDisplay.EyeProjection(60f, 1.5f, .05f, 10f);
        check(Mathf.Abs(wide.m00 * 1.5f - focal) < .0001f && Mathf.Abs(wide.m11 - focal) < .0001f,
            "Headset eye projection preserves vertical FOV across target aspect ratios");
        check(Mathf.Abs(projection.MultiplyPoint(new Vector3(0f,0f,-.05f)).z+1f)<.0001f &&
            Mathf.Abs(projection.MultiplyPoint(new Vector3(0f,0f,-10f)).z-1f)<.0001f,
            "Headset projection retains valid near and far clipping planes");
        Vector3 leftOffset = PhoneHeadsetDisplay.EyeLocalOffset(false);
        Vector3 rightOffset = PhoneHeadsetDisplay.EyeLocalOffset(true);
        check(Vector3.Distance(leftOffset, new Vector3(-.03f,0f,0f)) < .0001f &&
            Vector3.Distance(rightOffset, new Vector3(.03f,0f,0f)) < .0001f,
            "Headset eye origins are separated by 60 mm without toe-in");
        check(Vector2.Distance(PhoneHeadsetDisplay.UndistortEyeUv(new Vector2(.5f,.5f), new Vector2(.22f,.24f)),
            new Vector2(.5f,.5f)) < .0001f,
            "Lens prewarp leaves each eye centre fixed");
        check(Vector2.Distance(PhoneHeadsetDisplay.UndistortEyeUv(new Vector2(.2f,.7f), Vector2.zero),
            new Vector2(.2f,.7f)) < .0001f,
            "Zero lens distortion leaves eye UV coordinates unchanged");
        Vector2 corner = PhoneHeadsetDisplay.UndistortEyeUv(Vector2.one, new Vector2(.22f,.24f));
        check(corner.x > 1f && corner.y > 1f, "Lens prewarp maps extreme corners outside valid eye image");
        check(Mathf.Abs(PhoneHeadsetDisplay.LensCenterShift(2712f,0f)-(.5f-60f/155f))<.0001f &&
            Mathf.Abs(PhoneHeadsetDisplay.LensCenterShift(2400f,508f))<.0001f,
            "Lens centres use bounded physical display width with a defined DPI fallback");
        Matrix4x4 phoneEyeProjection = PhoneHeadsetDisplay.EyeProjection(60f, 1356f/1220f, .05f, 10f);
        bool fits = true;
        foreach(float distance in new[]{.35f,.75f,1f})
        foreach(bool right in new[]{false,true})
        foreach(float x in new[]{-.32f,.32f})
        foreach(float y in new[]{-.18f,.18f})
        {
            Vector3 local = new Vector3(x*distance/.75f,y*distance/.75f,-distance);
            local.x -= PhoneHeadsetDisplay.EyeLocalOffset(right).x;
            Vector3 clip = phoneEyeProjection.MultiplyPoint(local);
            fits &= Mathf.Abs(clip.x)<1f && Mathf.Abs(clip.y)<1f;
        }
        check(fits,"Shared 1600x900 mode Canvas fits both phone eye frustums over automatic workspace distances");
        VerifyStereoGpu(check, evidenceDirectory, projection);
        VerifyCompositeGpu(check, evidenceDirectory);
        VerifyLifecycle(check);
    }

    private static Texture2D Read(RenderTexture target)
    {
        RenderTexture previous = RenderTexture.active;
        try
        {
            RenderTexture.active = target;
            Texture2D pixels = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false, true);
            pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0);
            pixels.Apply();
            return pixels;
        }
        finally { RenderTexture.active = previous; }
    }

    private static Vector2 RedCentroid(Texture2D image)
    {
        Color[] colors = image.GetPixels();
        double x = 0, y = 0;
        int count = 0;
        for(int i=0;i<colors.Length;i++)
        {
            if(colors[i].r < .65f || colors[i].g > .3f || colors[i].b > .3f || colors[i].a < .95f) continue;
            x += i % image.width + .5;
            y += i / image.width + .5;
            count++;
        }
        if(count < 10) throw new BuildFailedException("Stereo eye contains no opaque red model");
        return new Vector2((float)(x/count/image.width),(float)(y/count/image.height));
    }

    private static void VerifyStereoGpu(Action<bool,string> check, string directory, Matrix4x4 projection)
    {
        var root = new GameObject("Headset stereo GPU verification");
        root.hideFlags = HideFlags.HideAndDontSave;
        var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        material.SetColor("_BaseColor", new Color(1f,.05f,.025f,1f));
        GameObject model = GameObject.CreatePrimitive(PrimitiveType.Cube);
        model.transform.SetParent(root.transform, false);
        model.layer = 29;
        model.transform.position = new Vector3(0f,0f,.7f);
        model.transform.localScale = Vector3.one * .06f;
        model.GetComponent<Renderer>().sharedMaterial = material;
        RenderTexture[] targets = new RenderTexture[2];
        Texture2D[] pictures = new Texture2D[2];
        try
        {
            for(int eye=0;eye<2;eye++)
            {
                GameObject eyeObject = new GameObject("Verification eye " + eye, typeof(Camera));
                eyeObject.transform.SetParent(root.transform,false);
                Camera camera = eyeObject.GetComponent<Camera>();
                camera.enabled = false;
                camera.transform.localPosition = PhoneHeadsetDisplay.EyeLocalOffset(eye == 1);
                camera.nearClipPlane = .05f;
                camera.farClipPlane = 10f;
                camera.aspect = 1f;
                camera.projectionMatrix = projection;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.clear;
                camera.cullingMask = 1 << 29;
                camera.allowHDR = false;
                camera.allowMSAA = false;
                UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = false;
                data.renderShadows = false;
                targets[eye] = new RenderTexture(256,256,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
                targets[eye].Create();
                RenderPipeline.SubmitRenderRequest(camera,
                    new UniversalRenderPipeline.SingleCameraRequest {destination = targets[eye]});
                pictures[eye] = Read(targets[eye]);
                File.WriteAllBytes(Path.Combine(directory,"headset-eye-"+eye+".png"),pictures[eye].EncodeToPNG());
            }
            Vector2 left = RedCentroid(pictures[0]), right = RedCentroid(pictures[1]);
            check(left.x > .52f && right.x < .48f && left.x-right.x > .05f && left.x-right.x < .15f,
                "Actual URP eye renders contain opposite horizontal parallax for a reachable object");
            check(Mathf.Abs(left.y-right.y) < 1f/256f,
                "Parallel eye poses do not introduce vertical disparity");
            check(pictures.All(picture=> picture.GetPixel(4,4).a < .01f),
                "Eye render targets preserve transparent camera background");
            check(pictures[0].GetPixel((int)(left.x*256),(int)(left.y*256)).a > .99f &&
                pictures[1].GetPixel((int)(right.x*256),(int)(right.y*256)).a > .99f,
                "Opaque geometry writes full alpha into both eye targets");
        }
        finally
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(material);
            foreach(Texture2D picture in pictures) if(picture != null) Object.DestroyImmediate(picture);
            foreach(RenderTexture target in targets) if(target != null) {target.Release();Object.DestroyImmediate(target);}
        }
    }

    private static Texture2D Solid(Color color)
    {
        var texture = new Texture2D(4,4,TextureFormat.RGBA32,false,true);
        texture.SetPixels(Enumerable.Repeat(color,16).ToArray());
        texture.Apply();
        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Clamp;
        return texture;
    }

    private static bool Near(Color actual, Color expected, float tolerance = .035f) =>
        Mathf.Abs(actual.r-expected.r)<tolerance && Mathf.Abs(actual.g-expected.g)<tolerance &&
        Mathf.Abs(actual.b-expected.b)<tolerance;

    private static void VerifyCompositeGpu(Action<bool,string> check, string directory)
    {
        Shader shader = Resources.Load<Shader>("SmartGeoAR_HeadsetComposite");
        check(shader != null && shader.isSupported,"Packaged headset compositor shader is supported");
        Material material = new Material(shader);
        Texture2D red = Solid(Color.red), blue = Solid(Color.blue), clear = Solid(Color.clear);
        Texture2D half = Solid(new Color(.5f,0f,0f,.5f));
        Texture2D camera = new Texture2D(64,64,TextureFormat.RGBA32,false,true);
        Color[] pattern = new Color[64*64];
        for(int y=0;y<64;y++) for(int x=0;x<64;x++) pattern[y*64+x]=new Color((x+.5f)/64f,(y+.5f)/64f,.25f,1f);
        camera.SetPixels(pattern);camera.Apply();camera.wrapMode=TextureWrapMode.Clamp;camera.filterMode=FilterMode.Bilinear;
        var target = new RenderTexture(512,256,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
        target.Create();
        var outputRoot = new GameObject("Headset compositor UI verification");
        outputRoot.hideFlags = HideFlags.HideAndDontSave;
        var cameraObject = new GameObject("Output camera", typeof(Camera));
        cameraObject.transform.SetParent(outputRoot.transform,false);
        Camera outputCamera = cameraObject.GetComponent<Camera>();
        outputCamera.enabled=false;outputCamera.orthographic=true;outputCamera.orthographicSize=.5f;
        outputCamera.aspect=2f;outputCamera.nearClipPlane=.01f;outputCamera.farClipPlane=5f;
        outputCamera.clearFlags=CameraClearFlags.SolidColor;outputCamera.backgroundColor=Color.magenta;
        outputCamera.cullingMask=1<<29;outputCamera.allowHDR=false;outputCamera.allowMSAA=false;
        outputCamera.GetUniversalAdditionalCameraData().renderPostProcessing=false;
        var canvasObject = new GameObject("Actual RawImage compositor",typeof(RectTransform),typeof(Canvas));
        canvasObject.transform.SetParent(outputRoot.transform,false);canvasObject.layer=29;
        Canvas canvas=canvasObject.GetComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;canvas.worldCamera=outputCamera;
        canvasObject.GetComponent<RectTransform>().sizeDelta=new Vector2(2f,1f);
        canvasObject.transform.position=Vector3.forward;
        var imageObject = new GameObject("Stereo image",typeof(RectTransform),typeof(RawImage));
        imageObject.transform.SetParent(canvasObject.transform,false);imageObject.layer=29;
        RectTransform imageRect=imageObject.GetComponent<RectTransform>();
        imageRect.anchorMin=Vector2.zero;imageRect.anchorMax=Vector2.one;imageRect.offsetMin=imageRect.offsetMax=Vector2.zero;
        RawImage image=imageObject.GetComponent<RawImage>();image.texture=Texture2D.whiteTexture;image.material=material;
        Texture2D picture = null;
        Action<string> render = name=>
        {
            Canvas.ForceUpdateCanvases();
            RenderPipeline.SubmitRenderRequest(outputCamera,
                new UniversalRenderPipeline.SingleCameraRequest{destination=target});
            if(picture != null) Object.DestroyImmediate(picture);
            picture=Read(target);
            File.WriteAllBytes(Path.Combine(directory,name+".png"),picture.EncodeToPNG());
        };
        Func<float,float,Color> sample = (x,y)=>picture.GetPixel(Mathf.Clamp((int)(x*512),0,511),Mathf.Clamp((int)(y*256),0,255));
        try
        {
            material.SetTexture("_CameraTex",camera);
            material.SetTexture("_LeftEye",red);material.SetTexture("_RightEye",blue);
            material.SetVector("_EyeProjection",new Vector4(1f,1f,0f,0f));
            material.SetVector("_CameraProjection",new Vector4(1f,1f,0f,0f));
            material.SetVector("_Distortion",Vector4.zero);
            render("headset-composite-eye-selection");
            check(!ShaderUtil.ShaderHasError(shader),"Headset compositor compiles for the actual GPU draw");
            check(Near(sample(.25f,.5f),Color.red) && Near(sample(.75f,.5f),Color.blue),
                "Left and right screen halves sample their own eye render targets");
            material.SetTexture("_LeftEye",clear);material.SetTexture("_RightEye",clear);
            render("headset-composite-camera-background");
            check(Near(sample(.125f,.25f),new Color(.25f,.25f,.25f)) &&
                Near(sample(.625f,.25f),new Color(.25f,.25f,.25f)) &&
                Near(sample(.375f,.75f),new Color(.75f,.75f,.25f)),
                "Each eye maps its full UV domain to the same calibrated mono camera background");
            material.SetFloat("_LensCenterShift",.1f);
            render("headset-composite-lens-centres");
            check(Near(sample(.3f,.5f),new Color(.5f,.5f,.25f)) &&
                Near(sample(.7f,.5f),new Color(.5f,.5f,.25f)),
                "Compositor shifts left and right optical centres inward by opposite amounts");
            material.SetFloat("_LensCenterShift",0f);
            material.SetTexture("_LeftEye",half);material.SetTexture("_RightEye",half);
            render("headset-composite-premultiplied-alpha");
            check(Near(sample(.25f,.5f),new Color(.75f,.25f,.125f)),
                "Premultiplied eye alpha composites camera RGB once without dark edges");
            material.SetTexture("_LeftEye",clear);material.SetTexture("_RightEye",clear);
            material.SetVector("_CameraProjection",new Vector4(.5f,1f,.2f,0f));
            render("headset-composite-intrinsics");
            check(Near(sample(.125f,.25f),new Color(.275f,.25f,.25f)),
                "Compositor maps eye rays through camera focal length and principal point");
            material.SetVector("_CameraProjection",new Vector4(1f,1f,0f,0f));
            material.SetVector("_Distortion",new Vector4(.22f,.24f,0f,0f));
            render("headset-composite-lens-warp");
            check(Near(sample(.001f,.001f),Color.black,.015f) &&
                Near(sample(.25f,.5f),new Color(.5f,.5f,.25f)),
                "Lens prewarp keeps eye centre valid and masks outside eye image black");
            material.SetVector("_Distortion",Vector4.zero);
            material.SetVector("_CameraProjection",new Vector4(4f,4f,0f,0f));
            render("headset-composite-outside-camera-fov");
            check(Near(sample(.05f,.5f),Color.black,.015f) &&
                Near(sample(.25f,.5f),new Color(.5f,.5f,.25f)),
                "Missing real-camera coverage is black rather than stretched edge pixels");
        }
        finally
        {
            Object.DestroyImmediate(outputRoot);
            if(picture != null)Object.DestroyImmediate(picture);
            target.Release();Object.DestroyImmediate(target);
            Object.DestroyImmediate(material);
            foreach(Texture2D texture in new[]{red,blue,clear,half,camera})Object.DestroyImmediate(texture);
        }
    }

    private static void VerifyLifecycle(Action<bool,string> check)
    {
        const string preference="SmartGeoAR.Headset";
        Type type=typeof(PhoneHeadsetDisplay);
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        const BindingFlags statics=BindingFlags.Static|BindingFlags.NonPublic;
        bool hadPreference=PlayerPrefs.HasKey(preference);
        int oldPreference=PlayerPrefs.GetInt(preference,0);
        object oldInstance=type.GetField("instance",statics).GetValue(null);
        object oldRequested=type.GetField("requested",statics).GetValue(null);
        object oldRead=type.GetField("preferenceRead",statics).GetValue(null);
        int originalFrameRate=Application.targetFrameRate;
        int originalSleepTimeout=Screen.sleepTimeout;
        GameObject root=new GameObject("Headset lifecycle verification");
        root.hideFlags=HideFlags.HideAndDontSave;root.SetActive(false);
        Camera camera=root.AddComponent<Camera>();camera.enabled=true;
        ARCameraManager manager=root.AddComponent<ARCameraManager>();manager.enabled=true;
        camera.projectionMatrix=Matrix4x4.Perspective(47f,2.2f,.04f,20f);
        Matrix4x4 originalProjection=camera.projectionMatrix;
        PhoneCameraSender.CameraIntrinsics intrinsics=PhoneCameraSender.IntrinsicsForProjection(originalProjection,640,288);
        PhoneHeadsetDisplay display=root.AddComponent<PhoneHeadsetDisplay>();
        type.GetMethod("Awake",flags).Invoke(display,null);
        try
        {
            bool created=(bool)type.GetMethod("CreateRendering",flags).Invoke(display,null);
            check(created,"Production headset setup creates eye targets and compositor resources");
            Camera[] eyes=(Camera[])type.GetField("eyes",flags).GetValue(display);
            Camera[] controls=(Camera[])type.GetField("controls",flags).GetValue(display);
            RenderTexture[] targets=(RenderTexture[])type.GetField("eyeTextures",flags).GetValue(display);
            check(eyes.All(eye=>eye!=null&&!eye.enabled&&eye.clearFlags==CameraClearFlags.SolidColor&&
                eye.backgroundColor.a==0f&&!eye.GetUniversalAdditionalCameraData().renderPostProcessing) &&
                targets.All(target=>target!=null&&target.IsCreated()),
                "Production eye cameras clear transparent and render only on explicit requests");
            check(controls.All(control=>control!=null&&control.enabled&&
                control.GetUniversalAdditionalCameraData().renderType==CameraRenderType.Overlay&&control.cullingMask==1<<30),
                "Production per-eye recovery overlays are enabled and isolate control layer");
            PhoneCameraSender.CameraIntrinsics after=PhoneCameraSender.IntrinsicsForProjection(camera.projectionMatrix,640,288);
            check(camera.projectionMatrix==originalProjection && intrinsics.fx==after.fx && intrinsics.fy==after.fy &&
                intrinsics.cx==after.cx && intrinsics.cy==after.cy && manager.enabled,
                "Headset setup preserves original AR camera projection and capture intrinsics");
            PhoneHeadsetDisplay.SetActive(true);
            check(PhoneHeadsetDisplay.IsActive,"Headset activation records requested display mode");
            // Edit-mode cleanup uses DestroyImmediate; runtime StopRendering uses deferred Destroy.
            // Exercise its state restoration after removing the graphics resources explicitly.
            foreach(Camera eye in eyes)if(eye!=null)Object.DestroyImmediate(eye.gameObject);
            for(int i=0;i<eyes.Length;i++){eyes[i]=null;controls[i]=null;if(targets[i]!=null){targets[i].Release();Object.DestroyImmediate(targets[i]);targets[i]=null;}}
            foreach(string field in new[]{"outputCamera","outputCanvas"})
            {
                Component component=(Component)type.GetField(field,flags).GetValue(display);
                if(component!=null)Object.DestroyImmediate(component.gameObject);
                type.GetField(field,flags).SetValue(display,null);
            }
            RenderTexture background=(RenderTexture)type.GetField("cameraTexture",flags).GetValue(display);
            if(background!=null){background.Release();Object.DestroyImmediate(background);}
            type.GetField("cameraTexture",flags).SetValue(display,null);
            Material compositor=(Material)type.GetField("composite",flags).GetValue(display);
            if(compositor!=null)Object.DestroyImmediate(compositor);
            type.GetField("composite",flags).SetValue(display,null);
            type.GetField("originalCameraEnabled",flags).SetValue(display,true);
            type.GetField("originalSleepTimeout",flags).SetValue(display,Screen.sleepTimeout);
            type.GetField("originalFrameRate",flags).SetValue(display,originalFrameRate);
            type.GetField("rendering",flags).SetValue(display,true);
            camera.enabled=false;
            PhoneHeadsetDisplay.SetActive(false);
            check(!PhoneHeadsetDisplay.IsActive&&camera.enabled&&manager.enabled&&camera.projectionMatrix==originalProjection,
                "Headset exit restores camera visibility while retaining AR provider and intrinsics");
        }
        finally
        {
            // Safely clean up even when an assertion fails before the normal resource teardown.
            foreach(Transform child in root.transform.Cast<Transform>().ToArray())Object.DestroyImmediate(child.gameObject);
            foreach(string field in new[]{"outputCamera","outputCanvas","composite","cameraTexture"})type.GetField(field,flags).SetValue(display,null);
            Camera[] eyes=(Camera[])type.GetField("eyes",flags).GetValue(display);
            Camera[] controls=(Camera[])type.GetField("controls",flags).GetValue(display);
            RenderTexture[] textures=(RenderTexture[])type.GetField("eyeTextures",flags).GetValue(display);
            for(int i=0;i<eyes.Length;i++){eyes[i]=controls[i]=null;if(textures[i]!=null){textures[i].Release();Object.DestroyImmediate(textures[i]);textures[i]=null;}}
            CommandBuffer commands=(CommandBuffer)type.GetField("commands",flags).GetValue(display);
            commands?.Dispose();type.GetField("commands",flags).SetValue(display,null);
            type.GetField("rendering",flags).SetValue(display,false);
            Object.DestroyImmediate(root);
            type.GetField("instance",statics).SetValue(null,oldInstance);
            type.GetField("requested",statics).SetValue(null,oldRequested);
            type.GetField("preferenceRead",statics).SetValue(null,oldRead);
            if(hadPreference)PlayerPrefs.SetInt(preference,oldPreference);else PlayerPrefs.DeleteKey(preference);
            PlayerPrefs.Save();
            Application.targetFrameRate=originalFrameRate;
            Screen.sleepTimeout=originalSleepTimeout;
        }
    }
}
