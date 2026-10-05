// Normal test: render normals (RenderNormals) on a double-walled canopy. Outer and inner skin meet in a fold at the
// apex vent and at the hem (shared cloth points, but separate render vertices), plus a UV seam.
// Compares the normals of the original (Unity per render vertex = what the open canopy shows) with RenderNormals.
// Expected: equal at the folds (no normal standing across), both sides of the UV seam equal (smooth).
// Run: Unity -batchmode -quit -projectPath <project> -executeMethod ChuteFXNormal.NormalTest.Run -logFile normal.log
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ChuteFXNormal
{
    internal static class Cfg
    {
        public static int renderSmooth = 0;
        public static bool debugLog = true;
        public static void Log(string s) { Debug.Log("[ChuteFX] " + s); }
    }

/*@FALLEN@*/

/*@CLOTHMESH@*/

    public static class NormalTest
    {
/*@FUNCS@*/

        public static void Run()
        {
            // cloth points: outer skin rings 0..R, inner skin rings 1..R-1 (rings 0 and R shared = folds)
            int R = 10, S = 24;
            float a0 = 0.4f, a1 = 3f, Rs = 3.6f;
            List<Vector3> P = new List<Vector3>();
            int[,] outer = new int[R + 1, S], inner = new int[R + 1, S];
            for (int j = 0; j <= R; j++)
                for (int k = 0; k < S; k++)
                {
                    float rr = Mathf.Lerp(a0, a1, (float)j / R), an = k * Mathf.PI * 2f / S;
                    float y = Mathf.Sqrt(Rs * Rs - rr * rr);
                    Vector3 p = new Vector3(Mathf.Cos(an) * rr, y, Mathf.Sin(an) * rr);
                    outer[j, k] = P.Count; P.Add(p);
                }
            for (int j = 0; j <= R; j++)
                for (int k = 0; k < S; k++)
                {
                    if (j == 0 || j == R) { inner[j, k] = outer[j, k]; continue; }
                    Vector3 p = P[outer[j, k]];
                    inner[j, k] = P.Count; P.Add(p - p.normalized * 0.15f);   // 15 cm inside
                }
            // render vertices: separate ones per skin (with a UV seam: column S twice), outer wound outward, inner inward
            List<Vector3> rv = new List<Vector3>(); List<int> map = new List<int>(); List<int> tri = new List<int>(); List<Vector2> uv = new List<Vector2>();
            int[] skinStart = new int[2];
            for (int sk = 0; sk < 2; sk++)
            {
                skinStart[sk] = rv.Count;
                for (int j = 0; j <= R; j++)
                    for (int k = 0; k <= S; k++)
                    {
                        int p = sk == 0 ? outer[j, k % S] : inner[j, k % S];
                        rv.Add(P[p]); map.Add(p); uv.Add(new Vector2((float)k / S, (float)j / R));
                    }
                for (int j = 0; j < R; j++)
                    for (int k = 0; k < S; k++)
                    {
                        int A = skinStart[sk] + j * (S + 1) + k, B = A + 1, C = A + S + 1, D = C + 1;
                        if (sk == 0) { tri.Add(A); tri.Add(B); tri.Add(C); tri.Add(B); tri.Add(D); tri.Add(C); }
                        else { tri.Add(A); tri.Add(C); tri.Add(B); tri.Add(B); tri.Add(C); tri.Add(D); }
                    }
            }
            Mesh m = new Mesh(); m.SetVertices(rv); m.SetUVs(0, uv); m.SetTriangles(tri, 0); m.RecalculateNormals();
            Vector3[] orig = m.normals;   // this is how the open canopy is drawn (per render vertex)
            GameObject go = new GameObject("t");
            ClothMesh cm = new ClothMesh(m, map.ToArray(), go.AddComponent<MeshRenderer>());
            cm.FinishTopology();
            Fallen f = new Fallen();
            f.go = go; f.nCloth = P.Count; f.pos = P.ToArray(); f.onGround = new bool[P.Count];
            f.parts.Add(cm);
            BuildRenderTopology(f);
            SmoothRender(f);
            RenderNormals(f);
            float worstFold = 0f, worstAll = 0f, worstSeam = 0f;
            for (int sk = 0; sk < 2; sk++)
                for (int j = 0; j <= R; j++)
                    for (int k = 0; k <= S; k++)
                    {
                        int v = skinStart[sk] + j * (S + 1) + k;
                        float angA = Vector3.Angle(orig[v], cm.nbuf[v]);
                        worstAll = Mathf.Max(worstAll, angA);
                        if ((j == 0 || j == R) && k != 0 && k != S) worstFold = Mathf.Max(worstFold, angA);   // fold without the seam column
                        if (k == S) worstSeam = Mathf.Max(worstSeam, Vector3.Angle(cm.nbuf[v], cm.nbuf[skinStart[sk] + j * (S + 1)]));
                    }
            // for comparison: naive averaging over all render vertices of a point (front side aligned) at the folds
            float worstNaive = 0f;
            for (int k = 0; k < S; k++)
            {
                int p = outer[0, k];
                Vector3 sum = Vector3.zero;
                for (int v = 0; v < rv.Count; v++) if (map[v] == p) sum += orig[v];
                int vo = skinStart[0] + k;
                worstNaive = Mathf.Max(worstNaive, Vector3.Angle(orig[vo], sum.sqrMagnitude > 1e-8f ? sum.normalized : Vector3.up));
            }
            Debug.Log("CHUTENORMAL folds (without seam): largest deviation from the original " + worstFold.ToString("F1") + " deg (averaging over both skins: " + worstNaive.ToString("F1") +
                      " deg) | everywhere (the original is hard at the seam) " + worstAll.ToString("F1") + " deg | UV seam: difference of both sides " + worstSeam.ToString("F2") + " deg | " +
                      (worstFold < 5f && worstSeam < 0.5f ? "OK" : "FAIL"));
            UnityEngine.Object.DestroyImmediate(go);
        }
    }
}
