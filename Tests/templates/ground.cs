// Ground test: three caps like the ReStock Mk16-XL (one part, apex pieces, double-sided, UV seam) and one large single
// canopy are cut above flat ground and simulated for 30 s at 60 frames/s with the original Step, LayerCloth,
// SmoothRender, RenderNormals and ClothMesh.Apply.
// Log lines CHUTEGROUND: per second rest state (ASLEEP), vmax, height, supported points; run time; interpenetration;
// top view (share of the later canopy through which the earlier one pokes). Images go to the output folder.
// Environment variables:
//   CHUTE_OUT    output folder for the images (default: <project>/ChuteFXTestOutput/ground)
//   CHUTE_TAG    prefix of the image names (default "test")
//   CHUTE_WATER  1 = water landing (surface 1 m below the cut point, soaks for 20 s, then sinks; 60 s)
//   CHUTE_LAYER  layer thickness in m (0 = no layers)
//   CHUTE_SMOOTH render smoothing passes
// Run: Unity -batchmode -quit -projectPath <project> -executeMethod ChuteFXGround.GroundTest.Run -logFile ground.log
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ChuteFXGround
{
    internal static class Cfg
    {
        public static float deflateTime = 3f, settleTime = 1.5f, cutTerminalSpeed = 4f, turbulence = 6f, groundFriction = 0.6f, damping = 0.02f;
        public static int clothIterations = 5, minPieceSize = 20, renderSmooth = 0;
        public static float bendStiffness = 0.2f, settleMax = 25f, compressStiffness = 0.3f, groundCompressStiffness = 0.8f, groundBendStiffness = 0.6f;
        public static float sailDrag = 2f, spreadPush = 5f, inflatePressure = 8f, isoDrag = 0.3f, layerThickness = 0.05f;
        public static bool debugLog = true;
        public static List<string> log = new List<string>();
        public static void Log(string s) { log.Add(s); }
    }

/*@FALLEN@*/

/*@CLOTHMESH@*/

    public class GroundTest
    {
        List<Fallen> fallen = new List<Fallen>();
        static Dictionary<long, float> sharedStack = new Dictionary<long, float>();
        const float StackCell = 0.35f, StackLayer = 0.025f;

/*@FUNCS@*/

        // ---- test scene ----

        static string OutDir;
        static bool waterMode;
        static float simEnd = 30f;

        class Piece
        {
            public List<Vector3> rv = new List<Vector3>();
            public List<Vector2> uv = new List<Vector2>();
            public List<int> map = new List<int>();
            public List<int> tri = new List<int>();
        }

        // Spherical cap (hem radius a, height h, vent r0), rings x gores; render vertices with a UV seam (last gore twice),
        // optionally double-sided (second set of render vertices on the same points, triangles flipped)
        static int Cap(List<Vector3> P, List<int> meshOf, Piece pc, int mesh, Vector3 c, Quaternion rot, float a, float h, float r0,
                       int rings, int segs, bool center, float yOff, bool twoSided)
        {
            float Rs = (a * a + h * h) / (2f * h);
            Func<float, float> Y = r => Mathf.Sqrt(Mathf.Max(0f, Rs * Rs - r * r)) + (h - Rs) + yOff;
            int centerIdx = -1;
            if (center) { centerIdx = P.Count; P.Add(c + rot * new Vector3(0f, Y(0f), 0f)); meshOf.Add(mesh); }
            int ringStart = P.Count;
            for (int j = 0; j < rings; j++)
            {
                float rr = Mathf.Lerp(r0, a, rings == 1 ? 1f : (float)j / (rings - 1));
                for (int k = 0; k < segs; k++)
                {
                    float an = k * Mathf.PI * 2f / segs;
                    P.Add(c + rot * new Vector3(Mathf.Cos(an) * rr, Y(rr), Mathf.Sin(an) * rr));
                    meshOf.Add(mesh);
                }
            }
            for (int side = 0; side < (twoSided ? 2 : 1); side++)
            {
                int rvCenter = -1;
                if (center) { rvCenter = pc.rv.Count; pc.rv.Add(P[centerIdx]); pc.uv.Add(new Vector2(0.5f, 1f)); pc.map.Add(centerIdx); }
                int rvRing = pc.rv.Count;
                for (int j = 0; j < rings; j++)
                    for (int k = 0; k <= segs; k++)
                    {
                        int pi = ringStart + j * segs + (k % segs);
                        pc.rv.Add(P[pi]);
                        pc.uv.Add(new Vector2(k / 4f, (float)j / Mathf.Max(1, rings - 1)));
                        pc.map.Add(pi);
                    }
                for (int j = 0; j + 1 < rings; j++)
                    for (int k = 0; k < segs; k++)
                    {
                        int A = rvRing + j * (segs + 1) + k, B = A + 1, C = A + segs + 1, D = C + 1;
                        if (side == 0) { pc.tri.Add(A); pc.tri.Add(B); pc.tri.Add(C); pc.tri.Add(B); pc.tri.Add(D); pc.tri.Add(C); }
                        else { pc.tri.Add(A); pc.tri.Add(C); pc.tri.Add(B); pc.tri.Add(B); pc.tri.Add(C); pc.tri.Add(D); }
                    }
                if (center)
                    for (int k = 0; k < segs; k++)
                    {
                        int A = rvRing + k, B = A + 1;
                        if (side == 0) { pc.tri.Add(rvCenter); pc.tri.Add(B); pc.tri.Add(A); }
                        else { pc.tri.Add(rvCenter); pc.tri.Add(A); pc.tri.Add(B); }
                    }
            }
            return ringStart + (rings - 1) * segs;   // first point of the hem
        }

        static Texture2D StripeTex(Color a, Color b)
        {
            Texture2D t = new Texture2D(64, 4, TextureFormat.RGBA32, true);
            for (int x = 0; x < 64; x++) for (int y = 0; y < 4; y++) t.SetPixel(x, y, x < 32 ? a : b);
            t.wrapMode = TextureWrapMode.Repeat;
            t.filterMode = FilterMode.Bilinear;
            t.Apply();
            return t;
        }

        // like the end of SpawnFallen: edges, bend edges, seams, lines to the knot, riser held at the part
        Fallen Build(GameObject root, List<Vector3> P, List<int> meshOfL, List<Piece> pieces, Material mat, List<int[]> rimRings,
                     Vector3 conf, Vector3 pinPos, Vector3 outDir, float seed)
        {
            Fallen f = new Fallen();
            f.go = root;
            int nCloth = P.Count;
            Dictionary<long, float> edges = new Dictionary<long, float>();
            Dictionary<long, float> bends = new Dictionary<long, float>();
            Dictionary<long, int> opp = new Dictionary<long, int>();
            List<bool> used = new List<bool>();
            List<float> pArea = new List<float>();
            for (int i = 0; i < nCloth; i++) { used.Add(false); pArea.Add(0f); }
            foreach (Piece pc in pieces)
                for (int k = 0; k + 2 < pc.tri.Count; k += 3)
                {
                    int a = pc.map[pc.tri[k]], b = pc.map[pc.tri[k + 1]], c = pc.map[pc.tri[k + 2]];
                    float area2 = Vector3.Cross(P[b] - P[a], P[c] - P[a]).magnitude;
                    pArea[a] += area2 / 6f; pArea[b] += area2 / 6f; pArea[c] += area2 / 6f;
                    used[a] = true; used[b] = true; used[c] = true;
                    AddEdge(edges, a, b, P); AddEdge(edges, b, c, P); AddEdge(edges, c, a, P);
                    AddBend(bends, opp, a, b, c, P); AddBend(bends, opp, b, c, a, P); AddBend(bends, opp, c, a, b, P);
                }
            HashSet<long> clothEdgeKeys = new HashSet<long>(edges.Keys);
            int dropped, groups;
            int[] groupOf;
            int stitches = StitchIslands(P, edges, bends, nCloth, used, meshOfL.ToArray(), out dropped, out groupOf, out groups);
            int layerStitches = StitchLayers(P, edges, nCloth, used, groupOf, groups);
            foreach (Piece pc in pieces)
            {
                Mesh m = new Mesh();
                m.SetVertices(pc.rv);
                m.SetUVs(0, pc.uv);
                List<int> fin = new List<int>();
                for (int k = 0; k + 2 < pc.tri.Count; k += 3)
                {
                    if (!used[pc.map[pc.tri[k]]] || !used[pc.map[pc.tri[k + 1]]] || !used[pc.map[pc.tri[k + 2]]]) continue;
                    fin.Add(pc.tri[k]); fin.Add(pc.tri[k + 1]); fin.Add(pc.tri[k + 2]);
                }
                m.SetTriangles(fin, 0);
                m.RecalculateNormals();
                m.RecalculateBounds();
                GameObject child = new GameObject("cloth");
                child.transform.SetParent(root.transform, false);
                child.AddComponent<MeshFilter>().sharedMesh = m;
                MeshRenderer mr = child.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                ClothMesh cm = new ClothMesh(m, pc.map.ToArray(), mr);
                cm.FinishTopology();
                f.parts.Add(cm);
            }
            // lines: per cap 16 hem points to the knot, riser from the knot to the part
            int confluence = P.Count;
            P.Add(conf);
            List<int[]> ropes = new List<int[]>();
            int seg = 20;
            foreach (int[] rim in rimRings)
                for (int k = 0; k < 16; k++)
                {
                    int at = rim[0] + (k * rim[1]) / 16;
                    Vector3 top = P[at];
                    int[] rope = new int[seg + 1];
                    rope[0] = confluence;
                    for (int s2 = 1; s2 < seg; s2++) { rope[s2] = P.Count; P.Add(Vector3.Lerp(conf, top, (float)s2 / seg)); }
                    rope[seg] = at;
                    float sl = (top - conf).magnitude * 1.12f / seg;
                    for (int s2 = 0; s2 < seg; s2++) AddEdgeLen(edges, rope[s2], rope[s2 + 1], sl);
                    ropes.Add(rope);
                }
            int rs = 5;
            int[] riser = new int[rs + 1];
            riser[0] = confluence;
            for (int s = 1; s <= rs; s++) { riser[s] = P.Count; P.Add(Vector3.Lerp(conf, pinPos, (float)s / rs)); }
            for (int s = 0; s < rs; s++) AddEdgeLen(edges, riser[s], riser[s + 1], (pinPos - conf).magnitude / rs);
            ropes.Add(riser);
            f.ropes = ropes.ToArray();
            f.pin = riser[rs];
            f.pinLocal = pinPos;
            f.pinMaxDist = 1e6f;

            int n = P.Count;
            f.pos = P.ToArray();
            f.vel = new Vector3[n]; f.prev = new Vector3[n]; f.rest0 = new Vector3[n]; f.offs = new float[n];
            f.onGround = new bool[n]; f.stacked = new bool[n]; f.nCloth = nCloth; f.active = new bool[n]; f.gY = new float[n]; f.area = new float[n];
            for (int i = 0; i < nCloth; i++) f.area[i] = pArea[i];
            Vector3 cen = Vector3.zero;
            for (int i = 0; i < nCloth; i++) cen += f.pos[i];
            cen /= nCloth;
            Vector3 mn = f.pos[0], mx = f.pos[0];
            for (int i = 0; i < nCloth; i++) if (used[i]) { mn = Vector3.Min(mn, f.pos[i]); mx = Vector3.Max(mx, f.pos[i]); }
            f.size = (mx - mn).magnitude;
            for (int i = 0; i < n; i++)
            {
                f.active[i] = i >= nCloth || used[i];
                f.gY[i] = -1e6f;
                f.rest0[i] = f.pos[i] - cen;
                f.offs[i] = i < nCloth ? 0.017f : 0.012f;
            }
            f.ea = new int[edges.Count]; f.eb = new int[edges.Count]; f.el = new float[edges.Count]; f.et = new bool[edges.Count];
            int e = 0;
            foreach (KeyValuePair<long, float> kv in edges)
            {
                f.ea[e] = (int)(kv.Key / 1000000L); f.eb[e] = (int)(kv.Key % 1000000L); f.el[e] = kv.Value; f.et[e] = !clothEdgeKeys.Contains(kv.Key); e++;
            }
            f.ba = new int[bends.Count]; f.bb = new int[bends.Count]; f.bl = new float[bends.Count];
            e = 0;
            foreach (KeyValuePair<long, float> kv in bends)
            {
                f.ba[e] = (int)(kv.Key / 1000000L); f.bb[e] = (int)(kv.Key % 1000000L); f.bl[e] = kv.Value; e++;
            }
            float ex = InitLayers(f);
            BuildRenderTopology(f);
            f.wind = new Vector3(2f, 0f, 1f);
            f.atmo = true;
            f.gravity = 9.81f;
            f.seed = seed;
            f.noMemory = true;
            f.outDir = outDir.normalized;
            f.outAcc = Cfg.spreadPush;
            f.groundY = 0f; f.slopeX = 0f; f.slopeZ = 0f;
            f.alt = waterMode ? 1.0 : 100.0;
            if (waterMode) { f.water = true; f.seabedY = -1e6f; }
            f.lift = 0.13f;
            f.body = root;
            f.dirty = true;
            Debug.Log("CHUTEGROUND setup: points=" + nCloth + " canopies=" + groups + " stitches=" + stitches + " layer_stitches=" + layerStitches + " removed=" + dropped +
                      " edges=" + edges.Count + " bends=" + bends.Count + " layer_neighbours=" + ex.ToString("F2") + "m boundary_points=" + CountTrue(f.smFix));
            return f;
        }

        void Frame(float t, float dt, System.Diagnostics.Stopwatch swL, System.Diagnostics.Stopwatch swR)
        {
            const float h = 0.025f;
            foreach (Fallen f in fallen)
            {
                if (f.sleeping) continue;
                f.acc = Mathf.Min(f.acc + dt, 3f * h);
                int steps = Mathf.FloorToInt(f.acc / h);
                if (steps <= 0) continue;
                f.acc -= steps * h;
                ComputeNormals(f);
                ComputeTurbulence(f, t);
                f.dirty = true;
                for (int s = 0; s < steps; s++) Step(f, h, t, t - f.spawnTime);
            }
            swL.Start();
            if (Cfg.layerThickness > 0f && ((layerTick++ & 1) == 0 || LayerImpact(fallen, t))) LayerCloth();   // as in the game
            swL.Stop();
            swR.Start();
            foreach (Fallen f in fallen)
            {
                if (!f.dirty) continue;
                f.dirty = false;
                SmoothRender(f);
                RenderNormals(f);
                foreach (ClothMesh cm in f.parts) cm.Apply(f.rpos);
            }
            swR.Stop();
        }

        static void Shot(Camera cam, RenderTexture rt, Texture2D read, string name)
        {
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            read.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            read.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), read.EncodeToPNG());
        }

        public static void Run()
        {
            OutDir = Environment.GetEnvironmentVariable("CHUTE_OUT");
            if (string.IsNullOrEmpty(OutDir)) OutDir = Path.Combine(Directory.GetCurrentDirectory(), "ChuteFXTestOutput/ground");
            Directory.CreateDirectory(OutDir);
            string tag = Environment.GetEnvironmentVariable("CHUTE_TAG");
            if (string.IsNullOrEmpty(tag)) tag = "test";
            string ev = Environment.GetEnvironmentVariable("CHUTE_SMOOTH");
            if (!string.IsNullOrEmpty(ev)) int.TryParse(ev, out Cfg.renderSmooth);
            waterMode = Environment.GetEnvironmentVariable("CHUTE_WATER") == "1";
            if (waterMode) simEnd = 60f;
            string el = Environment.GetEnvironmentVariable("CHUTE_LAYER");
            if (!string.IsNullOrEmpty(el)) float.TryParse(el, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out Cfg.layerThickness);
            GroundTest T = new GroundTest();
            List<GameObject> junk = new List<GameObject>();
            UnityEngine.Random.InitState(7);

            // light, ground, camera
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.5f, 0.58f, 0.7f);
            RenderSettings.ambientEquatorColor = new Color(0.42f, 0.45f, 0.42f);
            RenderSettings.ambientGroundColor = new Color(0.2f, 0.24f, 0.16f);
            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowResolution = ShadowResolution.VeryHigh;
            QualitySettings.shadowDistance = 120f;
            GameObject sunGo = new GameObject("Sun"); junk.Add(sunGo);
            Light sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.1f; sun.shadows = LightShadows.Soft; sun.color = new Color(1f, 0.97f, 0.92f);
            sunGo.transform.rotation = Quaternion.Euler(42f, 130f, 0f);
            Shader std = Shader.Find("Standard");
            Material grass = new Material(std); grass.color = new Color(0.3f, 0.42f, 0.2f); grass.SetFloat("_Glossiness", 0.05f);
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane); junk.Add(ground);
            ground.transform.localScale = new Vector3(20f, 1f, 20f);
            ground.transform.position = new Vector3(0f, 0.13f, 0f);   // visible ground (raised like Parallax in the game)
            if (waterMode) { ground.transform.position = new Vector3(0f, -40f, 0f); grass.color = new Color(0.1f, 0.25f, 0.4f); }
            ground.GetComponent<Renderer>().sharedMaterial = grass;
            Material matXL = new Material(std); matXL.mainTexture = StripeTex(new Color(0.93f, 0.42f, 0.12f), new Color(0.95f, 0.95f, 0.92f)); matXL.SetFloat("_Glossiness", 0.25f);
            Material matDr = new Material(std); matDr.mainTexture = StripeTex(new Color(0.85f, 0.3f, 0.3f), new Color(0.9f, 0.35f, 0.35f)); matDr.SetFloat("_Glossiness", 0.25f);
            GameObject capsule = GameObject.CreatePrimitive(PrimitiveType.Cylinder); junk.Add(capsule);
            capsule.transform.localScale = new Vector3(2.5f, 1f, 2.5f);
            capsule.transform.position = new Vector3(0f, 1.1f, 0f);

            // Mk16-XL: three caps on one part, each with an apex piece (separate, 0.33 m above the vent)
            GameObject rootXL = new GameObject("XL"); junk.Add(rootXL);
            {
                List<Vector3> P = new List<Vector3>();
                List<int> meshOf = new List<int>();
                List<Piece> pieces = new List<Piece>();
                List<int[]> rims = new List<int[]>();
                for (int q = 0; q < 3; q++)
                {
                    float an = q * Mathf.PI * 2f / 3f + 0.4f;
                    Vector3 dir = new Vector3(Mathf.Cos(an), 0f, Mathf.Sin(an));
                    Vector3 c = dir * 4.2f + new Vector3(0f, 11f, 0f);
                    Quaternion rot = Quaternion.AngleAxis(-25f, Vector3.Cross(dir, Vector3.up));
                    Piece pc = new Piece();
                    int rim = Cap(P, meshOf, pc, q, c, rot, 3.4f, 2.2f, 0.35f, 24, 38, false, 0f, true);
                    Cap(P, meshOf, pc, q, c, rot, 0.3f, 0.05f, 0.05f, 5, 29, true, 2.2f + 0.28f, true);
                    pieces.Add(pc);
                    rims.Add(new int[] { rim, 38 });
                }
                Fallen f = T.Build(rootXL, P, meshOf, pieces, matXL, rims, new Vector3(0f, 2.6f, 0f), new Vector3(0f, 1.9f, 0f), new Vector3(1f, 0f, 0.3f), 11f);
                T.fallen.Add(f);
            }
            // a single large canopy next to it, falls partly onto the XL caps
            GameObject rootDr = new GameObject("Drogue"); junk.Add(rootDr);
            {
                List<Vector3> P = new List<Vector3>();
                List<int> meshOf = new List<int>();
                List<Piece> pieces = new List<Piece>();
                List<int[]> rims = new List<int[]>();
                Vector3 c = new Vector3(-4f, 10.5f, 5f);
                Piece pc = new Piece();
                int rim = Cap(P, meshOf, pc, 0, c, Quaternion.AngleAxis(20f, Vector3.right), 4.4f, 3f, 0.3f, 22, 34, false, 0f, true);
                Cap(P, meshOf, pc, 0, c, Quaternion.AngleAxis(20f, Vector3.right), 0.28f, 0.05f, 0.05f, 4, 24, true, 3f + 0.25f, true);
                pieces.Add(pc);
                rims.Add(new int[] { rim, 34 });
                Fallen f = T.Build(rootDr, P, meshOf, pieces, matDr, rims, new Vector3(0.6f, 2.6f, 0.4f), new Vector3(0.6f, 1.9f, 0.4f), new Vector3(0.5f, 0f, -0.6f), 23f);
                T.fallen.Add(f);
            }

            Camera cam = new GameObject("Camera").AddComponent<Camera>(); junk.Add(cam.gameObject);
            cam.fieldOfView = 38f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.62f, 0.72f, 0.85f);
            RenderTexture rt = new RenderTexture(1280, 720, 24);
            rt.antiAliasing = 4;
            Texture2D read = new Texture2D(1280, 720, TextureFormat.RGB24, false);

            System.Diagnostics.Stopwatch swL = new System.Diagnostics.Stopwatch(), swR = new System.Diagnostics.Stopwatch();
            float dt = 1f / 60f, t = 0f;
            float[] shots = waterMode ? new float[] { 6f, 12f, 30f, 45f, 59f } : new float[] { 1.5f, 3f, 6f, 12f, 30f };
            int nextShot = 0;
            int frames = 0;
            float nextLog = 1f;
            Vector3[][] last = new Vector3[T.fallen.Count][];
            for (int k = 0; k < T.fallen.Count; k++) last[k] = (Vector3[])T.fallen[k].pos.Clone();
            float jitterAcc = 0f; int jitterN = 0;
            while (t < simEnd)
            {
                t += dt;
                frames++;
                T.Frame(t, dt, swL, swR);
                if (waterMode)
                    foreach (Fallen f in T.fallen)
                    {
                        if (!f.contact) continue;
                        // like UpdateFallen: soak up water (20 s), then sink (0.15 m/s)
                        f.wetTime += dt;
                        f.soak = Mathf.Clamp01(f.wetTime / 20f);
                        if (f.soak >= 1f) f.sink += 0.15f * dt;
                    }
                // frame-to-frame motion of the cloth, from 20 s on
                if (t > 20f)
                {
                    for (int k = 0; k < T.fallen.Count; k++)
                    {
                        Fallen f = T.fallen[k];
                        float sum = 0f;
                        for (int i = 0; i < f.nCloth; i++) sum += (f.pos[i] - last[k][i]).magnitude;
                        jitterAcc += sum / f.nCloth; jitterN++;
                    }
                }
                for (int k = 0; k < T.fallen.Count; k++) Array.Copy(T.fallen[k].pos, last[k], T.fallen[k].nCloth);
                if (t >= nextLog)
                {
                    nextLog += 1f;
                    string s = "CHUTEGROUND t=" + t.ToString("F0");
                    foreach (Fallen f in T.fallen)
                    {
                        float maxV = 0f, hMax = -1e9f;
                        int ly = 0, fast = 0, fastRope = 0, iMax = -1;
                        for (int i = 0; i < f.pos.Length; i++)
                        {
                            if (!f.active[i]) continue;
                            float vv = f.vel[i].magnitude;
                            if (vv > maxV) { maxV = vv; iMax = i; }
                            if (vv > 0.1f) { if (i < f.nCloth) fast++; else fastRope++; }
                        }
                        for (int i = 0; i < f.nCloth; i++) { if (!f.active[i]) continue; hMax = Mathf.Max(hMax, f.pos[i].y); if (f.lyF[i] > -1e5f) ly++; }
                        // spike = highest cloth point above the median cloth height (a cone in the middle on water)
                        float[] hs = new float[f.nCloth]; int nh = 0;
                        for (int i = 0; i < f.nCloth; i++) if (f.active[i]) hs[nh++] = f.pos[i].y;
                        Array.Sort(hs, 0, nh);
                        float spike = nh > 0 ? hs[nh - 1] - hs[nh / 2] : 0f;
                        s += " | " + f.go.name + " spike=" + spike.ToString("F2") + (f.sleeping ? " ASLEEP" : "") + " vmax=" + maxV.ToString("F2") + (iMax >= f.nCloth ? "(line " : "(cloth ") + "y=" + (iMax >= 0 ? f.pos[iMax].y.ToString("F2") : "-") +
                             (iMax >= 0 && iMax < f.nCloth ? " supported=" + (f.lyF[iMax] > -1e5f ? "yes" : "no") + " on_ground=" + f.onGround[iMax] : "") + ") fast>0.1: cloth=" + fast + " line=" + fastRope + " height=" + hMax.ToString("F2") + " supported=" + ly;
                    }
                    Debug.Log(s);
                }
                if (nextShot < shots.Length && t >= shots[nextShot])
                {
                    Vector3 c = Vector3.zero; int nc = 0;
                    foreach (Fallen f in T.fallen) for (int i = 0; i < f.nCloth; i += 7) { c += f.pos[i]; nc++; }
                    c /= Mathf.Max(1, nc);
                    if (t < 5f) c.y = Mathf.Max(c.y, 2f);
                    cam.transform.position = c + new Vector3(-13f, 10f, -14f);
                    cam.transform.LookAt(c);
                    Shot(cam, rt, read, tag + "_" + shots[nextShot].ToString("00.0", System.Globalization.CultureInfo.InvariantCulture) + "s");
                    if (nextShot == shots.Length - 1)
                    {
                        cam.transform.position = c + new Vector3(-5f, 4.5f, -6f);
                        cam.transform.LookAt(c);
                        Shot(cam, rt, read, tag + "_close");
                    }
                    nextShot++;
                }
            }
            // render smoothing levels 0/2/4 for comparison
            {
                Vector3 c = Vector3.zero; int nc = 0;
                foreach (Fallen f in T.fallen) for (int i = 0; i < f.nCloth; i += 7) { c += f.pos[i]; nc++; }
                c /= Mathf.Max(1, nc);
                int keep = Cfg.renderSmooth;
                foreach (int sm in new int[] { 0, 2, 4 })
                {
                    Cfg.renderSmooth = sm;
                    foreach (Fallen f in T.fallen) { SmoothRender(f); RenderNormals(f); foreach (ClothMesh cm in f.parts) { cm.Apply(f.rpos); cm.mesh.RecalculateTangents(); } }
                    cam.transform.position = c + new Vector3(-6f, 5f, -7f); cam.transform.LookAt(c);
                    Shot(cam, rt, read, tag + "_smooth" + sm);
                }
                Cfg.renderSmooth = keep;
            }
            // Top view in flat colours (XL blue, drogue red): once everything, once without the XL. Where blue shows inside
            // the drogue outline, the XL (landed first, lies below) pokes through the drogue.
            {
                Fallen fd = T.fallen[1], fxl = T.fallen[0];
                Shader unlit = Shader.Find("Unlit/Color");
                Material mR = new Material(unlit); mR.color = Color.red;
                Material mB = new Material(unlit); mB.color = Color.blue;
                foreach (ClothMesh cm in fd.parts) cm.r.sharedMaterial = mR;
                foreach (ClothMesh cm in fxl.parts) cm.r.sharedMaterial = mB;
                Vector3 mn = fd.pos[0], mx = fd.pos[0];
                for (int i = 0; i < fd.nCloth; i++) { mn = Vector3.Min(mn, fd.pos[i]); mx = Vector3.Max(mx, fd.pos[i]); }
                Vector3 cc = 0.5f * (mn + mx);
                cam.orthographic = true;
                cam.orthographicSize = 0.55f * Mathf.Max(mx.x - mn.x, mx.z - mn.z);
                cam.transform.position = new Vector3(cc.x, 30f, cc.z);
                cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                Shot(cam, rt, read, tag + "_top_all");
                Color32[] all = read.GetPixels32();
                foreach (ClothMesh cm in fxl.parts) cm.r.enabled = false;
                Shot(cam, rt, read, tag + "_top_drogue");
                Color32[] dro = read.GetPixels32();
                foreach (ClothMesh cm in fxl.parts) cm.r.enabled = true;
                int foot = 0, through = 0;
                for (int i = 0; i < dro.Length; i++)
                {
                    if (dro[i].r > 200 && dro[i].b < 60) { foot++; if (all[i].b > 200 && all[i].r < 60) through++; }
                }
                Debug.Log("CHUTEGROUND top view: drogue area " + foot + " pixels, XL visible on " + through + " (" + (100f * through / Mathf.Max(1, foot)).ToString("F1") + " %)");
                cam.orthographic = false;
            }
            Debug.Log("CHUTEGROUND run time: layers " + (swL.Elapsed.TotalMilliseconds / frames).ToString("F2") + " ms/frame, rendering " + (swR.Elapsed.TotalMilliseconds / frames).ToString("F2") +
                      " ms/frame; mean motion after 20 s " + (jitterN > 0 ? (1000f * jitterAcc / jitterN).ToString("F3") : "-") + " mm/frame");
            // Interpenetration in the final state: point pairs of different layers (another canopy, or within the canopy
            // farther apart than the layer neighbour distance) horizontally < 0.25 m and vertically < 2 cm (interpenetrating)
            // or 2-4 cm (too close)
            {
                int pen = 0, close = 0, pairs = 0, penX = 0, pairsX = 0;
                List<Fallen> F = T.fallen;
                for (int a = 0; a < F.Count; a++)
                    for (int i = 0; i < F[a].nCloth; i++)
                    {
                        if (!F[a].active[i]) continue;
                        Vector3 p = F[a].pos[i];
                        for (int b2 = a; b2 < F.Count; b2++)
                            for (int j = (b2 == a ? i + 1 : 0); j < F[b2].nCloth; j++)
                            {
                                if (!F[b2].active[j]) continue;
                                Vector3 q = F[b2].pos[j];
                                float dx = p.x - q.x, dz = p.z - q.z;
                                if (dx * dx + dz * dz > 0.0625f) continue;
                                if (a == b2 && (F[a].rest0[i] - F[a].rest0[j]).sqrMagnitude < F[a].layerEx2) continue;
                                pairs++;
                                float dy = Mathf.Abs(p.y - q.y);
                                if (dy < 0.02f) { pen++; if (a != b2) penX++; } else if (dy < 0.04f) close++;
                                if (a != b2) pairsX++;
                            }
                    }
                Debug.Log("CHUTEGROUND interpenetration: pairs of different layers " + pairs + ", interpenetrating (<2 cm) " + pen + " (" + (100f * pen / Mathf.Max(1, pairs)).ToString("F1") + " %), too close (2-4 cm) " + close +
                          " | between canopies: " + pairsX + " pairs, interpenetrating " + penX + " (" + (100f * penX / Mathf.Max(1, pairsX)).ToString("F1") + " %)");
            }
            foreach (string l in Cfg.log) Debug.Log("CHUTEGROUND log: " + l);
            foreach (GameObject g in junk) if (g != null) UnityEngine.Object.DestroyImmediate(g);
        }
    }
}
