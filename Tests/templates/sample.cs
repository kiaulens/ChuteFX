// Sample test: cluster geometry in the air. A skinned dome mesh on two bones; after rotating/scaling the bones (as when
// the canopy deploys) the self-computed samples (DeformMesh.SampleWorld) must match Unity's skinning (BakeMesh),
// immediately and without rendering.
// Run: Unity -batchmode -quit -projectPath <project> -executeMethod ChuteFXSample.SampleTest.Run -logFile sample.log
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ChuteFXSample
{
    internal static class Cfg
    {
        public static bool recalcNormals = true;
        public static bool debugLog = true;
        public static void Log(string s) { Debug.Log("[ChuteFX] " + s); }
    }

/*@DEFORMMESH@*/

    public static class SampleTest
    {
        public static void Run()
        {
            GameObject root = new GameObject("canopy");
            GameObject b0 = new GameObject("B_ParachuteNode001"); b0.transform.SetParent(root.transform, false);
            GameObject b1 = new GameObject("B_ParachuteNode002"); b1.transform.SetParent(b0.transform, false); b1.transform.localPosition = new Vector3(0f, 1f, 0f);
            GameObject go = new GameObject("Canopy001"); go.transform.SetParent(root.transform, false);
            SkinnedMeshRenderer smr = go.AddComponent<SkinnedMeshRenderer>();
            // dome: 12 rings x 24 gores, weighted to b0 at the bottom and b1 at the top
            List<Vector3> V = new List<Vector3>(); List<BoneWeight> W = new List<BoneWeight>(); List<int> T = new List<int>();
            int rings = 12, segs = 24;
            for (int j = 0; j < rings; j++)
                for (int k = 0; k < segs; k++)
                {
                    float r = Mathf.Lerp(0.2f, 1f, (float)j / (rings - 1)), a = k * Mathf.PI * 2f / segs;
                    float y = Mathf.Sqrt(Mathf.Max(0f, 1.1f - r * r));
                    V.Add(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r));
                    BoneWeight bw = new BoneWeight(); float w1 = Mathf.Clamp01(y);
                    bw.boneIndex0 = 0; bw.weight0 = 1f - w1; bw.boneIndex1 = 1; bw.weight1 = w1; W.Add(bw);
                }
            for (int j = 0; j + 1 < rings; j++)
                for (int k = 0; k < segs; k++)
                {
                    int A = j * segs + k, B = j * segs + (k + 1) % segs, C = A + segs, D = B + segs;
                    T.Add(A); T.Add(B); T.Add(C); T.Add(B); T.Add(D); T.Add(C);
                }
            Mesh m = new Mesh();
            m.SetVertices(V); m.boneWeights = W.ToArray(); m.SetTriangles(T, 0);
            m.bindposes = new Matrix4x4[] { b0.transform.worldToLocalMatrix * go.transform.localToWorldMatrix, b1.transform.worldToLocalMatrix * go.transform.localToWorldMatrix };
            m.RecalculateBounds(); m.RecalculateNormals();
            smr.sharedMesh = m; smr.bones = new Transform[] { b0.transform, b1.transform }; smr.rootBone = b0.transform;
            smr.quality = SkinQuality.Bone4;
            DeformMesh d = DeformMesh.Create(go.transform, m, smr, root.transform.position, Vector3.up, root.transform);
            Debug.Log("CHUTESAMPLE samples: " + d.samp.Length + " of " + V.Count + " vertices, bones " + (d.sb != null ? d.sb.Length : -1));
            Vector3[] buf = new Vector3[128];
            float worst = 0f;
            for (int pose = 0; pose < 4; pose++)
            {
                // deployed pose: bones scaled and rotated, root (canopy) rotated and moved
                b0.transform.localScale = Vector3.one * (1f + pose);
                b1.transform.localRotation = Quaternion.Euler(10f * pose, 25f * pose, 0f);
                root.transform.rotation = Quaternion.Euler(0f, 0f, 30f * pose);
                root.transform.position = new Vector3(pose * 3f, 100f, 0f);
                int n = d.SampleWorld(buf);
                Mesh baked = new Mesh();
                smr.BakeMesh(baked);
                Vector3[] bv = baked.vertices;
                float err = 0f;
                for (int k = 0; k < n; k++)
                {
                    Vector3 w = go.transform.TransformPoint(bv[d.samp[k]]);
                    err = Mathf.Max(err, (w - buf[k]).magnitude);
                }
                worst = Mathf.Max(worst, err);
                Vector3 c = Vector3.zero; for (int k = 0; k < n; k++) c += buf[k]; c /= n;
                float r2 = 0f; for (int k = 0; k < n; k++) r2 = Mathf.Max(r2, (buf[k] - c).sqrMagnitude);
                Debug.Log("CHUTESAMPLE pose " + pose + ": largest deviation from BakeMesh " + err.ToString("F5") + " m, sphere center " + c.ToString("F2") + " radius " + Mathf.Sqrt(r2).ToString("F2"));
                UnityEngine.Object.DestroyImmediate(baked);
            }
            Debug.Log("CHUTESAMPLE result: " + (worst < 1e-3f ? "OK" : "FAIL") + " (largest deviation " + worst.ToString("F5") + " m)");
            UnityEngine.Object.DestroyImmediate(root);
        }
    }
}
