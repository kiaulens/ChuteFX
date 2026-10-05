// ChuteFX - parachute cloth effects for KSP 1.12 (stock ModuleParachute)
// Part 1: the open canopy flutters (mostly the hem), "breathes" and leans with the airflow (vertex deformation in the
//         bind space of the skinned mesh; lines are not deformed). On the ground it keeps its orientation from before touchdown.
// Part 2: when the canopy is cut (manually or by stock auto-cut after landing/splashdown), a copy is simulated as cloth
//         (position-based dynamics): edges do not stretch, compression/bending is soft (stiffer on the ground),
//         seams between separate pieces and between outer/inner skin only pull, lines are chains of links.
//         Air: sail effect (force normal to the surface), residual pressure, shared wind per celestial body (game time).
//         Ground: height per point by raycast.
//         Water: buoyancy, waves, soaks up water (darker), sinks; in shallow water it stays on the seabed.
//         Fixed 40 Hz step; canopies at rest are no longer simulated.
//         Cloth layers: points above another layer (another canopy, or a part far away in the deployed canopy)
//         stay at least one layer thickness above it and are carried by it in the simulation step like by the ground;
//         the canopy that landed later lies on top. With Parallax the cloth lies higher by the terrain displacement
//         (otherwise flat parts disappear in the visible ground).
//         Rendering: normals per cloth point (no hard edges at gore/UV seams), with tangents.
//         Several caps in one part (Mk16-XL): each cap is a canopy of its own with its own apex piece and its
//         own lines to the common knot.
// Cluster: open canopies of one vessel that overlap tilt apart about their attachment point (optional, off by default;
//         rendering only, undone at the end of the frame - stock always sees its own orientation). Position and size of
//         each cap come from sample vertices with the current bones (not from renderer bounds: feedback, jitter).
// The physics/drag of the stock parachute are NOT changed.
//
// Build (C# 5, .NET Framework csc) - see build.ps1 in the repository:
//   C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe -target:library -nostdlib -noconfig
//     -out:..\GameData\ChuteFX\Plugins\ChuteFX.dll  -r:<KSP>\KSP_x64_Data\Managed\{mscorlib,System,System.Core,UnityEngine,
//     UnityEngine.CoreModule,UnityEngine.PhysicsModule,UnityEngine.AnimationModule,UnityEngine.UI,
//     UnityEngine.InputLegacyModule,Assembly-CSharp}.dll
//     -codepage:65001 ChuteFX.cs
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ChuteFX
{
    internal static class Cfg
    {
        public static bool enabled = true;
        public static bool flightEffects = true;        // part 1 (flutter in flight) on/off
        public static float flutterAmplitude = 0.04f;   // fraction of the canopy radius
        public static float flutterFrequency = 6f;      // roughly Hz
        public static float breatheAmplitude = 0.03f;   // "breathing" of the canopy
        public static float breatheFrequency = 1.3f;
        public static float leanStrength = 0.35f;       // lean from sideways airflow
        public static float windSpeed = 3f;             // m/s wind for cut canopies
        public static float cutTerminalSpeed = 4f;      // sink speed of the collapsed canopy
        public static float deflateTime = 3f;           // s until fully collapsed
        public static float settleTime = 1.5f;          // s to lie down after ground contact
        public static float groundLifetime = 600f;      // s of game time (runs with time warp), 0 = until scene change
        public static float waterFloatTime = 30f;       // s floating, then sinking
        public static float sinkSpeed = 0.15f;          // m/s
        public static float maxDistance = 5000f;        // m to the active vessel
        public static int maxFallen = 30;
        public static bool recalcNormals = true;
        public static int clothIterations = 5;          // cloth stiffness (iterations)
        public static float turbulence = 6f;            // air turbulence on the cut canopy
        public static float groundFriction = 0.6f;      // 0 = slippery, 1 = sticks at once
        public static float damping = 0.02f;            // velocity damping per step
        public static int ropeSegments = 20;
        public static float bendStiffness = 0.2f;
        public static bool tetherOnLanding = true;      // cut after landing: the riser stays attached to the part
        public static float lineGray = 0.78f;           // brightness of the lines (0 = black, 1 = white)
        public static float settleMax = 25f;            // s after ground contact; at the latest then the canopy comes to rest
        public static float waterRemoveDepth = 15f;     // m below the surface a sunk canopy is removed
        public static float compressStiffness = 0.3f;
        public static float groundCompressStiffness = 0.8f;   // on the ground: compression resistance (larger folds)
        public static float groundBendStiffness = 0.6f;       // on the ground: bending stiffness (larger folds)
        public static float soakDarkness = 0.45f;       // how much darker the wet canopy gets (0 = not at all, 1 = black)
        public static int minPieceSize = 20;
        public static float sailDrag = 2.0f;
        public static float spreadPush = 5f;            // m/s^2: after landing the canopy falls away from the vessel (0 = off)
        public static float inflatePressure = 8f;      // m/s^2 residual pressure after the cut (fades out over deflateTime)
        public static float isoDrag = 0.3f;             // all-round drag independent of the surface orientation
        public static float windMin = 0.2f;             // wind strength per canopy: windSpeed * random(windMin..windMax)
        public static float windMax = 1.4f;
        public static float holdHeight = 6f;            // m above ground: below this the open canopy keeps its orientation (no tipping at touchdown)
        public static float soakDelay = 2f;             // s on the water before it visibly soaks up water
        public static float soakTime = 5f;              // s time constant of the darkening
        public static float lineWidth = 0f;             // line thickness in m, 0 = automatic
        public static bool clusterSpread = false;       // open canopies of one vessel tilt apart (off: restless with SAS/rolling)
        public static float clusterGap = 0.95f;         // distance of the canopies in the air (fraction of the sum of their radii)
        public static float clusterMaxTilt = 35f;       // degrees: at most this far a canopy tilts sideways
        public static float layerThickness = 0.05f;     // m: cloth layers on the ground stay at least this far apart (0 = off)
        public static float parallaxLift = 0.85f;       // raise the cloth on land by this fraction of the Parallax terrain displacement (0 = off)
        public static bool fadeOnHover = true;          // a cut canopy under the mouse cursor becomes semi-transparent
        public static bool fadeForKerbals = true;       // ... likewise when it hides a Kerbal (EVA) from the camera
        public static float fadeAlpha = 0.55f;          // opacity then (0 = invisible, 1 = off)
        public static int renderSmooth = 0;             // smoothing passes for rendering (deforms double-walled hems/vents, 0 = off)
        public static bool debugLog = true;

        public static void Load()
        {
            LoadParallax();
            ConfigNode[] nodes = GameDatabase.Instance.GetConfigNodes("CHUTEFX");
            if (nodes == null || nodes.Length == 0) return;
            ConfigNode n = nodes[0];
            n.TryGetValue("enabled", ref enabled);
            n.TryGetValue("flightEffects", ref flightEffects);
            n.TryGetValue("flutterAmplitude", ref flutterAmplitude);
            n.TryGetValue("flutterFrequency", ref flutterFrequency);
            n.TryGetValue("breatheAmplitude", ref breatheAmplitude);
            n.TryGetValue("breatheFrequency", ref breatheFrequency);
            n.TryGetValue("leanStrength", ref leanStrength);
            n.TryGetValue("windSpeed", ref windSpeed);
            n.TryGetValue("cutTerminalSpeed", ref cutTerminalSpeed);
            n.TryGetValue("deflateTime", ref deflateTime);
            n.TryGetValue("settleTime", ref settleTime);
            n.TryGetValue("groundLifetime", ref groundLifetime);
            n.TryGetValue("waterFloatTime", ref waterFloatTime);
            n.TryGetValue("sinkSpeed", ref sinkSpeed);
            n.TryGetValue("maxDistance", ref maxDistance);
            n.TryGetValue("maxFallen", ref maxFallen);
            n.TryGetValue("recalcNormals", ref recalcNormals);
            n.TryGetValue("clothIterations", ref clothIterations);
            n.TryGetValue("turbulence", ref turbulence);
            n.TryGetValue("groundFriction", ref groundFriction);
            n.TryGetValue("damping", ref damping);
            n.TryGetValue("ropeSegments", ref ropeSegments);
            n.TryGetValue("bendStiffness", ref bendStiffness);
            n.TryGetValue("tetherOnLanding", ref tetherOnLanding);
            n.TryGetValue("lineGray", ref lineGray);
            n.TryGetValue("settleMax", ref settleMax);
            n.TryGetValue("waterRemoveDepth", ref waterRemoveDepth);
            n.TryGetValue("minPieceSize", ref minPieceSize);
            n.TryGetValue("sailDrag", ref sailDrag);
            n.TryGetValue("spreadPush", ref spreadPush);
            n.TryGetValue("inflatePressure", ref inflatePressure);
            n.TryGetValue("isoDrag", ref isoDrag);
            n.TryGetValue("windMin", ref windMin);
            n.TryGetValue("windMax", ref windMax);
            n.TryGetValue("holdHeight", ref holdHeight);
            n.TryGetValue("soakDelay", ref soakDelay);
            n.TryGetValue("soakTime", ref soakTime);
            n.TryGetValue("soakDarkness", ref soakDarkness);
            n.TryGetValue("compressStiffness", ref compressStiffness);
            n.TryGetValue("groundCompressStiffness", ref groundCompressStiffness);
            n.TryGetValue("groundBendStiffness", ref groundBendStiffness);
            n.TryGetValue("lineWidth", ref lineWidth);
            n.TryGetValue("clusterSpread", ref clusterSpread);
            n.TryGetValue("clusterGap", ref clusterGap);
            n.TryGetValue("clusterMaxTilt", ref clusterMaxTilt);
            n.TryGetValue("layerThickness", ref layerThickness);
            n.TryGetValue("parallaxLift", ref parallaxLift);
            n.TryGetValue("fadeOnHover", ref fadeOnHover);
            n.TryGetValue("fadeForKerbals", ref fadeForKerbals);
            n.TryGetValue("fadeAlpha", ref fadeAlpha);
            n.TryGetValue("renderSmooth", ref renderSmooth);
            n.TryGetValue("debugLog", ref debugLog);
        }

        // Parallax displaces the visible terrain (tessellation + height map) against the collider the canopies lie on:
        // flat cloth parts disappeared in places in the ground. Per celestial body the largest upward displacement
        // from the Parallax settings (displacement = (map + offset) x scale, map 0..1).
        public static Dictionary<string, float> parallaxUp = new Dictionary<string, float>();
        static void LoadParallax()
        {
            parallaxUp.Clear();
            ConfigNode[] pt = GameDatabase.Instance.GetConfigNodes("ParallaxTerrain");
            if (pt == null) return;
            foreach (ConfigNode root in pt)
                foreach (ConfigNode body in root.GetNodes("Body"))
                {
                    string name = body.GetValue("name");
                    ConfigNode sp = body.GetNode("ShaderProperties");
                    if (string.IsNullOrEmpty(name) || sp == null) continue;
                    float scale = 0f, offset = -0.5f;
                    sp.TryGetValue("_DisplacementScale", ref scale);
                    sp.TryGetValue("_DisplacementOffset", ref offset);
                    float up = scale * Mathf.Max(0f, 1f + offset);
                    if (up > 0f) parallaxUp[name] = Mathf.Min(up, 2f);
                }
            if (parallaxUp.Count > 0) Log("Parallax terrain: " + parallaxUp.Count + " bodies (e.g. Kerbin " + (parallaxUp.ContainsKey("Kerbin") ? parallaxUp["Kerbin"].ToString("F2") : "-") + " m upward)");
        }

        public static void Log(string s) { if (debugLog) Debug.Log("[ChuteFX] " + s); }
    }

    // Deformable mesh with precomputed geometry relative to the canopy axis
    internal class DeformMesh
    {
        public Transform t;
        public Mesh mesh;
        public Vector3[] baseV;
        public Vector3[] work;
        public float[] h;          // height along the axis
        public Vector3[] radial;   // radial part
        public float[] ang;        // angle around the axis
        public float[] w;          // 1 = cloth (deform), 0 = line (leave unchanged)
        public Vector3 pivotL;
        public Vector3 axisL;
        public float hMax;
        public float rMax;
        public SkinnedMeshRenderer smr;
        public Renderer rend;
        // cluster: samples of the cloth (undeformed), computed in world space with the current bones
        public int[] samp;
        public Transform[] bones;           // fetched once (smr.bones creates a new array on every call)
        public Matrix4x4[] bind;
        public int[] sb;                    // bones used by the samples (index into bones)
        public Matrix4x4[] sbM;             // per frame: bone x bindpose
        public int[] sampBI;                // per sample 4 bones (index into sb) ...
        public float[] sampBW;              // ... and their weights

        public static DeformMesh Create(Transform t, Mesh src, SkinnedMeshRenderer smr, Vector3 pivotWorld, Vector3 axisWorld, Transform pivotT)
        {
            if (src == null || !src.isReadable) return null;
            DeformMesh d = new DeformMesh();
            d.t = t;
            d.smr = smr;
            d.mesh = UnityEngine.Object.Instantiate(src);
            d.mesh.MarkDynamic();
            d.baseV = d.mesh.vertices;
            d.work = new Vector3[d.baseV.Length];
            d.h = new float[d.baseV.Length];
            d.radial = new Vector3[d.baseV.Length];
            d.ang = new float[d.baseV.Length];
            d.pivotL = t.InverseTransformPoint(pivotWorld);
            d.axisL = t.InverseTransformDirection(axisWorld).normalized;
            // Skinned mesh: the vertices are in bind space; only the bones (strongly scaled when deployed) form the visible
            // canopy. So pivot/axis are determined in bind space, otherwise the deformation runs around a wrong axis and
            // the bones amplify it (distorted shape, jittering middle).
            if (smr != null && smr.bones != null && src.bindposes != null && src.bindposes.Length == smr.bones.Length)
            {
                Transform[] bones = smr.bones;
                int bi = Array.IndexOf(bones, pivotT);
                if (bi < 0)
                {
                    float bd = float.MaxValue;
                    for (int k = 0; k < bones.Length; k++)
                    {
                        if (bones[k] == null) continue;
                        float dd = (bones[k].position - pivotWorld).sqrMagnitude;
                        if (dd < bd) { bd = dd; bi = k; }
                    }
                }
                if (bi >= 0)
                {
                    Matrix4x4 ib = src.bindposes[bi].inverse;
                    Vector3 piv = ib.MultiplyPoint3x4(Vector3.zero);
                    Vector3 cen = Vector3.zero;
                    for (int k = 0; k < d.baseV.Length; k++) cen += d.baseV[k];
                    cen /= Mathf.Max(1, d.baseV.Length);
                    Vector3 ax = cen - piv;
                    if (ax.sqrMagnitude < 1e-6f) ax = ib.MultiplyVector(Vector3.up);
                    Cfg.Log("Bind space: bone=" + (bones[bi] != null ? bones[bi].name : "?") + (bones[bi] == pivotT ? "" : " (nearest)") +
                            " pivot old=" + d.pivotL.ToString("F2") + " new=" + piv.ToString("F2") + " axis old=" + d.axisL.ToString("F2") + " new=" + ax.normalized.ToString("F2"));
                    d.pivotL = piv;
                    d.axisL = ax.normalized;
                }
            }
            Vector3 u = Vector3.Cross(d.axisL, Mathf.Abs(d.axisL.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 w = Vector3.Cross(d.axisL, u);
            float hm = 0.001f, rm = 0.001f;
            for (int i = 0; i < d.baseV.Length; i++)
            {
                Vector3 p = d.baseV[i] - d.pivotL;
                float hh = Vector3.Dot(p, d.axisL);
                Vector3 r = p - d.axisL * hh;
                d.h[i] = hh;
                d.radial[i] = r;
                d.ang[i] = Mathf.Atan2(Vector3.Dot(r, w), Vector3.Dot(r, u));
                if (hh > hm) hm = hh;
                float rl = r.magnitude;
                if (rl > rm) rm = rl;
            }
            d.hMax = hm;
            // Do not deform line triangles (long and very thin): otherwise the lines shift against the original
            d.w = new float[d.baseV.Length];
            {
                Vector3 bmn = d.baseV.Length > 0 ? d.baseV[0] : Vector3.zero, bmx = bmn;
                for (int i = 0; i < d.baseV.Length; i++) { bmn = Vector3.Min(bmn, d.baseV[i]); bmx = Vector3.Max(bmx, d.baseV[i]); }
                float D = (bmx - bmn).magnitude;
                for (int s = 0; s < src.subMeshCount; s++)
                {
                    int[] tri = src.GetTriangles(s);
                    for (int k = 0; k + 2 < tri.Length; k += 3)
                    {
                        Vector3 A = d.baseV[tri[k]], B = d.baseV[tri[k + 1]], C = d.baseV[tri[k + 2]];
                        float lmax = Mathf.Max((A - B).magnitude, Mathf.Max((B - C).magnitude, (C - A).magnitude));
                        float area2 = Vector3.Cross(B - A, C - A).magnitude;
                        bool line = lmax > 0.12f * D && area2 / Mathf.Max(lmax, 1e-6f) < 0.12f * lmax;
                        if (!line) { d.w[tri[k]] = 1f; d.w[tri[k + 1]] = 1f; d.w[tri[k + 2]] = 1f; }
                    }
                }
            }
            d.rMax = rm;
            d.rend = smr != null ? (Renderer)smr : t.GetComponent<Renderer>();
            d.InitSamples(src, smr);
            if (smr != null)
            {
                smr.sharedMesh = d.mesh;
                smr.updateWhenOffscreen = true;
            }
            else
            {
                MeshFilter mf = t.GetComponent<MeshFilter>();
                if (mf != null) mf.sharedMesh = d.mesh;
            }
            return d;
        }

        // Samples for the cluster: about 64 cloth vertices (every n-th) with their bone weights
        void InitSamples(Mesh src, SkinnedMeshRenderer smr)
        {
            List<int> sl = new List<int>();
            int nCl = 0;
            for (int i = 0; i < baseV.Length; i++) if (w[i] >= 1f) nCl++;
            int stride = Mathf.Max(1, nCl / 64);
            for (int i = 0, k = 0; i < baseV.Length; i++)
                if (w[i] >= 1f) { if (k % stride == 0) sl.Add(i); k++; }
            samp = sl.ToArray();
            if (smr == null) return;
            bones = smr.bones;
            bind = src.bindposes;
            BoneWeight[] bw = src.boneWeights;
            if (bones == null || bind == null || bw == null || bw.Length != baseV.Length || bind.Length != bones.Length) { bones = null; return; }
            Dictionary<int, int> slot = new Dictionary<int, int>();
            List<int> used = new List<int>();
            sampBI = new int[samp.Length * 4];
            sampBW = new float[samp.Length * 4];
            for (int k = 0; k < samp.Length; k++)
            {
                BoneWeight q = bw[samp[k]];
                int[] bi = { q.boneIndex0, q.boneIndex1, q.boneIndex2, q.boneIndex3 };
                float[] ww = { q.weight0, q.weight1, q.weight2, q.weight3 };
                for (int j = 0; j < 4; j++)
                {
                    int s = 0;
                    if (ww[j] > 0f && bi[j] >= 0 && bi[j] < bones.Length)
                    {
                        if (!slot.TryGetValue(bi[j], out s)) { s = used.Count; slot[bi[j]] = s; used.Add(bi[j]); }
                    }
                    else ww[j] = 0f;
                    sampBI[k * 4 + j] = s;
                    sampBW[k * 4 + j] = ww[j];
                }
            }
            sb = used.ToArray();
            sbM = new Matrix4x4[sb.Length];
        }

        // Samples in world space: current bones (as Unity skins), but without the flutter deformation
        public int SampleWorld(Vector3[] outP)
        {
            int n = Mathf.Min(samp.Length, outP.Length);
            if (sb != null)
            {
                for (int b = 0; b < sb.Length; b++)
                {
                    Transform bt = bones[sb[b]];
                    sbM[b] = (bt != null ? bt.localToWorldMatrix : t.localToWorldMatrix) * bind[sb[b]];
                }
                for (int k = 0; k < n; k++)
                {
                    Vector3 v = baseV[samp[k]], p = Vector3.zero;
                    float sw = 0f;
                    for (int j = 0; j < 4; j++)
                    {
                        float ww = sampBW[k * 4 + j];
                        if (ww <= 0f) continue;
                        p += sbM[sampBI[k * 4 + j]].MultiplyPoint3x4(v) * ww;
                        sw += ww;
                    }
                    outP[k] = sw > 1e-4f ? p / sw : t.TransformPoint(v);
                }
            }
            else
            {
                Matrix4x4 m = t.localToWorldMatrix;
                for (int k = 0; k < n; k++) outP[k] = m.MultiplyPoint3x4(baseV[samp[k]]);
            }
            return n;
        }

        public void Restore()
        {
            if (mesh == null) return;
            mesh.vertices = baseV;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        public void Apply()
        {
            mesh.vertices = work;
            if (Cfg.recalcNormals) mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }
    }

    // Cut canopy as a cloth simulation
    internal class Fallen
    {
        public GameObject go;
        public List<ClothMesh> parts = new List<ClothMesh>();
        public CelestialBody body;
        public double lat, lon, alt;        // reference point (cut point)
        public Vector3[] pos, vel, prev, rest0;
        public float[] offs;
        public int[] ea, eb;
        public float[] el;
        public bool[] et;                   // true = seam/rope: only pulls, never pushes (like a thread)
        public int[] ba, bb;                // bend edges
        public float[] bl;
        public Vector3 wind;
        public bool atmo;
        public float gravity;
        public float seed;
        public ModuleParachute owner;
        public float spawnTime, contactTime, stillSince, nextGround, groundY;
        public bool contact, water, sleeping;
        public float soak, sink;
        public int groundJumps;
        public bool loggedShader;
        public bool noMemory;
        public Vector3[] nrm;
        public bool[] active;               // false = removed small pieces, not simulated
        public float[] gY;                  // ground height per point (local), -1e6 = unknown
        public int rayCursor;
        public bool dirty = true;           // mesh must be rebuilt
        public Vector3[] turb;
        public float[] area;                // cloth area per point (m^2)
        public Vector3 outDir;              // direction in which the canopy falls away from the vessel
        public float outAcc;
        public float acc;                   // time accumulator for the fixed step
        public Vector3 cenStep;
        public int apexIdx;
        public bool[] stacked;              // layer height on the ground already assigned
        public float wetTime;               // s the cloth has actually been lying in the water
        public float size;                  // size of the canopy (m)
        public float slopeX, slopeZ, groundCX, groundCZ;   // ground plane: slope and reference point
        public float seabedY = -1e6f;       // seabed (local), water only
        public int pin = -1;                // riser end, held at the part
        public Part pinPart;
        public Vector3 pinOffset;           // riser end in the part's coordinate system
        public Vector3 pinLocal;
        public float pinMaxDist;
        public MaterialPropertyBlock mpb;
        public int nCloth;                  // particles 0..nCloth-1 = cloth, then ropes
        public bool[] onGround;
        public int[][] ropes;
        public float ropeWidth;
        public Mesh ropeMesh;
        public MeshRenderer ropeR;
        public Vector3[] ropeVerts, ropeNormals;
        public float lift;                  // m: lie this far above the collider on land (Parallax raises the visible terrain)
        public double contactUT = -1.0;     // game time (UT) of touchdown: the lying time runs with time warp
        public float removeT = -1f, removeDepth;   // removal by right-click: start time of sinking, depth (m)
        // semi-transparent under the mouse cursor / in front of a Kerbal
        public float fade = 1f;
        public bool kerbalHit, fadeMat;
        public Color tint = Color.white;    // wet tint (also for the transparent material)
        public Material[][] origMats;
        public Vector3 bMin, bMax;          // bounds of the render positions (local) for the ray test
        public float[] lyF;                 // layers: support height per cloth point (local; lies on another layer), -1e6 = none
        public float[] lyP;                 // ... the one from the previous update
        public float layerEx2;              // layers: points closer than this (squared) in the deployed canopy never separate
        public float[] layerR;              // layers: horizontal search radius per cloth point (from the local mesh spacing)
        public Matrix4x4 layerM, layerMinv; // layers: local -> shared ground grid and back
        // rendering: smoothed positions and normals per cloth point
        public Vector3[] rpos;
        public int[] pvStart, pvPart, pvVert;   // per cloth point its render vertices (part, index) for the normals
        public int[] smA, smB;              // cloth edges (from the triangles, without seams) for smoothing
        public float[] smW;                 // weight of the neighbours (0 = boundary stays in place)
        public bool[] smFix;                // boundary (hem, vent)
        public float[] smX, smY, smZ;       // buffers (smoothing, normals)
    }

    internal class Chute
    {
        public ModuleParachute mod;
        public Transform canopy;
        public List<DeformMesh> meshes;
        public bool setupTried;
        public ModuleParachute.deploymentStates prev;
        public List<Renderer> hidden = new List<Renderer>();
        public float seed;
        public Vector3 lean;
        public bool deformedLastFrame;
        public bool haveLast;
        public Vector3 lastPos;
        public Quaternion lastRot;
        public Vector3 lastScale;
        public float intensity;             // smoothed flutter strength (no jump at touchdown)
        public Quaternion airRot;           // smoothed canopy orientation in flight
        public bool haveAirRot;
        // Bone pose of the last open frame: when cutting, stock moves the bones at once,
        // but the copy must take over exactly the last visible shape
        public Dictionary<SkinnedMeshRenderer, Matrix4x4[]> boneCache = new Dictionary<SkinnedMeshRenderer, Matrix4x4[]>();
        public SkinnedMeshRenderer[] smrs;      // determined once (GetComponentsInChildren/bones would create garbage every frame)
        public Transform[][] smrBones;
        public Renderer[] renderers;
        public int frameSkip;
        // cluster in the air: canopies of one vessel tilt apart (rendering only, reset at the end of the frame)
        public bool open;                   // open and set up in this frame
        public bool geomValid;
        public Vector3[] sphC;              // one sphere per cap: centre (world, untilted) ...
        public float[] sphR;                // ... and radius (m)
        public int sphN;
        public float sphM;                  // sum of r^2: the bigger canopy gives way less
        public Quaternion tilt = Quaternion.identity, tiltT = Quaternion.identity, baseRot;
        public bool tiltApplied;
        public Vector3 sP;                  // pivot (world)
        public float nextTiltLog;
    }

    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class ChuteFXController : MonoBehaviour
    {
        static FieldInfo canopyField = typeof(ModuleParachute).GetField("canopy", BindingFlags.NonPublic | BindingFlags.Instance);
        static FieldInfo lastRotField = typeof(ModuleParachute).GetField("lastRot", BindingFlags.NonPublic | BindingFlags.Instance);
        Dictionary<ModuleParachute, Chute> chutes = new Dictionary<ModuleParachute, Chute>();
        List<Fallen> fallen = new List<Fallen>();
        float nextScan;
        const int TerrainMask = 1 << 15;
        const float WeldEps = 0.004f;
        static Dictionary<long, float> sharedStack = new Dictionary<long, float>();   // stack height per ground cell, all canopies
        static int rayBudgetPerChute = 250;
        const float StackCell = 0.35f;  // m, ground cell for stacking cloth layers
        const float StackLayer = 0.025f; // m thickness per layer

        public static ChuteFXController Instance;

        void Start()
        {
            Instance = this;
            Cfg.Load();
            Cfg.Log("loaded, enabled=" + Cfg.enabled);
            StartCoroutine(EndOfFrame());
        }

        // At the end of the frame (after all cameras) undo the cluster tilt: stock keeps rotating the canopy itself and must
        // see its own orientation (otherwise the tilt builds up)
        System.Collections.IEnumerator EndOfFrame()
        {
            WaitForEndOfFrame w = new WaitForEndOfFrame();
            while (true)
            {
                yield return w;
                foreach (Chute c in chutes.Values)
                    if (c.tiltApplied) { if (c.canopy != null) c.canopy.rotation = c.baseRot; c.tiltApplied = false; }
            }
        }

        void LateUpdate()
        {
            if (!Cfg.enabled || !FlightGlobals.ready) return;
            if (Time.time >= nextScan) { Scan(); nextScan = Time.time + 1f; }

            List<ModuleParachute> dead = null;
            foreach (KeyValuePair<ModuleParachute, Chute> kv in chutes)
            {
                kv.Value.open = false;
                if (kv.Key == null || kv.Key.part == null || kv.Key.vessel == null || !kv.Key.vessel.loaded)
                {
                    if (dead == null) dead = new List<ModuleParachute>();
                    dead.Add(kv.Key);
                    continue;
                }
                try { UpdateChute(kv.Value); }
                catch (Exception e) { Cfg.Log("Error in canopy: " + e.Message); if (dead == null) dead = new List<ModuleParachute>(); dead.Add(kv.Key); }
            }
            if (dead != null) foreach (ModuleParachute m in dead) chutes.Remove(m);
            // cluster: open canopies of one vessel tilt apart, then cache the bones and deform
            if (Cfg.clusterSpread)
            {
                try { SpreadClusters(Time.deltaTime); }
                catch (Exception e) { Cfg.Log("Error while spreading the cluster: " + e.Message); }
            }
            foreach (Chute c in chutes.Values)
            {
                if (!c.open) continue;
                try { FinishChute(c); }
                catch (Exception e) { Cfg.Log("Error in canopy: " + e.Message); c.open = false; }
            }

            float dt = Time.deltaTime;
            int awake = 0;
            foreach (Fallen ff in fallen) if (!ff.sleeping) awake++;
            rayBudgetPerChute = Mathf.Max(40, 300 / Mathf.Max(1, awake));
            for (int i = fallen.Count - 1; i >= 0; i--)
            {
                bool keep = false;
                try { keep = UpdateFallen(fallen[i], dt); }
                catch (Exception e) { Cfg.Log("Error in cut canopy: " + e.Message); }
                if (!keep)
                {
                    if (fallen[i].go != null) Destroy(fallen[i].go);
                    fallen.RemoveAt(i);
                }
            }
            if (Cfg.fadeOnHover || Cfg.fadeForKerbals)
            {
                try { UpdateFades(dt); }
                catch (Exception e) { Cfg.Log("Error in transparency: " + e.Message); }
            }
            // Cloth layers every 2nd frame (the support heights stay valid until the next update); every frame while a canopy is
            // still falling or has just touched down (otherwise it plunges deep into the other one within a double step)
            if (Cfg.layerThickness > 0f && awake > 0 && ((layerTick++ & 1) == 0 || LayerImpact(fallen, Time.time)))
            {
                try { LayerCloth(); }
                catch (Exception e) { Cfg.Log("Error in cloth layers: " + e.Message); }
            }
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            sharedStack.Clear();
            foreach (Fallen f in fallen) if (f.go != null) Destroy(f.go);
            fallen.Clear();
        }

        // Cut canopies of this part (or of the whole vessel) that are not being removed yet
        public int CountRemovable(Part p, bool wholeVessel)
        {
            int n = 0;
            foreach (Fallen f in fallen) if (f.removeT < 0f && Belongs(f, p, wholeVessel)) n++;
            return n;
        }

        public int Remove(Part p, bool wholeVessel)
        {
            int n = 0;
            foreach (Fallen f in fallen)
            {
                if (f.removeT >= 0f || !Belongs(f, p, wholeVessel)) continue;
                float lo = 1e9f, hi = -1e9f;
                for (int i = 0; i < f.nCloth; i++) if (f.active[i]) { lo = Mathf.Min(lo, f.pos[i].y); hi = Mathf.Max(hi, f.pos[i].y); }
                f.removeDepth = Mathf.Max(1f, hi - lo + 0.5f);   // completely below the ground, even a tall heap
                f.removeT = Time.time;
                f.sleeping = true;                                // no more simulation, only sinking
                n++;
            }
            // wake the other canopies: if they lay on a removed one, they now fall to the ground
            if (n > 0) foreach (Fallen f in fallen) if (f.removeT < 0f) { f.sleeping = false; f.stillSince = 0f; }
            if (n > 0) Cfg.Log(n + " canopy(ies) removed by right-click (" + p.partInfo.name + (wholeVessel ? ", whole vessel)" : ")"));
            return n;
        }

        static bool Belongs(Fallen f, Part p, bool wholeVessel)
        {
            if (f.owner == null || f.owner.part == null || p == null) return false;
            return wholeVessel ? f.owner.part.vessel == p.vessel : f.owner.part == p;
        }

        // ---------- Semi-transparent: under the mouse cursor or in front of a Kerbal ----------
        // The cloth has no collider: the ray is tested against its triangles directly (first against the bounds).
        // While it is transparent, the canopy gets a copy of its material with KSP/Alpha/Translucent Specular.
        float nextKerbal;
        List<Vector3> kerbalPos = new List<Vector3>();
        static Dictionary<Material, Material> fadeMats = new Dictionary<Material, Material>();
        static Shader fadeShader;
        static bool fadeShaderTried;

        void UpdateFades(float dt)
        {
            if (fallen.Count == 0) return;
            Camera cam = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : null;
            if (cam == null) return;
            Vector3 camPos = cam.transform.position;
            Ray mray = cam.ScreenPointToRay(Input.mousePosition);
            bool kerbalCheck = Cfg.fadeForKerbals && Time.time >= nextKerbal;
            if (kerbalCheck)
            {
                nextKerbal = Time.time + 0.2f;
                kerbalPos.Clear();
                List<Vessel> vs = FlightGlobals.VesselsLoaded;
                if (vs != null)
                    foreach (Vessel v in vs)
                        if (v != null && v.isEVA && (v.transform.position - camPos).sqrMagnitude < 300f * 300f) kerbalPos.Add(v.transform.position);
            }
            foreach (Fallen f in fallen)
            {
                if (f.go == null || f.rpos == null) continue;
                bool hover = Cfg.fadeOnHover && RayHitsCloth(f, mray.origin, mray.direction, 3000f);
                if (kerbalCheck)
                {
                    f.kerbalHit = false;
                    foreach (Vector3 k in kerbalPos)
                    {
                        Vector3 d = k - camPos;
                        float dist = d.magnitude;
                        if (dist > 0.5f && RayHitsCloth(f, camPos, d / dist, dist - 0.4f)) { f.kerbalHit = true; break; }
                    }
                }
                bool want = hover || (Cfg.fadeForKerbals && f.kerbalHit);
                // becoming transparent is smooth, becoming opaque again is instant (cursor off the canopy = normal at once)
                f.fade = want ? Mathf.MoveTowards(f.fade, Mathf.Clamp01(Cfg.fadeAlpha), dt * 4f) : 1f;
                ApplyFade(f);
            }
        }

        // Ray (world) against the drawn cloth triangles; maxDist in m
        static bool RayHitsCloth(Fallen f, Vector3 origin, Vector3 dir, float maxDist)
        {
            Matrix4x4 w2l = f.go.transform.worldToLocalMatrix;
            Vector3 o = w2l.MultiplyPoint3x4(origin), d = w2l.MultiplyVector(dir);
            float t0 = 0f, t1 = maxDist;
            for (int ax = 0; ax < 3; ax++)
            {
                float lo = f.bMin[ax] - 0.1f, hi = f.bMax[ax] + 0.1f, oa = o[ax], da = d[ax];
                if (Mathf.Abs(da) < 1e-8f) { if (oa < lo || oa > hi) return false; continue; }
                float ta = (lo - oa) / da, tb = (hi - oa) / da;
                if (ta > tb) { float tt = ta; ta = tb; tb = tt; }
                if (ta > t0) t0 = ta;
                if (tb < t1) t1 = tb;
                if (t0 > t1) return false;
            }
            Vector3[] r = f.rpos;
            foreach (ClothMesh cm in f.parts)
            {
                int[] t = cm.tri, map = cm.map;
                for (int k = 0; k + 2 < t.Length; k += 3)
                {
                    Vector3 a = r[map[t[k]]], b = r[map[t[k + 1]]], c = r[map[t[k + 2]]];
                    float e1x = b.x - a.x, e1y = b.y - a.y, e1z = b.z - a.z, e2x = c.x - a.x, e2y = c.y - a.y, e2z = c.z - a.z;
                    float px = d.y * e2z - d.z * e2y, py = d.z * e2x - d.x * e2z, pz = d.x * e2y - d.y * e2x;
                    float det = e1x * px + e1y * py + e1z * pz;
                    if (det > -1e-9f && det < 1e-9f) continue;
                    float inv = 1f / det;
                    float sx = o.x - a.x, sy = o.y - a.y, sz = o.z - a.z;
                    float u = (sx * px + sy * py + sz * pz) * inv;
                    if (u < 0f || u > 1f) continue;
                    float qx = sy * e1z - sz * e1y, qy = sz * e1x - sx * e1z, qz = sx * e1y - sy * e1x;
                    float v = (d.x * qx + d.y * qy + d.z * qz) * inv;
                    if (v < 0f || u + v > 1f) continue;
                    float hitT = (e2x * qx + e2y * qy + e2z * qz) * inv;
                    if (hitT > 0f && hitT < maxDist) return true;
                }
            }
            return false;
        }

        static Material FadeMaterial(Material src)
        {
            if (src == null) return null;
            Material m;
            if (fadeMats.TryGetValue(src, out m) && m != null) return m;
            if (!fadeShaderTried)
            {
                fadeShaderTried = true;
                fadeShader = Shader.Find("KSP/Alpha/Translucent Specular");
                if (fadeShader == null) fadeShader = Shader.Find("KSP/Alpha/Translucent");
                Cfg.Log("Semi-transparent: shader=" + (fadeShader != null ? fadeShader.name : "keiner gefunden - aus"));
            }
            if (fadeShader == null) return null;
            m = new Material(fadeShader);
            m.name = "ChuteFX_Fade";
            if (src.HasProperty("_MainTex") && m.HasProperty("_MainTex"))
            {
                m.mainTexture = src.mainTexture;
                m.mainTextureScale = src.mainTextureScale;
                m.mainTextureOffset = src.mainTextureOffset;
            }
            if (src.HasProperty("_Color") && m.HasProperty("_Color")) m.color = src.color;
            if (src.HasProperty("_SpecColor") && m.HasProperty("_SpecColor")) m.SetColor("_SpecColor", src.GetColor("_SpecColor"));
            if (src.HasProperty("_Shininess") && m.HasProperty("_Shininess")) m.SetFloat("_Shininess", src.GetFloat("_Shininess"));
            fadeMats[src] = m;
            return m;
        }

        static void ApplyFade(Fallen f)
        {
            bool want = f.fade < 0.995f;
            if (want && !f.fadeMat)
            {
                Material[][] orig = new Material[f.parts.Count][];
                Material[][] fm = new Material[f.parts.Count][];
                for (int i = 0; i < f.parts.Count; i++)
                {
                    Material[] om = f.parts[i].r.sharedMaterials;
                    orig[i] = om;
                    fm[i] = new Material[om.Length];
                    for (int j = 0; j < om.Length; j++) { fm[i][j] = FadeMaterial(om[j]); if (fm[i][j] == null) return; }
                }
                for (int i = 0; i < f.parts.Count; i++) f.parts[i].r.sharedMaterials = fm[i];
                f.origMats = orig;
                f.fadeMat = true;
            }
            else if (!want && f.fadeMat)
            {
                for (int i = 0; i < f.parts.Count && i < f.origMats.Length; i++)
                {
                    f.parts[i].r.sharedMaterials = f.origMats[i];
                    if (!f.water) f.parts[i].r.SetPropertyBlock(null);   // in water the wet tint sets the block again next frame
                }
                f.fadeMat = false;
                return;
            }
            if (!f.fadeMat) return;
            if (f.mpb == null) f.mpb = new MaterialPropertyBlock();
            foreach (ClothMesh cm in f.parts)
            {
                Material sm = cm.r.sharedMaterial;
                Color c = sm != null && sm.HasProperty("_Color") ? sm.color : Color.white;
                cm.r.GetPropertyBlock(f.mpb);
                f.mpb.SetColor("_Color", new Color(c.r * f.tint.r, c.g * f.tint.g, c.b * f.tint.b, f.fade));
                cm.r.SetPropertyBlock(f.mpb);
            }
        }

        void Scan()
        {
            List<Vessel> vs = FlightGlobals.VesselsLoaded;
            if (vs == null) return;
            for (int i = 0; i < vs.Count; i++)
            {
                Vessel v = vs[i];
                if (v == null || v.parts == null) continue;
                for (int p = 0; p < v.parts.Count; p++)
                {
                    List<ModuleParachute> mods = v.parts[p].FindModulesImplementing<ModuleParachute>();
                    for (int m = 0; m < mods.Count; m++)
                    {
                        if (chutes.ContainsKey(mods[m])) continue;
                        Chute c = new Chute();
                        c.mod = mods[m];
                        c.prev = mods[m].deploymentState;
                        c.seed = UnityEngine.Random.Range(0f, 100f);
                        chutes[mods[m]] = c;
                    }
                }
            }
        }

        Transform GetCanopy(ModuleParachute m)
        {
            Transform t = null;
            if (canopyField != null) t = canopyField.GetValue(m) as Transform;
            if (t == null && !string.IsNullOrEmpty(m.canopyName)) t = m.part.FindModelTransform(m.canopyName);
            return t;
        }

        static Vector3 AxisFromRenderers(Transform canopy)
        {
            return AxisFromRenderers(canopy, canopy.GetComponentsInChildren<Renderer>(true));
        }

        static Vector3 AxisFromRenderers(Transform canopy, Renderer[] rs)
        {
            Bounds b = new Bounds();
            bool first = true;
            foreach (Renderer r in rs)
            {
                if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
            }
            Vector3 a = first ? canopy.up : (b.center - canopy.position);
            if (a.sqrMagnitude < 1e-4f) a = canopy.up;
            return a.normalized;
        }

        static List<DeformMesh> BuildMeshes(Transform root, Vector3 pivotWorld, Vector3 axisWorld)
        {
            List<DeformMesh> list = new List<DeformMesh>();
            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                Renderer mr = mf.GetComponent<Renderer>();
                if (mr != null && IsLineRenderer(mr, mf.sharedMesh)) continue;   // do not deform the riser/line mesh
                DeformMesh d = DeformMesh.Create(mf.transform, mf.sharedMesh, null, pivotWorld, axisWorld, root);
                if (d != null) list.Add(d);
            }
            foreach (SkinnedMeshRenderer smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (IsLineRenderer(smr, smr.sharedMesh)) continue;
                DeformMesh d = DeformMesh.Create(smr.transform, smr.sharedMesh, smr, pivotWorld, axisWorld, root);
                if (d != null) list.Add(d);
            }
            return list;
        }

        void UpdateChute(Chute c)
        {
            ModuleParachute m = c.mod;
            ModuleParachute.deploymentStates s = m.deploymentState;
            if (c.canopy == null) c.canopy = GetCanopy(m);
            if (c.canopy == null) { c.prev = s; return; }
            // Cluster tilt from the last frame still applied (end-of-frame reset missed)? Then take the stock orientation:
            // stock rotates the canopy only in the physics step and remembers its own orientation (lastRot)
            if (c.tiltApplied)
            {
                c.tiltApplied = false;
                if (lastRotField != null) c.canopy.rotation = (Quaternion)lastRotField.GetValue(m);
            }

            // state change
            if (s != c.prev)
            {
                if (s == ModuleParachute.deploymentStates.CUT &&
                    (c.prev == ModuleParachute.deploymentStates.DEPLOYED || c.prev == ModuleParachute.deploymentStates.SEMIDEPLOYED))
                {
                    SpawnFallen(c);
                }
                if (c.prev == ModuleParachute.deploymentStates.CUT && s != ModuleParachute.deploymentStates.CUT)
                {
                    // Repacked: original visible again, remove the canopy left behind
                    foreach (Renderer r in c.hidden) if (r != null) r.enabled = true;
                    c.hidden.Clear();
                    for (int i = fallen.Count - 1; i >= 0; i--)
                        if (fallen[i].owner == m) { if (fallen[i].go != null) Destroy(fallen[i].go); fallen.RemoveAt(i); }
                }
                c.prev = s;
            }

            // keep the original invisible during CUT (the copy takes over)
            if (s == ModuleParachute.deploymentStates.CUT)
            {
                foreach (Renderer r in c.hidden) if (r != null && r.enabled) r.enabled = false;
                if (c.deformedLastFrame) { RestoreMeshes(c); }
                return;
            }

            bool open = s == ModuleParachute.deploymentStates.DEPLOYED || s == ModuleParachute.deploymentStates.SEMIDEPLOYED;
            if (!open)
            {
                if (c.deformedLastFrame) RestoreMeshes(c);
                return;
            }

            if (!c.setupTried)
            {
                c.setupTried = true;
                Vector3 axis = AxisFromRenderers(c.canopy);
                c.meshes = BuildMeshes(c.canopy, c.canopy.position, axis);
                int verts = 0; foreach (DeformMesh d in c.meshes) verts += d.baseV.Length;
                Cfg.Log("Canopy set up: " + m.part.partInfo.name + " canopy=" + c.canopy.name + " meshes=" + c.meshes.Count + " verts=" + verts);
            }
            // remember the last visible pose (in case stock hides/moves the canopy at once when cutting)
            // On the ground stock rotates the open canopy according to the (random) residual motion: it tipped sideways and
            // back abruptly at touchdown. So the orientation from shortly before touchdown is held.
            Vessel mv = m.vessel;
            bool nearLand = !mv.Splashed && (mv.Landed || (mv.heightFromTerrain >= 0f && mv.heightFromTerrain < Cfg.holdHeight));
            if (!nearLand)   // land only: on water the touchdown already looked right
            {
                c.airRot = c.haveAirRot ? Quaternion.Slerp(c.airRot, c.canopy.rotation, Mathf.Clamp01(Time.deltaTime * 4f)) : c.canopy.rotation;
                c.haveAirRot = true;
            }
            else if (c.haveAirRot && Cfg.flightEffects) c.canopy.rotation = c.airRot;
            c.haveLast = true;
            c.lastPos = c.canopy.position;
            c.lastRot = c.canopy.rotation;
            c.lastScale = c.canopy.lossyScale;
            c.baseRot = c.canopy.rotation;
            c.open = true;
            if (Cfg.clusterSpread) UpdateClusterGeometry(c);
        }

        // Cache the bone pose (after the cluster tilt: a cut copy takes over exactly the visible picture)
        // and deform the open canopy
        void FinishChute(Chute c)
        {
            ModuleParachute m = c.mod;
            ModuleParachute.deploymentStates s = m.deploymentState;
            if (c.smrs == null)
            {
                c.smrs = c.canopy.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                c.smrBones = new Transform[c.smrs.Length][];
                for (int k = 0; k < c.smrs.Length; k++) c.smrBones[k] = c.smrs[k].bones;
                c.renderers = c.canopy.GetComponentsInChildren<Renderer>(true);
            }
            for (int k = 0; k < c.smrs.Length; k++)
            {
                SkinnedMeshRenderer smr = c.smrs[k];
                Transform[] bones = c.smrBones[k];
                if (smr == null || bones == null) continue;
                Matrix4x4[] arr;
                if (!c.boneCache.TryGetValue(smr, out arr) || arr.Length != bones.Length) { arr = new Matrix4x4[bones.Length]; c.boneCache[smr] = arr; }
                for (int b = 0; b < bones.Length; b++) arr[b] = bones[b] != null ? bones[b].localToWorldMatrix : smr.transform.localToWorldMatrix;
            }
            if (c.meshes == null || c.meshes.Count == 0) return;

            // deform only near the camera
            Camera cam = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : null;
            // distance LOD: every frame when close, every 3rd frame up to 1 km, not at all farther away
            float camD2 = cam != null ? (cam.transform.position - c.canopy.position).sqrMagnitude : 0f;
            if (camD2 > 1e6f) return;
            if (camD2 > 9e4f && (++c.frameSkip % 3) != 0) return;

            // sideways airflow -> lean
            Vessel v = m.vessel;
            Vector3 axisW = AxisFromRenderers(c.canopy, c.renderers);
            Vector3 air = -(Vector3)v.srf_velocity;
            Vector3 lat = air - axisW * Vector3.Dot(air, axisW);
            float spd = (float)v.srf_velocity.magnitude;
            float t = Time.time;
            // some "wind": a slowly turning lateral impulse
            float wAng = Mathf.PerlinNoise(t * 0.05f, c.seed) * Mathf.PI * 4f;
            Vector3 perp1 = Vector3.Cross(axisW, Vector3.up); if (perp1.sqrMagnitude < 1e-4f) perp1 = Vector3.Cross(axisW, Vector3.right);
            perp1.Normalize();
            Vector3 perp2 = Vector3.Cross(axisW, perp1);
            Vector3 gust = (perp1 * Mathf.Cos(wAng) + perp2 * Mathf.Sin(wAng)) * (Mathf.PerlinNoise(t * 0.4f, c.seed + 7f) * 0.6f);
            Vector3 leanTarget = lat / (spd + 1f) + gust * 0.15f;
            leanTarget = Vector3.ClampMagnitude(leanTarget, 0.6f) * Cfg.leanStrength;
            if (v.LandedOrSplashed || !Cfg.flightEffects) leanTarget = Vector3.zero;   // no lean on the ground (the canopy slowly wandered off otherwise)
            c.lean = Vector3.Lerp(c.lean, leanTarget, Mathf.Clamp01(Time.deltaTime * 1.5f));

            float q = Mathf.Clamp((float)v.dynamicPressurekPa / 1.5f, v.LandedOrSplashed ? 0f : 0.25f, 1.2f);
            if (!Cfg.flightEffects) q = 0f;
            float semi = s == ModuleParachute.deploymentStates.SEMIDEPLOYED ? 0.5f : 1f;
            c.intensity = Mathf.MoveTowards(c.intensity, q * semi, Time.deltaTime * 0.5f);
            foreach (DeformMesh d in c.meshes) DeformFlying(d, c.canopy.position, c.lean, t, c.seed, c.intensity);
            c.deformedLastFrame = true;
        }

        // One sphere per cap (centre and radius of the cloth) in the current, untilted pose: from sample vertices, skinned
        // here with the current bones. (Before: from the renderer bounds, converted back with the tilted pose of the last
        // frame - but Unity already returns the bounds for the current pose. The tilt entered twice,
        // built up to the limit and the canopies jittered in flight.)
        static Vector3[] sampBuf = new Vector3[128];
        void UpdateClusterGeometry(Chute c)
        {
            c.geomValid = false;
            c.sphN = 0;
            if (c.meshes == null || c.meshes.Count == 0) return;
            if (c.sphC == null || c.sphC.Length < c.meshes.Count) { c.sphC = new Vector3[c.meshes.Count]; c.sphR = new float[c.meshes.Count]; }
            float mass = 0f;
            foreach (DeformMesh d in c.meshes)
            {
                if (d.samp == null || d.samp.Length < 4 || d.rend == null || !d.rend.enabled) continue;
                int n = d.SampleWorld(sampBuf);
                Vector3 cen = Vector3.zero;
                for (int k = 0; k < n; k++) cen += sampBuf[k];
                cen /= n;
                float r2 = 0f;
                for (int k = 0; k < n; k++) { float q = (sampBuf[k] - cen).sqrMagnitude; if (q > r2) r2 = q; }
                float r = Mathf.Sqrt(r2);
                if (r < 0.2f || float.IsNaN(cen.x)) continue;
                c.sphC[c.sphN] = cen;
                c.sphR[c.sphN] = r;
                c.sphN++;
                mass += r * r;
            }
            c.sphM = mass;
            c.geomValid = c.sphN > 0;
        }

        // Cluster in the air: when the open canopies of one vessel overlap, each tilts about its attachment point away
        // from the other (like real clusters pushing each other apart). Rendering only: at the end of the frame the
        // tilt is undone; the vessel's physics stay unchanged.
        List<Chute> spreadList = new List<Chute>();
        int tiltLogs;
        void SpreadClusters(float dt)
        {
            spreadList.Clear();
            foreach (Chute c in chutes.Values)
            {
                if (c.open && c.geomValid && c.canopy != null) spreadList.Add(c);
                else if (!c.open) { c.tilt = Quaternion.identity; c.tiltT = Quaternion.identity; }
            }
            int n = spreadList.Count;
            if (n == 0) return;
            for (int i = 0; i < n; i++)
            {
                Chute c = spreadList[i];
                c.sP = c.canopy.position;
                c.tiltT = Quaternion.identity;
            }
            // a few passes: every overlapping sphere pair of two canopies tilts both apart (sphere = one cap;
            // the Mk16-XL has three on one pivot)
            for (int it = 0; it < 4; it++)
                for (int i = 0; i < n; i++)
                    for (int j = i + 1; j < n; j++)
                    {
                        Chute a = spreadList[i], b = spreadList[j];
                        if (a.mod.vessel != b.mod.vessel) continue;
                        // the bigger canopy gives way less (it is steadier in the air; in a cluster the big one stays in the middle)
                        float wa = b.sphM / Mathf.Max(a.sphM + b.sphM, 1e-4f);
                        for (int u = 0; u < a.sphN; u++)
                            for (int w = 0; w < b.sphN; w++)
                            {
                                Vector3 qa = a.sP + a.tiltT * (a.sphC[u] - a.sP), qb = b.sP + b.tiltT * (b.sphC[w] - b.sP);
                                Vector3 d = qa - qb;
                                float dist = d.magnitude, need = (a.sphR[u] + b.sphR[w]) * Cfg.clusterGap;
                                if (dist >= need) continue;
                                Vector3 dir;
                                if (dist > 1e-3f) dir = d / dist;
                                else { dir = Vector3.Cross(qa - a.sP, Vector3.up); if (dir.sqrMagnitude < 1e-6f) dir = Vector3.right; dir.Normalize(); }
                                TiltAway(ref a.tiltT, a.sP, qa, dir, (need - dist) * wa);
                                TiltAway(ref b.tiltT, b.sP, qb, -dir, (need - dist) * (1f - wa));
                            }
                    }
            // follow smoothly and with limited turn rate (no jerk when another canopy opens)
            float k = 1f - Mathf.Exp(-Mathf.Max(dt, 0f) * 2.5f);
            float maxStep = 20f * Mathf.Max(dt, 0f);
            for (int i = 0; i < n; i++)
            {
                Chute c = spreadList[i];
                float ang; Vector3 ax;
                c.tiltT.ToAngleAxis(out ang, out ax);
                if (ang > 180f) ang -= 360f;
                if (Mathf.Abs(ang) > Cfg.clusterMaxTilt) c.tiltT = Quaternion.AngleAxis(Mathf.Sign(ang) * Cfg.clusterMaxTilt, ax);
                c.tilt = Quaternion.RotateTowards(c.tilt, Quaternion.Slerp(c.tilt, c.tiltT, k), maxStep);
                if (Cfg.debugLog && tiltLogs < 40 && Time.time >= c.nextTiltLog && Quaternion.Angle(c.tiltT, Quaternion.identity) > 1f)
                {
                    c.nextTiltLog = Time.time + 3f;
                    tiltLogs++;
                    Cfg.Log("Cluster: " + c.mod.part.partInfo.name + " caps=" + c.sphN + " r=" + c.sphR[0].ToString("F1") + " m, tilt " +
                            Quaternion.Angle(c.tilt, Quaternion.identity).ToString("F1") + " deg (target " + Quaternion.Angle(c.tiltT, Quaternion.identity).ToString("F1") + ")");
                }
                if (Quaternion.Angle(c.tilt, Quaternion.identity) < 0.05f) continue;
                c.canopy.rotation = c.tilt * c.baseRot;
                c.tiltApplied = true;
            }
        }

        static void TiltAway(ref Quaternion T, Vector3 pivot, Vector3 center, Vector3 dir, float dist)
        {
            Vector3 arm = center - pivot;
            float L = arm.magnitude;
            if (L < 0.1f) return;
            Vector3 ax = Vector3.Cross(arm, dir);
            if (ax.sqrMagnitude < 1e-8f) return;
            T = Quaternion.AngleAxis(Mathf.Min(dist / L, 0.3f) * Mathf.Rad2Deg, ax.normalized) * T;
        }

        void RestoreMeshes(Chute c)
        {
            if (c.meshes != null) foreach (DeformMesh d in c.meshes) d.Restore();
            c.deformedLastFrame = false;
        }

        static void DeformFlying(DeformMesh d, Vector3 pivotWorld, Vector3 leanWorld, float t, float seed, float intensity)
        {
            // More realistic: the hem breathes and flutters, the dome stays calmer,
            // a fine ripple runs between the gores, waves travel around the rim.
            Vector3 leanL = d.t.InverseTransformDirection(leanWorld) * d.hMax;
            float A = Cfg.flutterAmplitude * intensity;
            float B = Cfg.breatheAmplitude * intensity;
            float f = Cfg.flutterFrequency;
            float breathe = Mathf.Sin(Cfg.breatheFrequency * t * Mathf.PI + seed) * 0.7f
                          + Mathf.Sin(Cfg.breatheFrequency * 2.3f * t + seed * 2f) * 0.3f;
            float gust = Mathf.PerlinNoise(t * 0.6f, seed);
            float gores = 16f;
            for (int i = 0; i < d.baseV.Length; i++)
            {
                float hn = Mathf.Clamp01(d.h[i] / d.hMax);
                float rn = Mathf.Clamp01(d.radial[i].magnitude / d.rMax);
                float rim = rn * rn;
                float a = d.ang[i];
                float travel = Mathf.Sin(3f * a - f * 0.55f * t + seed) * 0.55f + Mathf.Sin(5f * a + f * 0.8f * t + seed * 1.7f) * 0.45f;
                float ripple = Mathf.Sin(gores * a + f * 1.9f * t + 7f * hn + seed) * 0.35f;
                float rs = 1f + B * breathe * rim
                              + A * (0.6f + 0.8f * gust) * (travel * rim + ripple * rn * 0.5f);
                // dome: slight up and down
                float apex = A * 0.3f * Mathf.Sin(f * 0.9f * t + 2f * a + seed) * (1f - rn) * hn;
                Vector3 p = d.pivotL + d.axisL * (d.h[i] * (1f + apex)) + d.radial[i] * rs + leanL * (hn * hn);
                d.work[i] = d.w[i] >= 1f ? p : Vector3.LerpUnclamped(d.baseV[i], p, d.w[i]);
            }
            d.Apply();
        }

        // ---------- Part 2: cut canopies as a cloth simulation ----------
        // Position-based dynamics: every (welded) vertex is a particle, edges keep their length.
        // Separate pieces of the canopy are stitched at their nearest points. The model's lines
        // (end points only, not bendable) are replaced by simulated ropes made of many links.
        // Local coordinate system at the cut point: x/z horizontal, y = up (metres).

        static Quaternion SurfaceFrame(CelestialBody b, double lat, double lon)
        {
            Vector3d up = b.GetSurfaceNVector(lat, lon);
            Vector3d axis = (Vector3d)b.transform.up;
            Vector3d north = axis - up * Vector3d.Dot(axis, up);
            if (north.sqrMagnitude < 1e-9) north = (Vector3d)b.transform.forward;
            return Quaternion.LookRotation((Vector3)north.normalized, (Vector3)up);
        }

        // World coordinates of the vertices (skinning computed from the bones)
        static Vector3[] WorldVertices(Renderer r, Mesh m, Matrix4x4[] cached)
        {
            Vector3[] v = m.vertices;
            Vector3[] w = new Vector3[v.Length];
            SkinnedMeshRenderer smr = r as SkinnedMeshRenderer;
            BoneWeight[] bw = smr != null ? m.boneWeights : null;
            if (smr != null && smr.bones != null && smr.bones.Length > 0 && bw != null && bw.Length == v.Length)
            {
                Transform[] bones = smr.bones;
                Matrix4x4[] bind = m.bindposes;
                Matrix4x4[] mats = new Matrix4x4[bones.Length];
                for (int b = 0; b < bones.Length; b++)
                    mats[b] = (cached != null && b < cached.Length ? cached[b] : (bones[b] != null ? bones[b].localToWorldMatrix : smr.transform.localToWorldMatrix)) * (b < bind.Length ? bind[b] : Matrix4x4.identity);
                for (int i = 0; i < v.Length; i++)
                {
                    BoneWeight q = bw[i];
                    Vector3 p = Vector3.zero;
                    if (q.weight0 > 0f) p += mats[q.boneIndex0].MultiplyPoint3x4(v[i]) * q.weight0;
                    if (q.weight1 > 0f) p += mats[q.boneIndex1].MultiplyPoint3x4(v[i]) * q.weight1;
                    if (q.weight2 > 0f) p += mats[q.boneIndex2].MultiplyPoint3x4(v[i]) * q.weight2;
                    if (q.weight3 > 0f) p += mats[q.boneIndex3].MultiplyPoint3x4(v[i]) * q.weight3;
                    float sw = q.weight0 + q.weight1 + q.weight2 + q.weight3;
                    w[i] = sw > 0.001f ? p / sw : smr.transform.TransformPoint(v[i]);
                }
            }
            else
            {
                Transform t = r.transform;
                for (int i = 0; i < v.Length; i++) w[i] = t.TransformPoint(v[i]);
            }
            return w;
        }

        static Dictionary<Material, Material> ropeMats = new Dictionary<Material, Material>();
        // Stock lines are thin light-grey lines: the original's shader, but without the canopy texture
        static Material RopeMaterial(Material src)
        {
            Material m;
            if (src == null) return null;
            if (ropeMats.TryGetValue(src, out m) && m != null) return m;
            m = new Material(src);
            m.name = "ChuteFX_Rope";
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", Texture2D.whiteTexture);
            if (m.HasProperty("_BumpMap")) m.SetTexture("_BumpMap", null);
            float gr = Cfg.lineGray;
            if (m.HasProperty("_Color")) m.SetColor("_Color", new Color(gr, gr, gr * 0.97f, 1f));
            if (m.HasProperty("_SpecColor")) m.SetColor("_SpecColor", new Color(0.1f, 0.1f, 0.1f, 1f));
            ropeMats[src] = m;
            return m;
        }

        static bool IsLineRenderer(Renderer r, Mesh m)
        {
            string n = (r.name + " " + (m != null ? m.name : "")).ToLowerInvariant();
            return n.Contains("connector") || n.Contains("wire") || n.Contains("line") || n.Contains("cord") ||
                   n.Contains("rope") || n.Contains("shroud") || n.Contains("riser") || n.Contains("string");
        }

        void SpawnFallen(Chute c)
        {
            if (fallen.Count >= Cfg.maxFallen) { if (fallen[0].go != null) Destroy(fallen[0].go); fallen.RemoveAt(0); }
            Transform src = c.canopy;
            Vessel v = c.mod.vessel;
            CelestialBody b = v.mainBody;
            if (c.deformedLastFrame) RestoreMeshes(c);

            Vector3 pivotW = c.haveLast ? c.lastPos : src.position;
            Fallen f = new Fallen();
            f.owner = c.mod;
            f.body = b;
            f.seed = c.seed + UnityEngine.Random.Range(0f, 50f);
            Vector3d pd = (Vector3d)pivotW;
            f.lat = b.GetLatitude(pd);
            f.lon = b.GetLongitude(pd);
            f.alt = b.GetAltitude(pd);
            Quaternion frame = SurfaceFrame(b, f.lat, f.lon);
            Quaternion inv = Quaternion.Inverse(frame);

            f.go = new GameObject("ChuteFX_Cloth");
            f.go.transform.position = pivotW;
            f.go.transform.rotation = frame;

            Vector3 vLocal = inv * (Vector3)v.srf_velocity;
            f.wind = b.atmosphere ? SharedWind(b, Planetarium.GetUniversalTime()) : Vector3.zero;
            f.atmo = b.atmosphere;
            f.gravity = (float)(9.81 * b.GeeASL);
            float pUp;
            f.lift = (Cfg.parallaxLift > 0f && Cfg.parallaxUp.TryGetValue(b.bodyName, out pUp)) ? pUp * Cfg.parallaxLift : 0f;

            List<Vector3> P = new List<Vector3>();
            Dictionary<Vector3Int, List<int>> weld = new Dictionary<Vector3Int, List<int>>();
            Dictionary<long, float> edges = new Dictionary<long, float>();
            List<Vector3> linePts = new List<Vector3>();      // points from separate line meshes (riser to the part)
            List<Vector3> meshLinePts = new List<Vector3>();  // points of the line triangles in the canopy mesh
            List<int> meshLineMesh = new List<int>();         // ... and which cloth mesh they come from (knot per canopy)
            List<bool> used = new List<bool>();
            List<float> pArea = new List<float>();   // cloth area per point (for the stack height)
            Dictionary<long, float> bends = new Dictionary<long, float>();
            Dictionary<long, int> opp = new Dictionary<long, int>();
            int lineTris = 0;
            int bindMerged = 0;
            List<List<List<int>>> pendingTris = new List<List<List<int>>>();
            Material[] lineMats = null;
            int lineLayer = 0;
            string names = "";

            List<Renderer> rlist = new List<Renderer>(src.GetComponentsInChildren<Renderer>(true));
            foreach (Renderer r in c.mod.part.GetComponentsInChildren<Renderer>(false))
            {
                if (rlist.Contains(r) || !r.enabled) continue;
                Mesh lm = null;
                SkinnedMeshRenderer ls = r as SkinnedMeshRenderer;
                if (ls != null) lm = ls.sharedMesh; else { MeshFilter lf = r.GetComponent<MeshFilter>(); if (lf != null) lm = lf.sharedMesh; }
                if (IsLineRenderer(r, lm)) rlist.Add(r);
            }
            foreach (Renderer r in rlist)
            {
                Mesh m = null;
                SkinnedMeshRenderer smr = r as SkinnedMeshRenderer;
                if (smr != null) m = smr.sharedMesh;
                else { MeshFilter mf = r.GetComponent<MeshFilter>(); if (mf != null) m = mf.sharedMesh; }
                if (m == null || !m.isReadable) continue;
                Matrix4x4[] cachedMats = null;
                SkinnedMeshRenderer csm = r as SkinnedMeshRenderer;
                if (csm != null) c.boneCache.TryGetValue(csm, out cachedMats);
                Vector3[] wv = WorldVertices(r, m, cachedMats);
                bool isLine = IsLineRenderer(r, m);
                names += r.name + (isLine ? "(lines) " : "(cloth) ");
                if (isLine)
                {
                    for (int i = 0; i < wv.Length; i++) linePts.Add(inv * (wv[i] - pivotW));
                    lineMats = r.sharedMaterials;
                    lineLayer = r.gameObject.layer;
                    continue;
                }

                int[] map = new int[wv.Length];
                // Seam duplicates in the model (same position in the model but different bone weights) end up to a few cm
                // apart after skinning -> they were not welded, giving tears/flaps on the red drogue.
                // So first weld by model position (exact duplicates only, at most 0.5 m apart in the world).
                Vector3[] bindV = m.vertices;
                Dictionary<Vector3Int, int> bindWeld = new Dictionary<Vector3Int, int>();
                for (int i = 0; i < wv.Length; i++)
                {
                    Vector3 lp = inv * (wv[i] - pivotW);
                    Vector3Int bk = new Vector3Int(Mathf.RoundToInt(bindV[i].x * 20000f), Mathf.RoundToInt(bindV[i].y * 20000f), Mathf.RoundToInt(bindV[i].z * 20000f));
                    int bidx;
                    if (bindWeld.TryGetValue(bk, out bidx) && (P[bidx] - lp).sqrMagnitude < 0.25f) { map[i] = bidx; bindMerged++; continue; }
                    // Weld with tolerance (check neighbouring cells): before, points at cell boundaries stayed separate,
                    // mostly at the gore seams and the apex -> tears that jittered in flight and on the ground
                    Vector3Int key = new Vector3Int(Mathf.FloorToInt(lp.x / WeldEps), Mathf.FloorToInt(lp.y / WeldEps), Mathf.FloorToInt(lp.z / WeldEps));
                    int idx = -1;
                    for (int gx = -1; gx <= 1 && idx < 0; gx++)
                        for (int gy = -1; gy <= 1 && idx < 0; gy++)
                            for (int gz = -1; gz <= 1 && idx < 0; gz++)
                            {
                                List<int> cell;
                                if (!weld.TryGetValue(new Vector3Int(key.x + gx, key.y + gy, key.z + gz), out cell)) continue;
                                foreach (int q in cell) if ((P[q] - lp).sqrMagnitude < WeldEps * WeldEps) { idx = q; break; }
                            }
                    if (idx < 0)
                    {
                        idx = P.Count; P.Add(lp); used.Add(false); pArea.Add(0f);
                        List<int> cell;
                        if (!weld.TryGetValue(key, out cell)) { cell = new List<int>(); weld[key] = cell; }
                        cell.Add(idx);
                    }
                    map[i] = idx;
                    if (!bindWeld.ContainsKey(bk)) bindWeld[bk] = idx;
                }
                Mesh cm = new Mesh();
                cm.name = "ChuteFX_" + m.name;
                cm.MarkDynamic();
                Vector3[] lv = new Vector3[wv.Length];
                for (int i = 0; i < wv.Length; i++) lv[i] = P[map[i]];
                cm.vertices = lv;
                cm.uv = m.uv;
                Color[] cols = m.colors;
                if (cols != null && cols.Length == wv.Length) cm.colors = cols;
                cm.subMeshCount = m.subMeshCount;
                Vector3 bmn = lv.Length > 0 ? lv[0] : Vector3.zero, bmx = bmn;
                for (int i = 0; i < lv.Length; i++) { bmn = Vector3.Min(bmn, lv[i]); bmx = Vector3.Max(bmx, lv[i]); }
                float D = (bmx - bmn).magnitude;
                List<List<int>> triLists = new List<List<int>>();
                pendingTris.Add(triLists);
                for (int s = 0; s < m.subMeshCount; s++)
                {
                    int[] tri = m.GetTriangles(s);
                    List<int> keep = new List<int>(tri.Length);
                    for (int k = 0; k + 2 < tri.Length; k += 3)
                    {
                        int a = map[tri[k]], tb = map[tri[k + 1]], cc = map[tri[k + 2]];
                        // lines are long, very thin triangles without intermediate points -> do not treat them as cloth
                        float lab = (P[a] - P[tb]).magnitude, lbc = (P[tb] - P[cc]).magnitude, lca = (P[cc] - P[a]).magnitude;
                        float lmax = Mathf.Max(lab, Mathf.Max(lbc, lca));
                        float area2 = Vector3.Cross(P[tb] - P[a], P[cc] - P[a]).magnitude;
                        if (lmax > 0.12f * D && area2 / Mathf.Max(lmax, 1e-6f) < 0.12f * lmax)
                        {
                            meshLinePts.Add(P[a]); meshLinePts.Add(P[tb]); meshLinePts.Add(P[cc]);
                            meshLineMesh.Add(f.parts.Count); meshLineMesh.Add(f.parts.Count); meshLineMesh.Add(f.parts.Count);
                            lineTris++;
                            continue;
                        }
                        keep.Add(tri[k]); keep.Add(tri[k + 1]); keep.Add(tri[k + 2]);
                        pArea[a] += area2 / 6f; pArea[tb] += area2 / 6f; pArea[cc] += area2 / 6f;
                        used[a] = true; used[tb] = true; used[cc] = true;
                        AddEdge(edges, a, tb, P);
                        AddEdge(edges, tb, cc, P);
                        AddEdge(edges, cc, a, P);
                        AddBend(bends, opp, a, tb, cc, P);
                        AddBend(bends, opp, tb, cc, a, P);
                        AddBend(bends, opp, cc, a, tb, P);
                    }
                    triLists.Add(keep);
                }
                GameObject child = new GameObject("ChuteFX_Mesh");
                child.layer = r.gameObject.layer;
                child.transform.SetParent(f.go.transform, false);
                MeshFilter nmf = child.AddComponent<MeshFilter>();
                nmf.sharedMesh = cm;
                MeshRenderer nmr = child.AddComponent<MeshRenderer>();
                nmr.sharedMaterials = r.sharedMaterials;
                f.parts.Add(new ClothMesh(cm, map, nmr));
            }
            if (P.Count == 0) { Destroy(f.go); Cfg.Log("Canopy cut, but no readable cloth mesh found (" + names + ")"); return; }
            int nCloth = P.Count;

            // Stitch separate pieces (apex piece, inner/outer surface) - per canopy (one part can have several caps)
            HashSet<long> clothEdgeKeys = new HashSet<long>(edges.Keys);
            int[] meshOf = new int[nCloth];
            for (int i = 0; i < nCloth; i++) meshOf[i] = -1;
            for (int pi = 0; pi < f.parts.Count; pi++) foreach (int q in f.parts[pi].map) if (q < nCloth && meshOf[q] < 0) meshOf[q] = pi;
            int dropped, groups;
            int[] groupOf;
            int stitches = StitchIslands(P, edges, bends, nCloth, used, meshOf, out dropped, out groupOf, out groups);
            int layerStitches = StitchLayers(P, edges, nCloth, used, groupOf, groups);
            for (int pi = 0; pi < f.parts.Count; pi++)
            {
                ClothMesh cmp = f.parts[pi];
                List<List<int>> tl = pendingTris[pi];
                for (int s = 0; s < tl.Count; s++)
                {
                    List<int> fin = new List<int>(tl[s].Count);
                    for (int k = 0; k + 2 < tl[s].Count; k += 3)
                    {
                        if (!used[cmp.map[tl[s][k]]] || !used[cmp.map[tl[s][k + 1]]] || !used[cmp.map[tl[s][k + 2]]]) continue;
                        fin.Add(tl[s][k]); fin.Add(tl[s][k + 1]); fin.Add(tl[s][k + 2]);
                    }
                    cmp.mesh.SetTriangles(fin, s);
                }
                cmp.mesh.RecalculateNormals();
                cmp.mesh.RecalculateBounds();
                cmp.FinishTopology();
            }

            // Ropes: per canopy attachment points around the hem (about its own axis), a common knot below
            Vector3 cenC = Vector3.zero;
            int nUsed = 0;
            Vector3[] gCen = new Vector3[groups];
            int[] gCnt = new int[groups];
            List<int>[] gPts = new List<int>[groups];
            for (int g = 0; g < groups; g++) gPts[g] = new List<int>();
            for (int i = 0; i < nCloth; i++)
            {
                if (!used[i]) continue;
                cenC += P[i]; nUsed++;
                int g = groupOf[i];
                if (g >= 0 && g < groups) { gCen[g] += P[i]; gCnt[g]++; gPts[g].Add(i); }
            }
            cenC /= Mathf.Max(1, nUsed);
            int[] gMesh = new int[groups];
            for (int g = 0; g < groups; g++) { gCen[g] /= Mathf.Max(1, gCnt[g]); gMesh[g] = gPts[g].Count > 0 ? MeshOwner(gPts[g], meshOf) : -1; }
            // riser end = farthest point of the line meshes (otherwise the part), knot = end of the canopy lines
            Vector3 riserEnd = inv * (c.mod.part.transform.position - pivotW);
            if (linePts.Count > 0)
            {
                float fd = -1f;
                foreach (Vector3 lp in linePts) { float dd = (lp - cenC).sqrMagnitude; if (dd > fd) { fd = dd; riserEnd = lp; } }
            }
            Vector3 conf = riserEnd;
            if (meshLinePts.Count > 0)
            {
                // several caps: knot per cap (farthest point of its lines), common knot = their centre
                Vector3 ks = Vector3.zero;
                int kc = 0;
                if (groups > 1)
                    for (int g = 0; g < groups; g++)
                    {
                        float fd = -1f;
                        Vector3 kn = Vector3.zero;
                        for (int k = 0; k < meshLinePts.Count; k++)
                        {
                            if (meshLineMesh[k] != gMesh[g]) continue;
                            float dd = (meshLinePts[k] - gCen[g]).sqrMagnitude;
                            if (dd > fd) { fd = dd; kn = meshLinePts[k]; }
                        }
                        if (fd >= 0f) { ks += kn; kc++; }
                    }
                if (kc > 0) conf = ks / kc;
                else
                {
                    float fd = -1f;
                    foreach (Vector3 lp in meshLinePts) { float dd = (lp - cenC).sqrMagnitude; if (dd > fd) { fd = dd; conf = lp; } }
                }
            }
            if ((cenC - conf).sqrMagnitude < 0.01f) conf = Vector3.zero;
            Vector3 axis = (cenC - conf).sqrMagnitude > 1e-6f ? (cenC - conf).normalized : Vector3.up;
            int confluence = P.Count;
            P.Add(conf);
            List<int[]> ropes = new List<int[]>();
            float ropeSlack = UnityEngine.Random.Range(1.06f, 1.25f);
            int seg = Mathf.Max(4, Cfg.ropeSegments);
            Vector3[] gAxis = new Vector3[groups];
            for (int g = 0; g < groups; g++)
            {
                Vector3 ax = (gCen[g] - conf).sqrMagnitude > 1e-6f ? (gCen[g] - conf).normalized : axis;
                gAxis[g] = ax;
                if (gPts[g].Count == 0) continue;
                Vector3 u = Vector3.Cross(ax, Mathf.Abs(ax.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                Vector3 w = Vector3.Cross(ax, u);
                List<Vector3> wl = new List<Vector3>();
                if (groups > 1) for (int k = 0; k < meshLinePts.Count; k++) if (meshLineMesh[k] == gMesh[g]) wl.Add(meshLinePts[k]);
                if (wl.Count < 4) wl = meshLinePts.Count > 0 ? meshLinePts : linePts;
                int ropeCount = Mathf.Clamp(CountWireClusters(wl, conf, ax, u, w), 6, groups > 1 ? 16 : 32);
                int[] attach = new int[ropeCount];
                float[] best = new float[ropeCount];
                for (int k = 0; k < ropeCount; k++) { attach[k] = -1; best[k] = -1e9f; }
                foreach (int i in gPts[g])
                {
                    Vector3 p = P[i] - conf;
                    float hh = Vector3.Dot(p, ax);
                    Vector3 rad = p - ax * hh;
                    float ang = Mathf.Atan2(Vector3.Dot(rad, w), Vector3.Dot(rad, u));
                    int sec = Mathf.Clamp(Mathf.FloorToInt((ang + Mathf.PI) / (2f * Mathf.PI) * ropeCount), 0, ropeCount - 1);
                    // hem = large radius, as close to the cut point as possible
                    float score = rad.magnitude - 0.35f * hh;
                    if (score > best[sec]) { best[sec] = score; attach[sec] = i; }
                }
                for (int k = 0; k < ropeCount; k++)
                {
                    if (attach[k] < 0) continue;
                    Vector3 top = P[attach[k]];
                    int[] rope = new int[seg + 1];
                    rope[0] = confluence;
                    for (int s2 = 1; s2 < seg; s2++) { rope[s2] = P.Count; P.Add(Vector3.Lerp(conf, top, (float)s2 / seg)); }
                    rope[seg] = attach[k];
                    float sl = (top - conf).magnitude * ropeSlack / seg;
                    for (int s2 = 0; s2 < seg; s2++) AddEdgeLen(edges, rope[s2], rope[s2 + 1], sl);
                    ropes.Add(rope);
                }
            }
            if ((riserEnd - conf).magnitude > 0.2f)
            {
                int rs = 5;
                int[] riser = new int[rs + 1];
                riser[0] = confluence;
                for (int s = 1; s <= rs; s++) { riser[s] = P.Count; P.Add(Vector3.Lerp(conf, riserEnd, (float)s / rs)); }
                float rl = (riserEnd - conf).magnitude / rs;
                for (int s = 0; s < rs; s++) AddEdgeLen(edges, riser[s], riser[s + 1], rl);
                ropes.Add(riser);
                if (Cfg.tetherOnLanding && v.LandedOrSplashed)
                {
                    f.pin = riser[rs];
                    f.pinPart = c.mod.part;
                    f.pinOffset = c.mod.part.transform.InverseTransformPoint(pivotW + frame * riserEnd);
                    f.pinLocal = riserEnd;
                    f.pinMaxDist = 2f * ((cenC - conf).magnitude + (riserEnd - conf).magnitude) + 5f;
                }
            }
            f.ropes = ropes.ToArray();
            f.ropeWidth = Cfg.lineWidth > 0f ? Cfg.lineWidth : Mathf.Clamp((cenC - conf).magnitude * 0.0015f, 0.006f, 0.02f);
            if (ropes.Count > 0)
            {
                GameObject rgo = new GameObject("ChuteFX_Ropes");
                rgo.layer = lineLayer != 0 ? lineLayer : f.parts[0].r.gameObject.layer;
                rgo.transform.SetParent(f.go.transform, false);
                f.ropeMesh = new Mesh();
                f.ropeMesh.MarkDynamic();
                rgo.AddComponent<MeshFilter>().sharedMesh = f.ropeMesh;
                MeshRenderer rr = rgo.AddComponent<MeshRenderer>();
                rr.sharedMaterial = RopeMaterial((lineMats != null && lineMats.Length > 0) ? lineMats[0] : f.parts[0].r.sharedMaterial);
                rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                f.ropeR = rr;
                BuildRopeTopology(f);
            }

            int n = P.Count;
            f.pos = P.ToArray();
            f.vel = new Vector3[n];
            f.prev = new Vector3[n];
            f.rest0 = new Vector3[n];
            f.offs = new float[n];
            f.onGround = new bool[n];
            f.stacked = new bool[n];
            f.nCloth = nCloth;
            f.active = new bool[n];
            f.gY = new float[n];
            f.area = new float[n];
            for (int i = 0; i < nCloth && i < pArea.Count; i++) f.area[i] = pArea[i];
            for (int i = 0; i < n; i++) { f.active[i] = i >= nCloth || used[i]; f.gY[i] = -1e6f; }
            // layer height on the ground from the distance to the apex (0 = apex, 1 = hem)
            float[] layer = new float[P.Count];
            int apexIdx = 0;
            for (int g = groups - 1; g >= 0; g--)
            {
                if (gPts[g].Count == 0) continue;
                float dmax = 1e-4f;
                Vector3 apex = gCen[g];
                float hTop = -1e9f;
                foreach (int i in gPts[g]) { float hh = Vector3.Dot(P[i] - conf, gAxis[g]); if (hh > hTop) { hTop = hh; apex = P[i]; apexIdx = i; } }
                foreach (int i in gPts[g]) { float dd = (P[i] - apex).magnitude; layer[i] = dd; if (dd > dmax) dmax = dd; }
                foreach (int i in gPts[g]) layer[i] = Mathf.Clamp01(layer[i] / dmax);
            }
            f.apexIdx = apexIdx;
            // reference for the shape memory = cloth centroid (mixed with the ropes the canopy jumped up)
            Vector3 cen = Vector3.zero;
            for (int i = 0; i < nCloth; i++) cen += f.pos[i];
            cen /= nCloth;
            // Random toppling: upper parts get a push in a random direction,
            // so the canopy sometimes falls sideways, sometimes flips over, instead of always landing the same way
            float yLow = 1e9f, yHigh = -1e9f;
            for (int i = 0; i < nCloth; i++) if (used[i]) { yLow = Mathf.Min(yLow, f.pos[i].y); yHigh = Mathf.Max(yHigh, f.pos[i].y); }
            float yRange = Mathf.Max(0.1f, yHigh - yLow);
            float ta = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            Vector3 toppleDir = new Vector3(Mathf.Cos(ta), 0f, Mathf.Sin(ta));
            float toppleSpeed = v.Landed ? UnityEngine.Random.Range(0.8f, 3.5f) : UnityEngine.Random.Range(0.3f, 1.5f);
            bool down = v.LandedOrSplashed;
            f.noMemory = down;
            f.size = yRange;
            {
                Vector3 mn2 = f.pos[0], mx2 = f.pos[0];
                for (int i = 0; i < nCloth; i++) if (used[i]) { mn2 = Vector3.Min(mn2, f.pos[i]); mx2 = Vector3.Max(mx2, f.pos[i]); }
                f.size = (mx2 - mn2).magnitude;
            }
            Vector3 spin = Vector3.zero, spinPivot = Vector3.zero;
            toppleSpeed = 0f;
            // Fall outward: away from the vessel, toward the mount/canopy position (several canopies spread
            // around the capsule). If the canopy sits centred above the vessel, a random direction.
            if (v.LandedOrSplashed)
            {
                Vector3 com = inv * ((Vector3)v.CoM - pivotW);
                Vector3 partL = inv * (c.mod.part.transform.position - pivotW);
                Vector3 od = (cenC - com) + (partL - com) * 2f;
                od.y = 0f;
                f.outDir = od.magnitude > 0.3f ? od.normalized : toppleDir;
                f.outAcc = Cfg.spreadPush * UnityEngine.Random.Range(0.7f, 1.3f);
            }
            Cfg.Log("Wind: " + f.wind.magnitude.ToString("F1") + " m/s from " + (Mathf.Atan2(-f.wind.x, -f.wind.z) * Mathf.Rad2Deg).ToString("F0") + "°");
            for (int i = 0; i < n; i++)
            {
                f.vel[i] = vLocal + (i < nCloth ? toppleDir * (toppleSpeed * Mathf.Clamp01((f.pos[i].y - yLow) / yRange)) + Vector3.Cross(spin, f.pos[i] - spinPivot) : Vector3.zero);
                f.rest0[i] = f.pos[i] - cen;
                f.offs[i] = i < nCloth ? 0.015f + 0.004f * layer[i] : 0.012f;
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
            float layerEx = InitLayers(f);
            BuildRenderTopology(f);
            f.spawnTime = Time.time;
            f.groundY = -1e6f; f.nextGround = 0f;
            c.hidden.Clear();
            foreach (Renderer r in rlist) if (r.enabled) { c.hidden.Add(r); r.enabled = false; }
            fallen.Add(f);
            Cfg.Log("Canopy cut: " + c.mod.part.partInfo.name + " alt=" + f.alt.ToString("F1") + " v=" + vLocal.magnitude.ToString("F1") +
                    " landed=" + v.LandedOrSplashed + " clothParticles=" + nCloth + " seams=" + stitches + " layerSeams=" + layerStitches + " removedPieces=" + dropped + " ropes=" + ropes.Count + " lineTris=" + lineTris + " seamDuplicates=" + bindMerged + " bendEdges=" + bends.Count +
                    " edges=" + edges.Count + " layerNeighbours=" + layerEx.ToString("F2") + "m boundaryPoints=" + CountTrue(f.smFix) + " parts=[" + names.Trim() + "]");
        }

        static void AddEdge(Dictionary<long, float> edges, int a, int b, List<Vector3> P)
        {
            if (a == b) return;
            AddEdgeLen(edges, a, b, (P[a] - P[b]).magnitude);
        }

        // Bend edge between the opposite corners of two neighbouring triangles.
        // Rest length = distance when laid out flat: cloth folds softly instead of creasing.
        static void AddBend(Dictionary<long, float> bends, Dictionary<long, int> opp, int a, int b, int c, List<Vector3> P)
        {
            int lo = a < b ? a : b, hi = a < b ? b : a;
            long key = (long)lo * 1000000L + hi;
            int d;
            if (!opp.TryGetValue(key, out d)) { opp[key] = c; return; }
            if (d == c) return;
            Vector3 A = P[lo], e = P[hi] - A;
            float el = e.magnitude;
            if (el < 1e-6f) return;
            e /= el;
            float tc = Vector3.Dot(P[c] - A, e), td = Vector3.Dot(P[d] - A, e);
            float hc = (P[c] - A - e * tc).magnitude, hd = (P[d] - A - e * td).magnitude;
            float flat = Mathf.Sqrt((tc - td) * (tc - td) + (hc + hd) * (hc + hd));
            int l2 = c < d ? c : d, h2 = c < d ? d : c;
            long k2 = (long)l2 * 1000000L + h2;
            float cur = (P[c] - P[d]).magnitude;
            if (!bends.ContainsKey(k2)) bends[k2] = Mathf.Min(flat, cur);
        }

        static void AddEdgeLen(Dictionary<long, float> edges, int a, int b, float len)
        {
            if (a == b) return;
            int lo = a < b ? a : b, hi = a < b ? b : a;
            long key = (long)lo * 1000000L + hi;
            if (!edges.ContainsKey(key)) edges[key] = len;
        }

        static int Find(int[] par, int x) { while (par[x] != x) { par[x] = par[par[x]]; x = par[x]; } return x; }

        // Piece analysis: the largest connected piece of EACH cloth mesh is a canopy (one part can have several,
        // e.g. the Mk16-XL with three caps). Every other piece belongs to the nearest canopy: small pieces (caps at the
        // line ends) and far-away ones are removed - they used to be attached with "seams" up to 9 m long, which
        // stiffened the canopy and made the middle jitter. Larger pieces (the apex piece) are only stitched where
        // they are really close to their canopy. (Before, everything was measured against the largest piece only: on the
        // Mk16-XL the apex pieces of the second and third cap were dropped - their middle was missing.)
        // groupOf: canopy per point (-1 = removed), groups = number of canopies
        static int StitchIslands(List<Vector3> P, Dictionary<long, float> edges, Dictionary<long, float> bends, int n, List<bool> used,
                                 int[] meshOf, out int dropped, out int[] groupOf, out int groups)
        {
            dropped = 0;
            groupOf = new int[n];
            for (int i = 0; i < n; i++) groupOf[i] = used[i] ? 0 : -1;
            groups = 1;
            int[] par = new int[n];
            for (int i = 0; i < n; i++) par[i] = i;
            foreach (long key in edges.Keys)
            {
                int a = (int)(key / 1000000L), b = (int)(key % 1000000L);
                if (a >= n || b >= n) continue;
                int ra = Find(par, a), rb = Find(par, b);
                if (ra != rb) par[ra] = rb;
            }
            Dictionary<int, List<int>> compsD = new Dictionary<int, List<int>>();
            for (int i = 0; i < n; i++)
            {
                if (!used[i]) continue;
                int r = Find(par, i);
                List<int> l;
                if (!compsD.TryGetValue(r, out l)) { l = new List<int>(); compsD[r] = l; }
                l.Add(i);
            }
            if (compsD.Count <= 1) return 0;
            List<List<int>> comps = new List<List<int>>(compsD.Values);
            comps.Sort(delegate (List<int> x, List<int> y) { return y.Count.CompareTo(x.Count); });
            int biggest = comps[0].Count;
            // canopies: per mesh the largest piece, if it is reasonably large (a separate apex mesh does not become a canopy)
            Dictionary<int, int> meshBest = new Dictionary<int, int>();
            for (int ci = 0; ci < comps.Count; ci++)
            {
                int owner = MeshOwner(comps[ci], meshOf);
                int prev;
                if (!meshBest.TryGetValue(owner, out prev)) meshBest[owner] = ci;
            }
            List<int> mains = new List<int>();
            foreach (KeyValuePair<int, int> kv in meshBest)
                if (comps[kv.Value].Count >= Mathf.Max(Cfg.minPieceSize * 2, (int)(0.2f * biggest))) mains.Add(kv.Value);
            if (mains.Count == 0) mains.Add(0);
            mains.Sort();
            int nm = mains.Count;
            Vector3[] mMin = new Vector3[nm], mMax = new Vector3[nm];
            float[] mD = new float[nm];
            List<int>[] mainPts = new List<int>[nm];
            for (int m = 0; m < nm; m++)
            {
                List<int> l = comps[mains[m]];
                mainPts[m] = new List<int>(l);
                Vector3 mn = P[l[0]], mx = mn;
                foreach (int i in l) { mn = Vector3.Min(mn, P[i]); mx = Vector3.Max(mx, P[i]); }
                mMin[m] = mn; mMax[m] = mx; mD[m] = (mx - mn).magnitude;
                foreach (int i in l) groupOf[i] = m;
            }
            groups = nm;
            HashSet<int> isMain = new HashSet<int>(mains);
            int count = 0;
            string info = "";
            int shown = 0;
            HashSet<int> drop = new HashSet<int>();
            for (int ci = 0; ci < comps.Count; ci++)
            {
                if (isMain.Contains(ci)) continue;
                List<int> comp = comps[ci];
                Vector3 cmn = P[comp[0]], cmx = cmn;
                foreach (int i in comp) { cmn = Vector3.Min(cmn, P[i]); cmx = Vector3.Max(cmx, P[i]); }
                // nearest canopy: only those whose bounds (enlarged by 15 % of their size) touch the piece
                int bestM = -1;
                float bestMinD = float.MaxValue;
                float[] bestNd = null; int[] bestNj = null;
                for (int m = 0; m < nm; m++)
                {
                    float pad = 0.15f * mD[m];
                    if (cmx.x < mMin[m].x - pad || cmn.x > mMax[m].x + pad || cmx.y < mMin[m].y - pad || cmn.y > mMax[m].y + pad ||
                        cmx.z < mMin[m].z - pad || cmn.z > mMax[m].z + pad) continue;
                    float[] nd = new float[comp.Count];
                    int[] nj = new int[comp.Count];
                    float minD = float.MaxValue;
                    for (int k = 0; k < comp.Count; k++)
                    {
                        float bd = float.MaxValue; int bj = -1;
                        Vector3 pa = P[comp[k]];
                        foreach (int mi in mainPts[m])
                        {
                            float d = (pa - P[mi]).sqrMagnitude;
                            if (d < bd) { bd = d; bj = mi; }
                        }
                        nd[k] = Mathf.Sqrt(bd); nj[k] = bj;
                        if (nd[k] < minD) minD = nd[k];
                    }
                    if (minD < bestMinD) { bestMinD = minD; bestM = m; bestNd = nd; bestNj = nj; }
                }
                string verdict;
                int minSize = bestM >= 0 ? Mathf.Max(Cfg.minPieceSize, mainPts[bestM].Count / 50) : 0;
                if (bestM < 0 || comp.Count < minSize || bestMinD > 0.15f * mD[bestM])
                {
                    foreach (int i in comp) { used[i] = false; drop.Add(i); groupOf[i] = -1; }
                    dropped += comp.Count;
                    verdict = "removed";
                }
                else
                {
                    float lim = Mathf.Max(0.02f * mD[bestM], bestMinD * 1.5f + 0.02f);
                    int added = 0;
                    Dictionary<int, int> bestFor = new Dictionary<int, int>();
                    for (int k = 0; k < comp.Count; k++)
                    {
                        if (bestNd[k] > lim) continue;
                        int prevK;
                        if (!bestFor.TryGetValue(bestNj[k], out prevK) || bestNd[k] < bestNd[prevK]) bestFor[bestNj[k]] = k;
                    }
                    foreach (KeyValuePair<int, int> kv in bestFor)
                    {
                        AddEdgeLen(edges, comp[kv.Value], kv.Key, bestNd[kv.Value]);
                        added++;
                    }
                    count += added;
                    foreach (int i in comp) groupOf[i] = bestM;
                    verdict = "stitched(" + added + ")@" + (bestM + 1);
                }
                if (shown++ < 12 && (comp.Count >= 10 || verdict != "removed")) info += comp.Count + ":" + (bestMinD < 1e8f ? bestMinD.ToString("F2") : "-") + "m:" + verdict + " ";
            }
            if (drop.Count > 0)
            {
                List<long> rm = new List<long>();
                foreach (long key in edges.Keys) if (drop.Contains((int)(key / 1000000L)) || drop.Contains((int)(key % 1000000L))) rm.Add(key);
                foreach (long key in rm) edges.Remove(key);
                rm.Clear();
                foreach (long key in bends.Keys) if (drop.Contains((int)(key / 1000000L)) || drop.Contains((int)(key % 1000000L))) rm.Add(key);
                foreach (long key in rm) bends.Remove(key);
            }
            string ms = "";
            for (int m = 0; m < nm; m++) ms += (m > 0 ? "/" : "") + mainPts[m].Count;
            Cfg.Log("Pieces: " + comps.Count + ", canopies: " + nm + " (" + ms + " points, size " + mD[0].ToString("F1") + " m), removed: " + dropped +
                    " points, others (points:distance:result@canopy) " + info.Trim());
            return count;
        }

        // mesh that most points of a piece belong to
        static int MeshOwner(List<int> comp, int[] meshOf)
        {
            Dictionary<int, int> cnt = new Dictionary<int, int>();
            int best = -1, bc = -1;
            foreach (int i in comp)
            {
                int mo = i < meshOf.Length ? meshOf[i] : -1;
                int c0;
                cnt.TryGetValue(mo, out c0);
                cnt[mo] = ++c0;
                if (c0 > bc) { bc = c0; best = mo; }
            }
            return best;
        }

        // Double-walled canopies (outer and inner skin, joined only at the rim): without a connection the two skins
        // inflated separately ("canopy splits into 2 halves"). Every point is connected to the nearest point of the
        // other layer (not directly adjacent, closer than 2 % of the canopy size); the connection only pulls.
        // Separately per canopy (groupOf): distance from that canopy's size, no seams between different canopies
        static int StitchLayers(List<Vector3> P, Dictionary<long, float> edges, int n, List<bool> used, int[] groupOf, int groups)
        {
            float[] rG = new float[Mathf.Max(1, groups)];
            float r = 0.08f;
            for (int g = 0; g < rG.Length; g++)
            {
                Vector3 mn = Vector3.one * 1e9f, mx = -mn;
                bool any = false;
                for (int i = 0; i < n; i++) if (used[i] && groupOf[i] == g) { mn = Vector3.Min(mn, P[i]); mx = Vector3.Max(mx, P[i]); any = true; }
                rG[g] = any ? Mathf.Max(0.08f, 0.02f * (mx - mn).magnitude) : 0.08f;
                if (rG[g] > r) r = rG[g];
            }
            List<int>[] adj = new List<int>[n];
            foreach (long key in edges.Keys)
            {
                int a = (int)(key / 1000000L), b = (int)(key % 1000000L);
                if (a >= n || b >= n) continue;
                if (adj[a] == null) adj[a] = new List<int>();
                if (adj[b] == null) adj[b] = new List<int>();
                adj[a].Add(b); adj[b].Add(a);
            }
            Dictionary<Vector3Int, List<int>> grid = new Dictionary<Vector3Int, List<int>>();
            for (int i = 0; i < n; i++)
            {
                if (!used[i]) continue;
                Vector3Int k = new Vector3Int(Mathf.FloorToInt(P[i].x / r), Mathf.FloorToInt(P[i].y / r), Mathf.FloorToInt(P[i].z / r));
                List<int> l;
                if (!grid.TryGetValue(k, out l)) { l = new List<int>(); grid[k] = l; }
                l.Add(i);
            }
            HashSet<int> near = new HashSet<int>();
            int count = 0;
            for (int i = 0; i < n; i++)
            {
                if (!used[i]) continue;
                near.Clear(); near.Add(i);
                if (adj[i] != null) foreach (int a in adj[i]) { near.Add(a); if (adj[a] != null) foreach (int b in adj[a]) near.Add(b); }
                Vector3Int k = new Vector3Int(Mathf.FloorToInt(P[i].x / r), Mathf.FloorToInt(P[i].y / r), Mathf.FloorToInt(P[i].z / r));
                int gi = groupOf[i];
                float ri = gi >= 0 && gi < rG.Length ? rG[gi] : r;
                int best = -1; float bd = ri * ri;
                for (int gx = -1; gx <= 1; gx++)
                    for (int gy = -1; gy <= 1; gy++)
                        for (int gz = -1; gz <= 1; gz++)
                        {
                            List<int> l;
                            if (!grid.TryGetValue(new Vector3Int(k.x + gx, k.y + gy, k.z + gz), out l)) continue;
                            foreach (int j in l)
                            {
                                if (near.Contains(j) || groupOf[j] != gi) continue;
                                float d = (P[j] - P[i]).sqrMagnitude;
                                if (d < bd) { bd = d; best = j; }
                            }
                        }
                if (best < 0) continue;
                int lo = i < best ? i : best, hi = i < best ? best : i;
                long key2 = (long)lo * 1000000L + hi;
                if (edges.ContainsKey(key2)) continue;
                edges[key2] = Mathf.Sqrt(bd);
                count++;
            }
            return count;
        }

        // Estimate the number of lines from the original line mesh (angle clusters)
        static int CountWireClusters(List<Vector3> pts, Vector3 conf, Vector3 axis, Vector3 u, Vector3 w)
        {
            if (pts.Count < 4) return 16;
            float rMax = 0f;
            foreach (Vector3 p0 in pts) { Vector3 p = p0 - conf; float hh = Vector3.Dot(p, axis); float r = (p - axis * hh).magnitude; if (r > rMax) rMax = r; }
            const int bins = 120;
            bool[] occ = new bool[bins];
            foreach (Vector3 p0 in pts)
            {
                Vector3 p = p0 - conf;
                float hh = Vector3.Dot(p, axis);
                Vector3 rad = p - axis * hh;
                if (rad.magnitude < rMax * 0.5f) continue;
                float ang = Mathf.Atan2(Vector3.Dot(rad, w), Vector3.Dot(rad, u));
                occ[Mathf.Clamp(Mathf.FloorToInt((ang + Mathf.PI) / (2f * Mathf.PI) * bins), 0, bins - 1)] = true;
            }
            int clusters = 0;
            for (int i = 0; i < bins; i++) if (occ[i] && !occ[(i + bins - 1) % bins]) clusters++;
            if (clusters == 0) clusters = 16;
            return clusters;
        }

        static void BuildRopeTopology(Fallen f)
        {
            int nodes = 0;
            foreach (int[] r in f.ropes) nodes += r.Length;
            Vector3[] verts = new Vector3[nodes * 4];
            Vector2[] uv = new Vector2[nodes * 4];
            List<int> tris = new List<int>();
            int baseV = 0;
            foreach (int[] r in f.ropes)
            {
                for (int k = 0; k < r.Length; k++)
                {
                    float vv = (float)k / (r.Length - 1);
                    uv[baseV + k * 4 + 0] = new Vector2(0f, vv); uv[baseV + k * 4 + 1] = new Vector2(1f, vv);
                    uv[baseV + k * 4 + 2] = new Vector2(0f, vv); uv[baseV + k * 4 + 3] = new Vector2(1f, vv);
                    if (k < r.Length - 1)
                    {
                        int a = baseV + k * 4, b = baseV + (k + 1) * 4;
                        // two crossed strips, double-sided
                        tris.Add(a); tris.Add(b); tris.Add(a + 1); tris.Add(a + 1); tris.Add(b); tris.Add(b + 1);
                        tris.Add(a); tris.Add(a + 1); tris.Add(b); tris.Add(a + 1); tris.Add(b + 1); tris.Add(b);
                        tris.Add(a + 2); tris.Add(b + 2); tris.Add(a + 3); tris.Add(a + 3); tris.Add(b + 2); tris.Add(b + 3);
                        tris.Add(a + 2); tris.Add(a + 3); tris.Add(b + 2); tris.Add(a + 3); tris.Add(b + 3); tris.Add(b + 2);
                    }
                }
                baseV += r.Length * 4;
            }
            f.ropeVerts = verts;
            f.ropeNormals = new Vector3[verts.Length];
            f.ropeMesh.vertices = verts;
            f.ropeMesh.uv = uv;
            f.ropeMesh.SetTriangles(tris, 0);
        }

        static void UpdateRopeMesh(Fallen f)
        {
            if (f.ropeMesh == null) return;
            float hw = f.ropeWidth * 0.5f;
            int baseV = 0;
            foreach (int[] r in f.ropes)
            {
                for (int k = 0; k < r.Length; k++)
                {
                    Vector3 p = f.pos[r[k]];
                    Vector3 d = (k < r.Length - 1 ? f.pos[r[k + 1]] : p) - (k > 0 ? f.pos[r[k - 1]] : p);
                    if (d.sqrMagnitude < 1e-8f) d = Vector3.up;
                    d.Normalize();
                    Vector3 s1 = Vector3.Cross(d, Mathf.Abs(d.y) < 0.9f ? Vector3.up : Vector3.right).normalized * hw;
                    Vector3 s2 = Vector3.Cross(d, s1).normalized * hw;
                    int o = baseV + k * 4;
                    f.ropeVerts[o] = p - s1; f.ropeVerts[o + 1] = p + s1;
                    f.ropeVerts[o + 2] = p - s2; f.ropeVerts[o + 3] = p + s2;
                    Vector3 n1 = s2.normalized, n2 = s1.normalized;
                    if (n1.y < 0f) n1 = -n1;
                    if (n2.y < 0f) n2 = -n2;
                    f.ropeNormals[o] = n1; f.ropeNormals[o + 1] = n1; f.ropeNormals[o + 2] = n2; f.ropeNormals[o + 3] = n2;
                }
                baseV += r.Length * 4;
            }
            f.ropeMesh.vertices = f.ropeVerts;
            f.ropeMesh.normals = f.ropeNormals;
            f.ropeMesh.RecalculateBounds();
        }

        double GroundAltAt(CelestialBody b, double lat, double lon, double refAlt, out bool water)
        {
            water = false;
            Vector3d top = b.GetWorldSurfacePosition(lat, lon, refAlt + 50.0);
            Vector3 up = (Vector3)b.GetSurfaceNVector(lat, lon);
            RaycastHit hit;
            double terr = PqsAltitude(b, lat, lon);
            bool sea = b.ocean && terr < 0.0;
            if (Physics.Raycast((Vector3)top, -up, out hit, 5000f, TerrainMask))
            {
                double g = refAlt + 50.0 - hit.distance;
                if (sea && g < 0.5) { water = true; return 0.0; }
                lastGroundInfo = "ray=" + hit.collider.name + " g=" + g.ToString("F1") + " pqs=" + terr.ToString("F1") + " ref=" + refAlt.ToString("F1");
                if (g < terr - 5.0) return terr;   // ray fell through a gap
                return g;
            }
            lastGroundInfo = "no hit, pqs=" + terr.ToString("F1") + " ref=" + refAlt.ToString("F1");
            if (sea) { water = true; return 0.0; }
            return terr;
        }

        static string lastGroundInfo = "";
        // Terrain height directly from the PQS (TerrainAltitude returned ~4000 m at the KSC in places)
        static double PqsAltitude(CelestialBody b, double lat, double lon)
        {
            if (b.pqsController == null) return 0.0;
            return b.pqsController.GetSurfaceHeight(b.GetRelSurfaceNVector(lat, lon)) - b.Radius;
        }

        bool UpdateFallen(Fallen f, float dt)
        {
            if (f.go == null || f.body == null) return false;
            Vessel av = FlightGlobals.ActiveVessel;
            CelestialBody b = f.body;
            if (av == null || av.mainBody != b) return false;
            Quaternion frame = SurfaceFrame(b, f.lat, f.lon);
            Vector3d wp = b.GetWorldSurfacePosition(f.lat, f.lon, f.alt);
            f.go.transform.position = (Vector3)wp;
            f.go.transform.rotation = frame;
            if ((wp - (Vector3d)av.transform.position).magnitude > Cfg.maxDistance + 2000.0) return false;

            int n = f.pos.Length;
            if (f.atmo) f.wind = SharedWind(b, Planetarium.GetUniversalTime());
            Vector3 cen = Vector3.zero;
            int nAct = 0;
            for (int i = 0; i < f.nCloth; i++) if (f.active[i]) { cen += f.pos[i]; nAct++; }
            cen /= Mathf.Max(1, nAct);

            if (Time.time >= f.nextGround)
            {
                f.nextGround = Time.time + 0.5f;
                Vector3 cw = f.go.transform.TransformPoint(cen);
                Vector3d cd = (Vector3d)cw;
                double clat = b.GetLatitude(cd), clon = b.GetLongitude(cd);
                bool water;
                double g = GroundAltAt(b, clat, clon, f.alt + cen.y, out water);
                f.water = water;
                float ng = (float)(g - f.alt);
                if (!water)
                {
                    float R = Mathf.Max(1f, f.size * 0.4f);
                    float gE = SampleGround(f, b, cen + new Vector3(R, 0f, 0f), ng), gW = SampleGround(f, b, cen - new Vector3(R, 0f, 0f), ng);
                    float gN = SampleGround(f, b, cen + new Vector3(0f, 0f, R), ng), gS = SampleGround(f, b, cen - new Vector3(0f, 0f, R), ng);
                    float sx = Mathf.Clamp((gE - gW) / (2f * R), -0.7f, 0.7f), sz = Mathf.Clamp((gN - gS) / (2f * R), -0.7f, 0.7f);
                    bool first = f.groundY < -1e5f;
                    f.slopeX = first ? sx : Mathf.Lerp(f.slopeX, sx, 0.3f);
                    f.slopeZ = first ? sz : Mathf.Lerp(f.slopeZ, sz, 0.3f);
                    f.groundCX = cen.x; f.groundCZ = cen.z;
                }
                if (f.groundY < -1e5f) f.groundY = ng;
                else if (Mathf.Abs(ng - f.groundY) > 30f) { if (f.groundJumps < 10) { f.groundJumps++; Cfg.Log("Ground measurement discarded: " + f.groundY.ToString("F1") + " -> " + ng.ToString("F1") + " (" + lastGroundInfo + ")"); } }
                else
                {
                    // Follow the ground height only smoothly: if the measurement jumps (e.g. the ray hits a grass collider sometimes,
                    // sometimes not), the whole canopy would be pushed up/dropped every 0.5 s
                    f.groundY = Mathf.Lerp(f.groundY, ng, 0.3f);
                }
                f.seabedY = water ? (float)(PqsAltitude(b, clat, clon) - f.alt) : -1e6f;
            }

            bool rails = TimeWarp.CurrentRate > 50f;
            float t = Time.time;
            float age = t - f.spawnTime;
            double ut = Planetarium.GetUniversalTime();
            if (f.contact && f.contactUT < 0.0) f.contactUT = ut;
            double lyingFor = f.contactUT >= 0.0 ? ut - f.contactUT : 0.0;
            if (f.contact && Cfg.groundLifetime > 0f && lyingFor > Cfg.groundLifetime + (f.water ? Cfg.waterFloatTime : 0f)) return false;

            // riser end follows the part (as long as it is loaded and in range)
            if (f.pin >= 0)
            {
                if (f.pinPart == null || f.pinPart.vessel == null || !f.pinPart.vessel.loaded) f.pin = -1;
                else
                {
                    Vector3 pw = f.pinPart.transform.TransformPoint(f.pinOffset);
                    f.pinLocal = Quaternion.Inverse(frame) * (pw - (Vector3)wp);
                    if ((f.pinLocal - f.pos[f.pin]).magnitude > f.pinMaxDist) { f.pin = -1; Cfg.Log("Riser detached from the part (too far away)"); }
                    else if (f.sleeping && (f.pinLocal - f.pos[f.pin]).sqrMagnitude > 0.04f) { f.sleeping = false; f.stillSince = 0f; }
                }
            }

            if (rails)
            {
                for (int i = 0; i < n; i++)
                {
                    float floor = f.water ? Mathf.Max(-(float)f.alt - f.sink, f.seabedY) : GroundAt(f, f.pos[i], i) + f.lift;
                    f.pos[i].y = floor + f.offs[i];
                }
                if (!f.contact) { f.contact = true; f.contactTime = t; }
                f.dirty = true;
            }
            else if (!f.sleeping)
            {
                // fixed step: at a low frame rate do not compute more and more steps (downward spiral);
                // with time warp up to 50x the simulation then simply runs slower than game time
                const float h = 0.025f;
                f.acc = Mathf.Min(f.acc + dt, 3f * h);
                int steps = Mathf.FloorToInt(f.acc / h);
                if (steps > 0)
                {
                    f.acc -= steps * h;
                    ComputeNormals(f);
                    UpdateParticleGround(f);
                    ComputeTurbulence(f, t);
                    f.dirty = true;
                    for (int s = 0; s < steps; s++) Step(f, h, t, age);
                }
            }

            if (f.water && f.contact)
            {
                int inW = 0, tot = 0;
                float sea = -(float)f.alt + 0.25f;
                for (int i = 0; i < f.nCloth; i += 4) { tot++; if (f.pos[i].y < sea) inW++; }
                float frac = tot > 0 ? (float)inW / tot : 0f;
                f.wetTime += dt * Mathf.Clamp01((frac - 0.2f) / 0.4f);
                float soak = Mathf.Clamp01(f.wetTime / Mathf.Max(1f, Cfg.waterFloatTime));
                f.soak = soak;
                if (soak >= 1f) f.sink += Cfg.sinkSpeed * dt;
                // soaking: fast at first, then slower (clearly darker after ~15 s, slightly cooler tone)
                float wt = f.wetTime - Cfg.soakDelay;
                float wet = wt <= 0f ? 0f : 1f - Mathf.Exp(-wt / Mathf.Max(0.5f, Cfg.soakTime));
                float br = 1f - Cfg.soakDarkness * wet;
                Color col = new Color(br * (1f - 0.06f * wet), br * (1f - 0.03f * wet), br, 1f);
                f.tint = col;
                if (f.mpb == null) f.mpb = new MaterialPropertyBlock();
                if (!f.loggedShader && f.parts.Count > 0 && f.parts[0].r.sharedMaterial != null)
                {
                    f.loggedShader = true;
                    Material sm = f.parts[0].r.sharedMaterial;
                    Cfg.Log("Wet tint: shader=" + sm.shader.name + " _Color=" + sm.HasProperty("_Color") + " _BurnColor=" + sm.HasProperty("_BurnColor"));
                }
                foreach (ClothMesh cm in f.parts)
                {
                    cm.r.GetPropertyBlock(f.mpb);
                    Material sm = cm.r.sharedMaterial;
                    if (sm == null || sm.HasProperty("_Color")) f.mpb.SetColor("_Color", col);
                    if (sm != null && sm.HasProperty("_BurnColor")) f.mpb.SetColor("_BurnColor", col);
                    cm.r.SetPropertyBlock(f.mpb);
                }
                if (f.ropeR != null)
                {
                    f.ropeR.GetPropertyBlock(f.mpb);
                    f.mpb.SetColor("_Color", new Color(Cfg.lineGray * br, Cfg.lineGray * br, Cfg.lineGray * br * 0.97f, 1f));
                    f.ropeR.SetPropertyBlock(f.mpb);
                }
                if (cen.y < -f.alt - Cfg.waterRemoveDepth) return false;
            }

            // End of the lying time: sink slowly into the ground during the last 4 s instead of vanishing suddenly
            float life = Cfg.groundLifetime + (f.water ? Cfg.waterFloatTime : 0f);
            if (f.contact && Cfg.groundLifetime > 0f)
            {
                if (f.contactUT < 0.0) f.contactUT = ut;   // touched down only in this frame
                float rem = life - (float)(ut - f.contactUT);
                if (rem < 4f) f.go.transform.position = (Vector3)wp - (frame * Vector3.up) * (Mathf.Clamp01((4f - rem) / 4f) * 0.8f);
            }
            // removed by right-click: sink into the ground/water within 1.5 s, then gone
            if (f.removeT >= 0f)
            {
                float k = Mathf.Clamp01((t - f.removeT) / 1.5f);
                f.go.transform.position = (Vector3)wp - (frame * Vector3.up) * (k * k * f.removeDepth);
                if (k >= 1f) return false;
            }
            if (f.dirty)
            {
                f.dirty = false;
                SmoothRender(f);
                RenderNormals(f);
                foreach (ClothMesh cm in f.parts) cm.Apply(f.rpos);
                Vector3 mn = f.rpos[0], mx = mn;
                for (int i = 0; i < f.nCloth; i++) if (f.active[i]) { mn = Vector3.Min(mn, f.rpos[i]); mx = Vector3.Max(mx, f.rpos[i]); }
                f.bMin = mn; f.bMax = mx;
                UpdateRopeMesh(f);
            }
            return true;
        }

        // Prepare layers: empty support heights, and from which distance in the deployed canopy two points can be different
        // layers (about 3 cloth edges; closer = neighbours in the cloth, e.g. the apex piece above the vent, inner
        // and outer skin, densely packed points at the apex). Returns that distance (m)
        static float InitLayers(Fallen f)
        {
            int n = f.nCloth;
            f.lyF = new float[n];
            f.lyP = new float[n];
            for (int i = 0; i < n; i++) { f.lyF[i] = -1e6f; f.lyP[i] = -1e6f; }
            // Search radius per point from the local mesh spacing: coarse areas (drogue hem ~0.8 m) need more, otherwise
            // folds of the lower layer poke through between the points of the upper one
            float[] eMax = new float[n];
            List<float> el = new List<float>();
            for (int k = 0; k < f.ea.Length; k++)
            {
                int a = f.ea[k], b = f.eb[k];
                if (f.et[k] || a >= n || b >= n) continue;
                el.Add(f.el[k]);
                if (f.el[k] > eMax[a]) eMax[a] = f.el[k];
                if (f.el[k] > eMax[b]) eMax[b] = f.el[k];
            }
            f.layerR = new float[n];
            float rMax = LayerRMin;
            for (int i = 0; i < n; i++) { f.layerR[i] = Mathf.Clamp(0.6f * eMax[i], LayerRMin, LayerRMax); if (f.layerR[i] > rMax) rMax = f.layerR[i]; }
            el.Sort();
            float med = el.Count > 0 ? el[el.Count / 2] : 0.3f;
            float ex = Mathf.Clamp(Mathf.Max(3f * med, 1.6f * rMax), 0.6f, 1.5f);
            f.layerEx2 = ex * ex;
            return ex;
        }

        // Prepare rendering. Edges for smoothing (each cloth triangle only once - double-sided surfaces have every
        // triangle twice, front and back on the same points); boundary points (edge of only one triangle) stay in place.
        // For the normals: per cloth point the list of its render vertices (from all parts).
        static void BuildRenderTopology(Fallen f)
        {
            int n = f.nCloth;
            HashSet<long> triSeen = new HashSet<long>();
            Dictionary<long, int> edgeCount = new Dictionary<long, int>();
            foreach (ClothMesh cm in f.parts)
            {
                int[] t = cm.tri;
                for (int k = 0; k + 2 < t.Length; k += 3)
                {
                    int a = cm.map[t[k]], b = cm.map[t[k + 1]], c = cm.map[t[k + 2]];
                    if (a == b || b == c || a == c || a >= n || b >= n || c >= n) continue;
                    int lo = Math.Min(a, Math.Min(b, c)), hi = Math.Max(a, Math.Max(b, c)), mid = a + b + c - lo - hi;
                    if (!triSeen.Add(((long)lo * 1000000L + mid) * 1000000L + hi)) continue;
                    CountEdge(edgeCount, a, b);
                    CountEdge(edgeCount, b, c);
                    CountEdge(edgeCount, c, a);
                }
            }
            f.smFix = new bool[n];
            int[] deg = new int[n];
            f.smA = new int[edgeCount.Count];
            f.smB = new int[edgeCount.Count];
            int e = 0;
            foreach (KeyValuePair<long, int> kv in edgeCount)
            {
                int a = (int)(kv.Key / 1000000L), b = (int)(kv.Key % 1000000L);
                f.smA[e] = a; f.smB[e] = b; e++;
                deg[a]++; deg[b]++;
                if (kv.Value == 1) { f.smFix[a] = true; f.smFix[b] = true; }
            }
            f.smW = new float[n];
            for (int i = 0; i < n; i++) f.smW[i] = f.smFix[i] || deg[i] == 0 ? 0f : 0.5f / deg[i];
            f.rpos = new Vector3[n];
            f.smX = new float[n]; f.smY = new float[n]; f.smZ = new float[n];
            // render vertices per cloth point
            f.pvStart = new int[n + 1];
            foreach (ClothMesh cm in f.parts) foreach (int p in cm.map) if (p < n) f.pvStart[p + 1]++;
            for (int i = 0; i < n; i++) f.pvStart[i + 1] += f.pvStart[i];
            f.pvPart = new int[f.pvStart[n]];
            f.pvVert = new int[f.pvStart[n]];
            int[] cur = new int[n];
            for (int pi = 0; pi < f.parts.Count; pi++)
            {
                ClothMesh cm = f.parts[pi];
                for (int v = 0; v < cm.map.Length; v++)
                {
                    int p = cm.map[v];
                    if (p >= n) continue;
                    int slot = f.pvStart[p] + cur[p]++;
                    f.pvPart[slot] = pi;
                    f.pvVert[slot] = v;
                }
                int nv = cm.map.Length;
                cm.rnx = new float[nv]; cm.rny = new float[nv]; cm.rnz = new float[nv];
                cm.unx = new float[nv]; cm.uny = new float[nv]; cm.unz = new float[nv];
            }
        }

        static void CountEdge(Dictionary<long, int> d, int a, int b)
        {
            long key = a < b ? (long)a * 1000000L + b : (long)b * 1000000L + a;
            int c;
            d.TryGetValue(key, out c);
            d[key] = c + 1;
        }

        static int CountTrue(bool[] a)
        {
            int c = 0;
            if (a != null) foreach (bool b in a) if (b) c++;
            return c;
        }

        // Render positions: the simulated positions, optionally slightly smoothed (average with the neighbours). The
        // boundary stays, resting points do not sink below their support. (Off by default: on the double-walled ReStock
        // canopies the hem and vent are folds, not boundaries - smoothing pulled them back.)
        static void SmoothRender(Fallen f)
        {
            int n = f.nCloth;
            Vector3[] r = f.rpos;
            Array.Copy(f.pos, r, n);
            if (Cfg.renderSmooth <= 0 || f.smA == null) return;
            float[] ax = f.smX, ay = f.smY, az = f.smZ, w = f.smW;
            int[] A = f.smA, B = f.smB;
            for (int it = 0; it < Cfg.renderSmooth; it++)
            {
                Array.Clear(ax, 0, n); Array.Clear(ay, 0, n); Array.Clear(az, 0, n);
                for (int e = 0; e < A.Length; e++)
                {
                    int a = A[e], b = B[e];
                    Vector3 pa = r[a], pb = r[b];
                    ax[a] += pb.x; ay[a] += pb.y; az[a] += pb.z;
                    ax[b] += pa.x; ay[b] += pa.y; az[b] += pa.z;
                }
                for (int i = 0; i < n; i++)
                {
                    float wi = w[i];
                    if (wi <= 0f) continue;
                    Vector3 p = r[i];
                    r[i] = new Vector3(0.5f * p.x + wi * ax[i], 0.5f * p.y + wi * ay[i], 0.5f * p.z + wi * az[i]);
                }
            }
            for (int i = 0; i < n; i++) if (f.onGround[i] && r[i].y < f.pos[i].y) r[i].y = f.pos[i].y;
        }

        // Normals per render vertex: from its own triangles, then averaged with the other render vertices of the same
        // cloth point whose direction differs by less than 60 degrees. This makes gore and UV seams smooth (Unity computed
        // each render vertex on its own: hard edges at every seam), front and back stay separate, and so does the fold
        // where outer and inner skin of the double-walled canopies meet at hem and vent (averaged over both skins the
        // normal pointed sideways there - the edge looked bulged and got a shadow).
        static void RenderNormals(Fallen f)
        {
            Vector3[] r = f.rpos;
            List<ClothMesh> parts = f.parts;
            for (int pi = 0; pi < parts.Count; pi++)
            {
                ClothMesh cm = parts[pi];
                int[] t = cm.tri, map = cm.map;
                float[] ax = cm.rnx, ay = cm.rny, az = cm.rnz, ux = cm.unx, uy = cm.uny, uz = cm.unz;
                int nv = map.Length;
                Array.Clear(ax, 0, nv); Array.Clear(ay, 0, nv); Array.Clear(az, 0, nv);
                for (int k = 0; k + 2 < t.Length; k += 3)
                {
                    int a = t[k], b = t[k + 1], c = t[k + 2];
                    Vector3 pa = r[map[a]], pb = r[map[b]], pc = r[map[c]];
                    float x1 = pb.x - pa.x, y1 = pb.y - pa.y, z1 = pb.z - pa.z;
                    float x2 = pc.x - pa.x, y2 = pc.y - pa.y, z2 = pc.z - pa.z;
                    float cx = y1 * z2 - z1 * y2, cy = z1 * x2 - x1 * z2, cz = x1 * y2 - y1 * x2;
                    ax[a] += cx; ay[a] += cy; az[a] += cz;
                    ax[b] += cx; ay[b] += cy; az[b] += cz;
                    ax[c] += cx; ay[c] += cy; az[c] += cz;
                }
                for (int v = 0; v < nv; v++)
                {
                    float l2 = ax[v] * ax[v] + ay[v] * ay[v] + az[v] * az[v];
                    if (l2 > 1e-24f) { float inv = 1f / Mathf.Sqrt(l2); ux[v] = ax[v] * inv; uy[v] = ay[v] * inv; uz[v] = az[v] * inv; }
                    else { ux[v] = 0f; uy[v] = 0f; uz[v] = 0f; }
                }
            }
            int[] ps = f.pvStart, pp = f.pvPart, pvv = f.pvVert;
            for (int p = 0; p < f.nCloth; p++)
            {
                int s0 = ps[p], s1 = ps[p + 1];
                for (int i = s0; i < s1; i++)
                {
                    ClothMesh ci = parts[pp[i]];
                    int vi = pvv[i];
                    float uxi = ci.unx[vi], uyi = ci.uny[vi], uzi = ci.unz[vi];
                    float sx = ci.rnx[vi], sy = ci.rny[vi], sz = ci.rnz[vi];
                    for (int j = s0; j < s1; j++)
                    {
                        if (j == i) continue;
                        ClothMesh cj = parts[pp[j]];
                        int vj = pvv[j];
                        if (uxi * cj.unx[vj] + uyi * cj.uny[vj] + uzi * cj.unz[vj] < 0.5f) continue;
                        sx += cj.rnx[vj]; sy += cj.rny[vj]; sz += cj.rnz[vj];
                    }
                    float l2 = sx * sx + sy * sy + sz * sz;
                    ci.nbuf[vi] = l2 > 1e-24f ? new Vector3(sx, sy, sz) / Mathf.Sqrt(l2) : Vector3.up;
                }
            }
        }

        // Sorts k[lo..hi) ascending and v along with it (without a comparer call per comparison like Array.Sort)
        static void SortLong(long[] k, int[] v, int lo, int hi)
        {
            while (hi - lo > 16)
            {
                int a = lo, b = (lo + hi) >> 1, c = hi - 1;
                if (k[a] > k[b]) SwapL(k, v, a, b);
                if (k[b] > k[c]) { SwapL(k, v, b, c); if (k[a] > k[b]) SwapL(k, v, a, b); }
                long piv = k[b];
                int i = lo, j = hi - 1;
                while (i <= j)
                {
                    while (k[i] < piv) i++;
                    while (k[j] > piv) j--;
                    if (i <= j) { SwapL(k, v, i, j); i++; j--; }
                }
                if (j - lo < hi - i) { SortLong(k, v, lo, j + 1); lo = i; }
                else { SortLong(k, v, i, hi); hi = j + 1; }
            }
            for (int i = lo + 1; i < hi; i++)
            {
                long kk = k[i];
                int vv = v[i], j = i - 1;
                while (j >= lo && k[j] > kk) { k[j + 1] = k[j]; v[j + 1] = v[j]; j--; }
                k[j + 1] = kk; v[j + 1] = vv;
            }
        }

        static void SwapL(long[] k, int[] v, int a, int b)
        {
            long t = k[a]; k[a] = k[b]; k[b] = t;
            int u = v[a]; v[a] = v[b]; v[b] = u;
        }

        // Cloth layers on the ground: a cloth point above another layer (another canopy, or a part of the same canopy
        // that was far away in the deployed canopy) lies at least one layer thickness above it. Every 2nd frame
        // (every frame on impact) for all canopies nearby in a shared ground grid: per point the highest foreign
        // support just below it in the 3x3 neighbour cells (fixed, no alternating offset; search radius from the local
        // mesh spacing). The support height (lyF) then holds the point in every simulation step like the ground. (Before,
        // it fell through a bit every step and was lifted again next frame - the canopies never came to rest. And densely
        // packed points at the apex piled up into a jittering spike because they were not direct neighbours in the mesh
        // - now only points that were far apart in the deployed canopy separate.)
        // Who lies on top: of two canopies the one that landed later - even if it plunged up to LayerDeep into the other
        // on impact (before, the heap that landed first poked through the later one over large areas). Within one canopy,
        // whoever was clearly above at the start of the last step, otherwise whoever is clearly above now; points that
        // already interpenetrate (tangled while falling) are not separated - otherwise both layers fight forever.
        const float LayerCell = 0.35f;      // m: grid cell (3x3 cells: search radius safe up to 0.35 m, mostly more)
        const float LayerRMax = 0.5f;       // m: largest search radius (coarse hems)
        const float LayerRMin = 0.2f;       // m: smallest search radius (fine areas)
        const float LayerDeep = 0.2f;       // m: a canopy that landed later may plunge this deep and is still put on top
        float[] ctK = new float[0];
        bool[] ctDeep = new bool[0];
        Fallen[] fArr = new Fallen[0];
        int layerTick;
        long[] lKey = new long[0], rKey = new long[0];
        int[] lOrd = new int[0], lF = new int[0], lI = new int[0], rStart = new int[0], rLen = new int[0], rNb = new int[0];
        float[] lX = new float[0], lY = new float[0], lZ = new float[0], lPY = new float[0], lRX = new float[0], lRY = new float[0], lRZ = new float[0], lYs = new float[0], lRH = new float[0];

        static bool LayerImpact(List<Fallen> list, float t)
        {
            foreach (Fallen f in list) if (!f.sleeping && (!f.contact || t - f.contactTime < 4f)) return true;
            return false;
        }

        static long LayerKey(int ix, int iz) { return ((long)(ix + 0x40000000) << 32) | (uint)(iz + 0x40000000); }

        void LayerCloth()
        {
            Fallen f0 = null;
            foreach (Fallen f in fallen) if (!f.sleeping && f.go != null && f.lyF != null) { f0 = f; break; }
            if (f0 == null) return;
            Matrix4x4 w2l = f0.go.transform.worldToLocalMatrix;
            Vector3 p0w = f0.go.transform.position;
            int total = 0;
            for (int k = 0; k < fallen.Count; k++) if (fallen[k].go != null && fallen[k].lyF != null) total += fallen[k].nCloth;
            if (lKey.Length < total)
            {
                int cap = total + 1024;
                lKey = new long[cap]; rKey = new long[cap];
                lOrd = new int[cap]; lF = new int[cap]; lI = new int[cap]; rStart = new int[cap]; rLen = new int[cap]; rNb = new int[cap * 9];
                lX = new float[cap]; lY = new float[cap]; lZ = new float[cap]; lPY = new float[cap]; lRX = new float[cap]; lRY = new float[cap]; lRZ = new float[cap]; lYs = new float[cap]; lRH = new float[cap];
            }
            // order of the canopies: whoever touched down later (or is still falling) lies on top
            if (ctK.Length < fallen.Count) { ctK = new float[fallen.Count + 8]; ctDeep = new bool[fallen.Count + 8]; }
            for (int k = 0; k < fallen.Count; k++) ctK[k] = fallen[k].contact ? fallen[k].contactTime : 1e9f;
            // only canopies that can lie on one that landed earlier need the deep search window
            for (int k = 0; k < fallen.Count; k++)
            {
                ctDeep[k] = false;
                for (int j = 0; j < fallen.Count; j++) if (j != k && ctK[j] < ctK[k] - 0.05f) { ctDeep[k] = true; break; }
            }
            float th = Cfg.layerThickness;
            float win = th + 0.03f;             // a layer this far below still counts as support
            float lo = 0.4f * th;               // "clearly above"
            int m = 0;
            for (int k = 0; k < fallen.Count; k++)
            {
                Fallen f = fallen[k];
                if (f.go == null || f.lyF == null || f.removeT >= 0f) continue;   // a removed canopy no longer supports anything
                if (!f.sleeping)
                {
                    // determined anew every time; remember the old one (points that already had support stay in the test and plunge more easily)
                    float[] tmp = f.lyP; f.lyP = f.lyF; f.lyF = tmp;
                    for (int i = 0; i < f.nCloth; i++) f.lyF[i] = -1e6f;
                }
                // no layers on water: buoyancy carries the cloth there. (With direct lifting, cloth that drifted together was stacked
                // higher frame by frame - a cone grew in the middle of the canopies, up to 1.5 m while sinking.)
                if (f.water) continue;
                if ((f.go.transform.position - p0w).sqrMagnitude > 250f * 250f) continue;
                if (f.sleeping)
                {
                    // canopies at rest only as support, and only when an awake one is in range (expensive after many landings otherwise)
                    bool near = false;
                    foreach (Fallen a in fallen)
                    {
                        if (a.sleeping || a.go == null) continue;
                        float rr = a.size + f.size + 5f;
                        if ((a.go.transform.position - f.go.transform.position).sqrMagnitude < rr * rr) { near = true; break; }
                    }
                    if (!near) continue;
                }
                f.layerM = w2l * f.go.transform.localToWorldMatrix;
                f.layerMinv = f.layerM.inverse;
                Matrix4x4 M = f.layerM;
                bool awakeF = !f.sleeping;
                Vector3[] fp = f.pos, fpr = f.prev, fr0 = f.rest0;
                float[] frh = f.layerR;
                bool[] act = f.active;
                long[] keys = lKey;
                int[] ordA = lOrd, fA = lF, iA = lI;
                float[] xA = lX, yA = lY, zA = lZ, pyA = lPY, rxA = lRX, ryA = lRY, rzA = lRZ, rhA = lRH;
                float invCell = 1f / LayerCell;
                for (int i = 0; i < f.nCloth; i++)
                {
                    if (!act[i]) continue;
                    Vector3 p = fp[i];
                    // only near the ground (points that already had support stay included - otherwise the top of a heap oscillated at the limit)
                    if (awakeF && p.y - GroundAt(f, p, i) > 2.5f && !(f.lyP[i] > -1e5f)) continue;
                    float qx = M.m00 * p.x + M.m01 * p.y + M.m02 * p.z + M.m03;
                    float qy = M.m10 * p.x + M.m11 * p.y + M.m12 * p.z + M.m13;
                    float qz = M.m20 * p.x + M.m21 * p.y + M.m22 * p.z + M.m23;
                    Vector3 pp = fpr[i];
                    Vector3 r0 = fr0[i];
                    keys[m] = LayerKey(Mathf.FloorToInt(qx * invCell), Mathf.FloorToInt(qz * invCell));
                    xA[m] = qx; yA[m] = qy; zA[m] = qz;
                    pyA[m] = M.m10 * pp.x + M.m11 * pp.y + M.m12 * pp.z + M.m13;
                    rxA[m] = r0.x; ryA[m] = r0.y; rzA[m] = r0.z;
                    rhA[m] = frh[i];
                    fA[m] = k; iA[m] = i; ordA[m] = m;
                    m++;
                }
            }
            if (m < 2) return;
            // cells: sorted by key, one run per cell (sorted by height inside); plus the 3x3 neighbour runs
            SortLong(lKey, lOrd, 0, m);
            int nr = 0;
            for (int s = 0; s < m; )
            {
                int e = s + 1;
                while (e < m && lKey[e] == lKey[s]) e++;
                for (int r = s + 1; r < e; r++)
                {
                    int qq = lOrd[r];
                    float yy = lY[qq];
                    int t = r - 1;
                    while (t >= s && lY[lOrd[t]] > yy) { lOrd[t + 1] = lOrd[t]; t--; }
                    lOrd[t + 1] = qq;
                }
                rKey[nr] = lKey[s]; rStart[nr] = s; rLen[nr] = e - s;
                nr++;
                s = e;
            }
            // neighbour cells: one search per neighbour row; the three cells of a row are consecutive in the sort order
            long[] rk = rKey;
            int[] nbr = rNb;
            for (int r = 0; r < nr; r++)
            {
                int ix = (int)(rk[r] >> 32) - 0x40000000, iz = (int)(rk[r] & 0xffffffffL) - 0x40000000;
                for (int dx = -1; dx <= 1; dx++)
                {
                    long k0 = LayerKey(ix + dx, iz - 1);
                    int a = 0, b = nr;      // first run with key >= k0
                    while (a < b) { int mid = (a + b) >> 1; if (rk[mid] < k0) a = mid + 1; else b = mid; }
                    for (int dz = 0; dz < 3; dz++)
                    {
                        long kk = k0 + dz;
                        while (a < nr && rk[a] < kk) a++;
                        nbr[r * 9 + (dx + 1) * 3 + dz] = a < nr && rk[a] == kk ? a : -1;
                    }
                }
            }
            // heights sorted per cell: only the height window is searched (in dense heaps there are many points per cell)
            int[] ord = lOrd, fl = lF, rs = rStart, rl = rLen;
            if (fArr.Length < fallen.Count) fArr = new Fallen[fallen.Count + 8];
            fallen.CopyTo(fArr);
            float[] ys = lYs, yv = lY, py = lPY, xs = lX, zs = lZ, rx = lRX, ry = lRY, rz = lRZ, rh = lRH;
            for (int s = 0; s < m; s++) ys[s] = yv[ord[s]];
            for (int r = 0; r < nr; r++)
            {
                for (int s = rs[r], e = s + rl[r]; s < e; s++)
                {
                    int q = ord[s];
                    int fiq = fl[q];
                    Fallen fq = fArr[fiq];
                    if (fq.sleeping) continue;      // canopies at rest are fixed support
                    float yq = yv[q], pyq = py[q], xq = xs[q], zq = zs[q], rxq = rx[q], ryq = ry[q], rzq = rz[q], rhq = rh[q];
                    int iq = lI[q];
                    bool had = fq.lyP[iq] > -1e5f;          // already had support (for plunging: no on/off at the edge)
                    float ex2 = fq.layerEx2, need = -1e9f, ylo = yq - win, yhi = yq + (ctDeep[fiq] ? LayerDeep : th), ctq = ctK[fiq];
                    for (int nb = r * 9, nbe = nb + 9; nb < nbe; nb++)
                    {
                        int rr = nbr[nb];
                        if (rr < 0) continue;
                        int s2 = rs[rr], e2 = s2 + rl[rr];
                        // from top to bottom: only the highest valid support counts, then the cell is done
                        int a = e2;                 // first index with height > yhi (small cells linearly, large ones by binary search)
                        if (e2 - s2 <= 8) { while (a > s2 && ys[a - 1] > yhi) a--; }
                        else
                        {
                            a = s2;
                            int b = e2;
                            while (a < b) { int mid = (a + b) >> 1; if (ys[mid] <= yhi) a = mid + 1; else b = mid; }
                        }
                        for (int s3 = a - 1; s3 >= s2; s3--)
                        {
                            float yu = ys[s3];
                            if (yu < ylo || yu + th <= need) break;
                            int u = ord[s3];
                            if (u == q) continue;
                            int fu = fl[u];
                            float d = yq - yu, ctu = ctK[fu];
                            bool deepOk = false;
                            if (fu != fiq && ctq > ctu + 0.05f) deepOk = true;      // another canopy that landed earlier: it lies below
                            else if (fu != fiq && ctq < ctu - 0.05f) continue;      // another canopy that landed later: it lies on top
                            else
                            {
                                // same canopy (or landed at the same time): whoever was clearly above at the start of the step
                                // (checked first: this rejects the many neighbours of the same layer cheaply)
                                if (d < -th) continue;
                                float pd = pyq - py[u];
                                if (pd < lo && (pd <= -lo || d < lo)) continue;     // u was on top, or both already interpenetrate
                            }
                            float dx = xs[u] - xq, dz = zs[u] - zq, rad = rh[u] > rhq ? rh[u] : rhq;   // radius of the coarser spot
                            float hd2 = dx * dx + dz * dz;
                            if (hd2 > rad * rad) continue;
                            // plunged (below the canopy that landed earlier) only when almost vertically below it - otherwise the point lies
                            // next to a fold (heap wall) and would be lifted onto it. Points that already had support may be a bit farther
                            // away (otherwise the support switched on and off at the edge).
                            if (deepOk && d < 0f && hd2 > (had ? 0.6f : 0.25f) * rad * rad) continue;
                            if (fu == fiq)
                            {
                                float ax = rx[u] - rxq, ay = ry[u] - ryq, az = rz[u] - rzq;
                                if (ax * ax + ay * ay + az * az < ex2) continue;     // neighbours in the cloth
                            }
                            need = yu + th;
                            break;
                        }
                    }
                    if (need < -1e8f) continue;
                    fq.lyF[iq] = fq.pos[iq].y + (need - yq);    // the simulation step puts it there (higher layers follow next frame)
                }
            }
        }

        // Shared wind per celestial body: depends on game time only, so all canopies in the area (also several
        // on one vessel) blow in the same direction. Strength changes over ~15 min, direction over ~30 min,
        // gusts within seconds. Local system: x = east, z = north.
        static Vector3 SharedWind(CelestialBody b, double ut)
        {
            float s = b.flightGlobalsIndex * 13.7f + 0.37f;
            float pS = Mathf.Clamp01((Mathf.PerlinNoise((float)(ut / 900.0 % 10000.0), s) - 0.2f) / 0.6f);
            float strength = Cfg.windSpeed * Mathf.Lerp(Cfg.windMin, Cfg.windMax, pS);
            float ang = Mathf.PerlinNoise((float)(ut / 1800.0 % 10000.0), s + 5.1f) * 4f * Mathf.PI;
            float gust = 0.6f + 0.8f * Mathf.PerlinNoise((float)(ut * 0.35 % 10000.0), s + 9.7f);
            return new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * (strength * gust);
        }

        // turbulence once per frame instead of every sub-step (3 Perlin calls per point are expensive)
        static void ComputeTurbulence(Fallen f, float t)
        {
            int n = f.pos.Length;
            if (f.turb == null || f.turb.Length != n) f.turb = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                if (!f.active[i] || f.onGround[i]) continue;
                Vector3 p = f.pos[i];
                float nx = Mathf.PerlinNoise(p.x * 0.35f + t * 0.9f, f.seed + p.z * 0.35f) - 0.5f;
                float nz = Mathf.PerlinNoise(p.z * 0.35f - t * 0.8f, f.seed * 1.7f + p.y * 0.35f) - 0.5f;
                float ny = Mathf.PerlinNoise(p.y * 0.35f + t * 1.2f, f.seed * 0.3f + p.x * 0.35f) - 0.5f;
                f.turb[i] = new Vector3(nx, ny * 0.6f, nz);
            }
        }

        static List<Vector3> nrmBuf = new List<Vector3>();
        static void ComputeNormals(Fallen f)
        {
            if (f.nrm == null || f.nrm.Length != f.nCloth) f.nrm = new Vector3[f.nCloth];
            for (int i = 0; i < f.nCloth; i++) f.nrm[i] = Vector3.zero;
            foreach (ClothMesh cm in f.parts)
            {
                cm.mesh.GetNormals(nrmBuf);
                if (nrmBuf.Count != cm.map.Length) continue;
                for (int k = 0; k < cm.map.Length; k++)
                {
                    // double-sided surfaces: align the normals (the sign does not matter for the sail force)
                    Vector3 nv = nrmBuf[k];
                    int pi = cm.map[k];
                    if (Vector3.Dot(f.nrm[pi], nv) < 0f) nv = -nv;
                    f.nrm[pi] += nv;
                }
            }
            for (int i = 0; i < f.nCloth; i++) { float m = f.nrm[i].magnitude; f.nrm[i] = m > 1e-5f ? f.nrm[i] / m : Vector3.up; }
        }

        static float GroundAt(Fallen f, Vector3 p, int i)
        {
            if (i >= 0 && f.gY[i] > -1e5f) return f.gY[i];
            float dx = Mathf.Clamp(p.x - f.groundCX, -f.size, f.size), dz = Mathf.Clamp(p.z - f.groundCZ, -f.size, f.size);
            return f.groundY + f.slopeX * dx + f.slopeZ * dz;
        }

        // Ground height under every single point (steps, edges, buildings). Only part of the points per frame.
        static void UpdateParticleGround(Fallen f)
        {
            if (f.water || f.sleeping) return;
            Transform tr = f.go.transform;
            Vector3 up = tr.up;
            int n = f.pos.Length;
            int budget = Mathf.Min(n, rayBudgetPerChute);
            RaycastHit hit;
            for (int k = 0; k < budget; k++)
            {
                int i = f.rayCursor;
                f.rayCursor = (f.rayCursor + 1) % n;
                if (!f.active[i]) continue;
                Vector3 wpnt = tr.TransformPoint(f.pos[i]);
                float plane = GroundAt(f, f.pos[i], -1);
                // test only near the ground; high up the plane is good enough
                if (f.pos[i].y - plane > 6f) { f.gY[i] = -1e6f; continue; }
                if (Physics.Raycast(wpnt + up * 3f, -up, out hit, 40f, TerrainMask))
                {
                    float gy = tr.InverseTransformPoint(hit.point).y;
                    f.gY[i] = Mathf.Abs(gy - plane) < 15f ? gy : -1e6f;
                }
                else f.gY[i] = -1e6f;
            }
        }

        float SampleGround(Fallen f, CelestialBody b, Vector3 local, float fallback)
        {
            Vector3d wd = (Vector3d)f.go.transform.TransformPoint(local);
            bool w;
            double g = GroundAltAt(b, b.GetLatitude(wd), b.GetLongitude(wd), f.alt + local.y, out w);
            return w ? fallback : (float)(g - f.alt);
        }

        static float WaveY(float x, float z, float t, float seed)
        {
            return 0.10f * Mathf.Sin(x * 0.35f + t * 1.1f + seed) + 0.06f * Mathf.Sin(z * 0.5f - t * 1.6f + seed * 2f)
                 + 0.03f * Mathf.Sin((x + z) * 1.3f + t * 2.3f);
        }

        static void Step(Fallen f, float h, float t, float age)
        {
            int n = f.pos.Length;
            float g = f.gravity;
            float kAir = f.atmo ? g / Mathf.Max(0.5f, Cfg.cutTerminalSpeed) : 0f;
            float waterY = -(float)f.alt;
            float memory = f.noMemory ? 0f : Mathf.Clamp01(1f - age / Mathf.Max(0.1f, Cfg.deflateTime)) * 0.06f;
            float turbBase = Cfg.turbulence * (f.contact ? (f.water ? 0.25f : 0.1f) : 1f);
            // after touchdown damp gradually more so it comes to rest
            float settle = f.contact ? Mathf.Clamp01((t - f.contactTime) / Mathf.Max(0.5f, Cfg.settleTime * 4f)) : 0f;
            bool onSeabed = f.water && f.seabedY > -1e5f && f.sink > (-(float)f.alt - f.seabedY);
            float damp = Mathf.Clamp01(1f - Cfg.damping);
            float dampGround = Mathf.Clamp01(damp - ((!f.water || onSeabed) ? 0.12f * settle : 0f));
            // gusts; after touchdown on land the wind dies down over a few seconds so the canopy comes to rest
            float calm = (f.contact && !f.water) ? Mathf.Lerp(1f, 0.15f, Mathf.Clamp01((t - f.contactTime) / 8f)) : 1f;
            Vector3 wind = f.wind * calm;
            bool anyContact = false;
            float maxV = 0f;
            int nFast = 0;
            float press = f.atmo ? Cfg.inflatePressure * Mathf.Clamp01(1f - age / Mathf.Max(0.1f, Cfg.deflateTime)) : 0f;
            if (press > 0f)
            {
                Vector3 cs = Vector3.zero; int nc = 0;
                for (int i = 0; i < f.nCloth; i++) if (f.active[i]) { cs += f.pos[i]; nc++; }
                f.cenStep = cs / Mathf.Max(1, nc);
            }

            for (int i = 0; i < n; i++)
            {
                if (!f.active[i]) continue;
                Vector3 p = f.pos[i];
                Vector3 v = f.vel[i] * (f.onGround[i] ? dampGround : damp);
                bool rope = i >= f.nCloth;
                v.y -= g * h;
                float surf = waterY + WaveY(p.x, p.z, t, f.seed) - f.sink;
                if (f.water && p.y < surf + 0.03f)
                {
                    float depth = surf - p.y;
                    float buoy = Mathf.Lerp(rope ? 1.4f : 2.0f, rope ? 0.8f : 0.9f, f.soak);
                    v.y += g * buoy * Mathf.Clamp01(depth / 0.12f) * h;
                    v *= Mathf.Exp(-4f * h);
                    v.y *= Mathf.Exp(-8f * h);
                }
                else if (f.onGround[i])
                {
                    v.x *= 0.5f; v.z *= 0.5f;
                }
                else if (kAir > 0f)
                {
                    // ropes have little drag, cloth a lot
                    Vector3 rel = wind - v;
                    if (rope || f.nrm == null) v += rel * Mathf.Clamp01((rope ? 0.25f : 1f) * kAir * h);
                    else
                    {
                        Vector3 nn = f.nrm[i];
                        v += rel * Mathf.Clamp01(Cfg.isoDrag * kAir * h);
                        v += nn * (Vector3.Dot(nn, rel) * Mathf.Clamp01(Cfg.sailDrag * kAir * h));
                        if (f.outAcc > 0f && age < 3f)
                        {
                            float hf = Mathf.Clamp01((p.y - GroundAt(f, p, i)) / 10f);
                            v += f.outDir * (f.outAcc * hf * hf * (1f - age / 3f) * h);
                        }
                        if (press > 0f)
                        {
                            Vector3 outw = p - f.cenStep;
                            v += (Vector3.Dot(nn, outw) >= 0f ? nn : -nn) * (press * h);
                        }
                    }
                    float tb = turbBase * (rope ? 0.3f : 1f);
                    v += f.turb[i] * (tb * h);
                }
                f.prev[i] = p;
                f.pos[i] = p + v * h;
            }

            // stiffer on the ground: a few large folds instead of many tight crumples that interpenetrate
            float gp = (f.contact && !f.water) ? Mathf.Clamp01((t - f.contactTime) / 1.0f) : 0f;
            float compK = Mathf.Lerp(Cfg.compressStiffness, Cfg.groundCompressStiffness, gp);
            float bendK = Mathf.Lerp(Cfg.bendStiffness, Cfg.groundBendStiffness, gp);
            float bendT = Mathf.Lerp(0.85f, 0.95f, gp);
            // keep lengths (cloth, seams, ropes)
            int it = Cfg.clothIterations;
            for (int k = 0; k < it; k++)
            {
                for (int e = 0; e < f.ea.Length; e++)
                {
                    int a = f.ea[e], b = f.eb[e];
                    Vector3 d = f.pos[b] - f.pos[a];
                    float len = d.magnitude;
                    if (len < 1e-6f) continue;
                    float diff = len - f.el[e];
                    if (diff < 0f) { if (f.et[e]) continue; diff *= compK; }
                    Vector3 corr = d * (0.5f * diff / len);
                    f.pos[a] += corr;
                    f.pos[b] -= corr;
                }
                if (f.pin >= 0) f.pos[f.pin] = f.pinLocal;
            }
            // bending: once per step, only resists folding
            float kb = Mathf.Clamp01(1.5f * bendK);
            if (kb > 0f)
                for (int e = 0; e < f.ba.Length; e++)
                {
                    int a = f.ba[e], b = f.bb[e];
                    Vector3 d = f.pos[b] - f.pos[a];
                    float len = d.magnitude;
                    if (len < 1e-6f || len >= bendT * f.bl[e]) continue;
                    Vector3 corr = d * (0.5f * kb * (len - bendT * f.bl[e]) / len);
                    f.pos[a] += corr;
                    f.pos[b] -= corr;
                }
            if (f.pin >= 0) f.pos[f.pin] = f.pinLocal;

            if (memory > 0f)
            {
                Vector3 c2 = Vector3.zero;
                int na = 0;
                for (int i = 0; i < f.nCloth; i++) if (f.active[i]) { c2 += f.pos[i]; na++; }
                c2 /= Mathf.Max(1, na);
                for (int i = 0; i < f.nCloth; i++) if (f.active[i]) f.pos[i] = Vector3.Lerp(f.pos[i], c2 + f.rest0[i], memory);
            }

            for (int i = 0; i < n; i++)
            {
                if (!f.active[i]) continue;
                Vector3 p = f.pos[i];
                f.onGround[i] = false;
                bool clamped = false;
                if (!f.water || p.y < f.seabedY + 0.05f)
                {
                    float gy = (f.water ? f.seabedY : GroundAt(f, p, i) + f.lift) + (i < f.nCloth ? f.offs[i] : 0.01f);
                    // lies on another cloth layer (LayerCloth): it carries the point like the ground, but on new contact lifts it
                    // at most 2 cm per step (otherwise it jumped up and dragged the neighbours along)
                    if (!f.water && i < f.nCloth && f.lyF != null && f.lyF[i] > gy && p.y <= f.lyF[i]) gy = Mathf.Max(gy, Mathf.Min(f.lyF[i], p.y + 0.02f));
                    // fixed stack height at first ground contact only without the running cloth layers (LayerCloth, layerThickness > 0)
                    if (p.y <= gy && i < f.nCloth && !f.stacked[i] && Cfg.layerThickness <= 0f)
                    {
                        f.stacked[i] = true;
                        Vector3 bf = f.body.transform.InverseTransformPoint(f.go.transform.TransformPoint(new Vector3(p.x, gy, p.z)));
                        long key = ((long)Mathf.FloorToInt(bf.x / StackCell) * 73856093L) ^ ((long)Mathf.FloorToInt(bf.y / StackCell) * 19349663L) ^ ((long)Mathf.FloorToInt(bf.z / StackCell) * 83492791L);
                        float sh;
                        sharedStack.TryGetValue(key, out sh);
                        f.offs[i] += sh;
                        // Stack height by cloth area: one full layer over the cell = one layer thickness. Before, every point counted
                        // a whole layer -> in the dense apex the points piled up into spikes (broken-up middle).
                        sharedStack[key] = sh + StackLayer * Mathf.Clamp(f.area[i] / (StackCell * StackCell), 0.02f, 1.5f);
                        gy += sh;
                    }
                    if (p.y <= gy)
                    {
                        Vector3 pr = f.prev[i];
                        p.y = gy;
                        p.x = Mathf.Lerp(p.x, pr.x, Cfg.groundFriction);
                        p.z = Mathf.Lerp(p.z, pr.z, Cfg.groundFriction);
                        // static friction: suppress small sliding motions completely
                        float hx = p.x - pr.x, hz = p.z - pr.z;
                        if (hx * hx + hz * hz < 4e-6f) { p.x = pr.x; p.z = pr.z; }
                        f.onGround[i] = true;
                        clamped = true;
                        if (i < f.nCloth) anyContact = true;   // only cloth counts (the ropes touched the ground early -> the wind died down too early)
                    }
                }
                else if (p.y < waterY + 0.3f && i < f.nCloth) anyContact = true;
                if (i == f.pin) p = f.pinLocal;
                f.pos[i] = p;
                Vector3 nv = (p - f.prev[i]) / h;
                if (clamped && nv.y > 0f) nv.y = 0f;   // lifting out of the ground/cloth layer gives no upward momentum
                if (f.contact)
                {
                    float vmax = f.water ? 2f : 3f;
                    float m2 = nv.sqrMagnitude;
                    if (m2 > vmax * vmax) nv *= vmax / Mathf.Sqrt(m2);
                }
                f.vel[i] = nv;
                float sm = nv.sqrMagnitude; if (sm > maxV) maxV = sm;
                if (sm > 0.01f) nFast++;
            }
            if (anyContact && !f.contact)
            {
                f.contact = true;
                f.contactTime = t;
                Cfg.Log("Canopy touches " + (f.water ? "water" : "ground"));
            }
            if (f.contact && (!f.water || onSeabed))
            {
                // at rest = all points slower than 0.1 m/s, or all but very few (single twitching points - e.g. at the edge
                // of a heap - must not delay sleeping until it is forced; once asleep they stand still too)
                bool still = maxV < 0.01f || (nFast <= Mathf.Max(2, (int)(0.003f * n)) && maxV < 1f);
                if (t - f.contactTime > Cfg.settleMax && (maxV < 0.09f || still)) { f.sleeping = true; Cfg.Log("Canopy at rest (forced after " + Cfg.settleMax.ToString("F0") + " s)"); }
                else if (still) { if (f.stillSince == 0f) f.stillSince = t; else if (t - f.stillSince > 3f) { f.sleeping = true; Cfg.Log("Canopy at rest (simulation paused)"); } }
                else f.stillSince = 0f;
            }
        }
    }

    internal class ClothMesh
    {
        public Mesh mesh;
        public int[] map;
        public MeshRenderer r;
        public int[] tri;                   // all triangles (render vertices), after removing the small pieces
        public Vector3[] nbuf;              // normals per render vertex (RenderNormals)
        public float[] rnx, rny, rnz, unx, uny, unz;   // normals per render vertex from its own triangles (raw, normalized)
        int tanWait;
        Vector3[] buf;
        public ClothMesh(Mesh m, int[] map, MeshRenderer r) { mesh = m; this.map = map; this.r = r; buf = new Vector3[map.Length]; }
        public void FinishTopology()
        {
            tri = mesh.triangles;
            nbuf = new Vector3[map.Length];
        }
        // Set positions and normals. Tangents (the cloth shaders have a normal map; without tangents the lighting was
        // almost only light/dark) every 3rd time - they turn slowly, and recomputing them is the most expensive part
        public void Apply(Vector3[] pos)
        {
            for (int i = 0; i < map.Length; i++) buf[i] = pos[map[i]];
            mesh.vertices = buf;
            mesh.normals = nbuf;
            if (--tanWait < 0) { mesh.RecalculateTangents(); tanWait = 2; }
            mesh.RecalculateBounds();
        }
    }

    // Right-click menu on every parachute part (attached by ModuleManager, Patches/ChuteFXRemove.cfg): remove cut
    // canopies, e.g. when one covers the capsule. The cloth has no collider - clicks go through it
    // to the part below.
    public class ChuteFXRemover : PartModule
    {
        [KSPEvent(guiActive = true, guiActiveUnfocused = true, unfocusedRange = 30f, active = false, guiName = "#ChuteFX_RemoveCanopy")]
        public void RemoveThis()
        {
            if (ChuteFXController.Instance != null) ChuteFXController.Instance.Remove(part, false);
            next = 0f;
        }

        [KSPEvent(guiActive = true, guiActiveUnfocused = true, unfocusedRange = 30f, active = false, guiName = "#ChuteFX_RemoveAllCanopies")]
        public void RemoveAll()
        {
            if (ChuteFXController.Instance != null) ChuteFXController.Instance.Remove(part, true);
            next = 0f;
        }

        float next;

        // show the entries only when a cut canopy of this part lies around ("all" only when the vessel has more)
        void Update()
        {
            if (!HighLogic.LoadedSceneIsFlight || Time.time < next) return;
            next = Time.time + 0.25f;
            ChuteFXController c = ChuteFXController.Instance;
            int own = c != null ? c.CountRemovable(part, false) : 0;
            int all = c != null ? c.CountRemovable(part, true) : 0;
            Events["RemoveThis"].active = own > 0;
            BaseEvent ev = Events["RemoveAll"];
            ev.active = all > own;
            string name = KSP.Localization.Localizer.Format("#ChuteFX_RemoveAllCanopies", all);   // language of the game (Localization/ChuteFX.cfg)
            if (ev.guiName != name) ev.guiName = name;
        }
    }
}
