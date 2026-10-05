// Island test: piece analysis (StitchIslands/StitchLayers) on a synthetic three-cap canopy like the ReStock Mk16-XL.
// Every cap must keep its own apex piece; line-end caps and a far-away piece must be removed.
// Run: Unity -batchmode -quit -projectPath <project> -executeMethod ChuteFXTest.IslandTest.Run -logFile island.log
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ChuteFXTest
{
    internal static class Cfg
    {
        public static int minPieceSize = 20;
        public static bool debugLog = true;
        public static void Log(string s) { Debug.Log("[ChuteFX] " + s); }
    }

    public static class IslandTest
    {
/*@FUNCS@*/

        static void Dome(List<Vector3> P, List<int[]> tris, List<int> meshOf, int mesh, Vector3 c, Quaternion rot, float r0, float r1, float R, float hOff, int rings, int segs, bool cap)
        {
            int start = P.Count;
            if (cap) { P.Add(c + rot * new Vector3(0f, hOff + Mathf.Sqrt(R * R), 0f)); meshOf.Add(mesh); }
            int ringStart = P.Count;
            for (int j = 0; j <= rings; j++)
            {
                float rr = Mathf.Lerp(r0, r1, (float)j / rings);
                float h = hOff + Mathf.Sqrt(Mathf.Max(0f, R * R - rr * rr));
                for (int k = 0; k < segs; k++)
                {
                    float a = k * Mathf.PI * 2f / segs;
                    P.Add(c + rot * new Vector3(Mathf.Cos(a) * rr, h, Mathf.Sin(a) * rr));
                    meshOf.Add(mesh);
                }
            }
            for (int j = 0; j < rings; j++)
                for (int k = 0; k < segs; k++)
                {
                    int a = ringStart + j * segs + k, b = ringStart + j * segs + (k + 1) % segs;
                    int c2 = a + segs, d = b + segs;
                    tris.Add(new int[] { a, b, c2 });
                    tris.Add(new int[] { b, d, c2 });
                }
            if (cap)
                for (int k = 0; k < segs; k++) tris.Add(new int[] { start, ringStart + k, ringStart + (k + 1) % segs });
        }

        public static void Run()
        {
            List<Vector3> P = new List<Vector3>();
            List<int[]> tris = new List<int[]>();
            List<int> meshOf = new List<int>();
            System.Random rnd = new System.Random(3);
            for (int m = 0; m < 3; m++)
            {
                float a = m * Mathf.PI * 2f / 3f;
                Vector3 c = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 6.5f;
                Quaternion rot = Quaternion.AngleAxis(20f, Vector3.Cross(Vector3.up, c.normalized));
                // cap with an apex vent (0.6 m), apex piece separate above it (gap ~0.3 m)
                Dome(P, tris, meshOf, m, c, rot, 0.6f, 5.5f, 6f, 0f, 24, 38, false);
                Dome(P, tris, meshOf, m, c, rot, 0.05f, 0.3f, 6f, 0.15f, 4, 29, true);
                // small caps at the line ends (down at the knot)
                for (int q = 0; q < 16; q++)
                {
                    Vector3 b = new Vector3((float)rnd.NextDouble() - 0.5f, -14f, (float)rnd.NextDouble() - 0.5f) * 1f + c * 0.2f;
                    int s0 = P.Count;
                    for (int t = 0; t < 6; t++) { P.Add(b + new Vector3(0.03f * t, 0.01f * (t % 2), 0.02f * (t % 3))); meshOf.Add(m); }
                    for (int t = 0; t < 4; t++) tris.Add(new int[] { s0 + t, s0 + t + 1, s0 + t + 2 });
                }
            }
            // one piece far away
            { int s0 = P.Count; for (int t = 0; t < 16; t++) { P.Add(new Vector3(20f + 0.1f * t, 2f, 0.05f * (t % 4))); meshOf.Add(0); } for (int t = 0; t < 14; t++) tris.Add(new int[] { s0 + t, s0 + t + 1, s0 + (t + 2) % 16 }); }
            int n = P.Count;
            Dictionary<long, float> edges = new Dictionary<long, float>();
            Dictionary<long, float> bends = new Dictionary<long, float>();
            List<bool> used = new List<bool>();
            for (int i = 0; i < n; i++) used.Add(false);
            foreach (int[] t in tris)
            {
                used[t[0]] = used[t[1]] = used[t[2]] = true;
                AddEdgeLen(edges, t[0], t[1], (P[t[0]] - P[t[1]]).magnitude);
                AddEdgeLen(edges, t[1], t[2], (P[t[1]] - P[t[2]]).magnitude);
                AddEdgeLen(edges, t[2], t[0], (P[t[2]] - P[t[0]]).magnitude);
            }
            int dropped, groups; int[] groupOf;
            int e0 = edges.Count;
            int st = StitchIslands(P, edges, bends, n, used, meshOf.ToArray(), out dropped, out groupOf, out groups);
            int ls = StitchLayers(P, edges, n, used, groupOf, groups);
            // check: every cap keeps its apex piece (same group), small pieces removed
            int[] perGroup = new int[groups];
            for (int i = 0; i < n; i++) if (groupOf[i] >= 0) perGroup[groupOf[i]]++;
            string pg = ""; for (int g = 0; g < groups; g++) pg += perGroup[g] + " ";
            bool ok = groups == 3;
            for (int m = 0; m < 3; m++)
            {
                // apex point (highest point of mesh m) and a point of the cap below it must share a group
                int top = -1; float hy = -1e9f;
                for (int i = 0; i < n; i++) if (meshOf[i] == m && P[i].y > hy && P[i].y > -5f) { hy = P[i].y; top = i; }
                int capPoint = -1;
                for (int i = 0; i < n; i++) if (meshOf[i] == m && P[i].y > -5f && P[i].y < hy - 1f) { capPoint = i; break; }
                bool same = top >= 0 && capPoint >= 0 && groupOf[top] >= 0 && groupOf[top] == groupOf[capPoint];
                Debug.Log("CHUTETEST cap " + m + ": apex in group " + groupOf[top] + ", cap in group " + groupOf[capPoint] + (same ? " OK" : " FAIL"));
                ok &= same;
            }
            Debug.Log("CHUTETEST result: " + (ok ? "OK" : "FAIL") + " groups=" + groups + " points/group=" + pg + " removed=" + dropped + " stitches=" + st + " layers=" + ls + " edges " + e0 + "->" + edges.Count);
        }
    }
}
