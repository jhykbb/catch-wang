using System.Collections.Generic;
using UnityEngine;

namespace StockDice.View
{
    /// <summary>
    /// 둥근 모서리의 로우폴리 메시를 코드로 만든다. 외부 모델 없이도 깔끔한 룩을 내기 위함.
    /// 같은 파라미터의 메시는 캐시해서 재사용한다.
    /// </summary>
    public static class MeshFactory
    {
        private static readonly Dictionary<string, Mesh> Cache = new Dictionary<string, Mesh>();

        public static Mesh RoundedBox(Vector3 size, float radius, int segments = 3)
        {
            string key = string.Format("rbox_{0:0.###}_{1:0.###}_{2:0.###}_{3:0.###}_{4}", size.x, size.y, size.z, radius, segments);
            Mesh cached;
            if (Cache.TryGetValue(key, out cached) && cached != null) return cached;

            Vector3 half = size * 0.5f;
            radius = Mathf.Max(0.0001f, Mathf.Min(radius, Mathf.Min(half.x, Mathf.Min(half.y, half.z)) - 0.0001f));
            Vector3 inner = half - Vector3.one * radius;
            segments = Mathf.Max(1, segments);

            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            float[] Samples(float innerHalf)
            {
                var list = new float[(segments + 1) * 2];
                for (int k = 0; k <= segments; k++)
                {
                    float phi = Mathf.PI * 0.5f * k / segments;
                    list[k] = -innerHalf - radius * Mathf.Cos(phi);
                    list[segments + 1 + k] = innerHalf + radius * Mathf.Sin(phi);
                }
                return list;
            }

            float[] sx = Samples(inner.x), sy = Samples(inner.y), sz = Samples(inner.z);

            // 면마다 (법선 축, u 축, v 축)
            AddFace(Vector3.right, 0, 1, 2, sy, sz, half, inner, radius, verts, normals, uvs, tris);
            AddFace(Vector3.left, 0, 1, 2, sy, sz, half, inner, radius, verts, normals, uvs, tris);
            AddFace(Vector3.up, 1, 0, 2, sx, sz, half, inner, radius, verts, normals, uvs, tris);
            AddFace(Vector3.down, 1, 0, 2, sx, sz, half, inner, radius, verts, normals, uvs, tris);
            AddFace(Vector3.forward, 2, 0, 1, sx, sy, half, inner, radius, verts, normals, uvs, tris);
            AddFace(Vector3.back, 2, 0, 1, sx, sy, half, inner, radius, verts, normals, uvs, tris);

            var mesh = new Mesh { name = key };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            Cache[key] = mesh;
            return mesh;
        }

        private static void AddFace(Vector3 normal, int nAxis, int uAxis, int vAxis, float[] su, float[] sv,
            Vector3 half, Vector3 inner, float radius,
            List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs, List<int> tris)
        {
            int start = verts.Count;
            float nSign = normal[nAxis];
            for (int i = 0; i < su.Length; i++)
            {
                for (int j = 0; j < sv.Length; j++)
                {
                    var p = Vector3.zero;
                    p[nAxis] = nSign * half[nAxis];
                    p[uAxis] = su[i];
                    p[vAxis] = sv[j];
                    var c = new Vector3(
                        Mathf.Clamp(p.x, -inner.x, inner.x),
                        Mathf.Clamp(p.y, -inner.y, inner.y),
                        Mathf.Clamp(p.z, -inner.z, inner.z));
                    Vector3 d = p - c;
                    Vector3 n = d.sqrMagnitude > 1e-12f ? d.normalized : normal;
                    verts.Add(c + n * radius);
                    normals.Add(n);
                    uvs.Add(new Vector2(i / (float)(su.Length - 1), j / (float)(sv.Length - 1)));
                }
            }

            int cols = sv.Length;
            for (int i = 0; i < su.Length - 1; i++)
            {
                for (int j = 0; j < sv.Length - 1; j++)
                {
                    int a = start + i * cols + j;
                    int b = start + (i + 1) * cols + j;
                    int c = start + (i + 1) * cols + j + 1;
                    int d = start + i * cols + j + 1;
                    AddTri(verts, tris, a, b, c, normal);
                    AddTri(verts, tris, a, c, d, normal);
                }
            }
        }

        private static void AddTri(List<Vector3> verts, List<int> tris, int a, int b, int c, Vector3 outward)
        {
            Vector3 cross = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
            if (cross.sqrMagnitude < 1e-14f)
            {
                tris.Add(a); tris.Add(b); tris.Add(c);
                return;
            }
            if (Vector3.Dot(cross, outward) >= 0f) { tris.Add(a); tris.Add(b); tris.Add(c); }
            else { tris.Add(a); tris.Add(c); tris.Add(b); }
        }

        /// <summary>원기둥/원뿔대. 아래 반지름과 위 반지름이 다를 수 있다. 피벗은 바닥 중앙.</summary>
        public static Mesh Cylinder(float radiusBottom, float radiusTop, float height, int segments = 24)
        {
            string key = string.Format("cyl_{0:0.###}_{1:0.###}_{2:0.###}_{3}", radiusBottom, radiusTop, height, segments);
            Mesh cached;
            if (Cache.TryGetValue(key, out cached) && cached != null) return cached;

            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var tris = new List<int>();
            float slope = (radiusBottom - radiusTop) / Mathf.Max(0.0001f, height);

            // 옆면
            for (int i = 0; i <= segments; i++)
            {
                float a = 2f * Mathf.PI * i / segments;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var n = new Vector3(dir.x, slope, dir.z).normalized;
                verts.Add(dir * radiusBottom);
                normals.Add(n);
                verts.Add(dir * radiusTop + Vector3.up * height);
                normals.Add(n);
            }
            for (int i = 0; i < segments; i++)
            {
                int b0 = i * 2, t0 = i * 2 + 1, b1 = i * 2 + 2, t1 = i * 2 + 3;
                Vector3 mid = (verts[b0] + verts[t1]) * 0.5f;
                Vector3 outward = new Vector3(mid.x, 0f, mid.z);
                AddTri(verts, tris, b0, t0, t1, outward);
                AddTri(verts, tris, b0, t1, b1, outward);
            }

            // 뚜껑
            AddCap(verts, normals, tris, radiusTop, height, Vector3.up, segments);
            AddCap(verts, normals, tris, radiusBottom, 0f, Vector3.down, segments);

            var mesh = new Mesh { name = key };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            Cache[key] = mesh;
            return mesh;
        }

        private static void AddCap(List<Vector3> verts, List<Vector3> normals, List<int> tris, float radius, float y, Vector3 n, int segments)
        {
            if (radius <= 0.0001f) return;
            int center = verts.Count;
            verts.Add(new Vector3(0f, y, 0f));
            normals.Add(n);
            for (int i = 0; i <= segments; i++)
            {
                float a = 2f * Mathf.PI * i / segments;
                verts.Add(new Vector3(Mathf.Cos(a) * radius, y, Mathf.Sin(a) * radius));
                normals.Add(n);
            }
            for (int i = 0; i < segments; i++) AddTri(verts, tris, center, center + 1 + i, center + 2 + i, n);
        }

        /// <summary>UV 구. 피벗은 중심.</summary>
        public static Mesh Sphere(float radius, int lon = 20, int lat = 14)
        {
            string key = string.Format("sph_{0:0.###}_{1}_{2}", radius, lon, lat);
            Mesh cached;
            if (Cache.TryGetValue(key, out cached) && cached != null) return cached;

            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var tris = new List<int>();
            for (int y = 0; y <= lat; y++)
            {
                float v = (float)y / lat;
                float theta = v * Mathf.PI;
                for (int x = 0; x <= lon; x++)
                {
                    float u = (float)x / lon;
                    float phi = u * Mathf.PI * 2f;
                    var n = new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Sin(phi));
                    verts.Add(n * radius);
                    normals.Add(n);
                }
            }
            for (int y = 0; y < lat; y++)
            {
                for (int x = 0; x < lon; x++)
                {
                    int a = y * (lon + 1) + x;
                    int b = a + lon + 1;
                    Vector3 outward = verts[a] + verts[b + 1];
                    AddTri(verts, tris, a, b, b + 1, outward);
                    AddTri(verts, tris, a, b + 1, a + 1, outward);
                }
            }
            var mesh = new Mesh { name = key };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            Cache[key] = mesh;
            return mesh;
        }

        /// <summary>삼각 지붕 (박공). 피벗은 바닥 중앙.</summary>
        public static Mesh Roof(float width, float depth, float height)
        {
            string key = string.Format("roof_{0:0.###}_{1:0.###}_{2:0.###}", width, depth, height);
            Mesh cached;
            if (Cache.TryGetValue(key, out cached) && cached != null) return cached;

            float w = width * 0.5f, d = depth * 0.5f;
            var p = new[]
            {
                new Vector3(-w, 0, -d), new Vector3(w, 0, -d), new Vector3(0, height, -d),
                new Vector3(-w, 0, d), new Vector3(w, 0, d), new Vector3(0, height, d),
            };
            var verts = new List<Vector3>();
            var tris = new List<int>();
            void Face(Vector3 outward, params int[] idx)
            {
                int start = verts.Count;
                foreach (int i in idx) verts.Add(p[i]);
                for (int k = 1; k < idx.Length - 1; k++) AddTri(verts, tris, start, start + k, start + k + 1, outward);
            }
            Face(Vector3.back, 0, 1, 2);
            Face(Vector3.forward, 3, 4, 5);
            Face(new Vector3(-height, w, 0), 0, 2, 5, 3);
            Face(new Vector3(height, w, 0), 1, 4, 5, 2);
            Face(Vector3.down, 0, 3, 4, 1);

            var mesh = new Mesh { name = key };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            Cache[key] = mesh;
            return mesh;
        }

        /// <summary>메시 + 재질을 가진 자식 오브젝트를 만든다.</summary>
        public static GameObject Spawn(string name, Mesh mesh, Material material, Transform parent, Vector3 localPos, bool castShadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = castShadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = true;
            return go;
        }
    }
}
