using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StockDice.View
{
    /// <summary>
    /// 3D 효과: 날아가는 코인, 색종이, 먼지 고리, 반짝이, 주가 화살표.
    /// 파티클 시스템 대신 작은 메시를 직접 움직여서 URP/Built-in 어디서나 같은 모습이 나온다.
    /// </summary>
    public sealed class FxManager : MonoBehaviour
    {
        private sealed class Bit
        {
            public Transform Tr;
            public Vector3 Vel;
            public Vector3 AngVel;
            public float Life;
            public float MaxLife;
            public float Gravity;
            public float Drag;
            public Vector3 BaseScale;
            public bool Shrink;
        }

        private readonly List<Bit> _bits = new List<Bit>();
        private readonly Stack<Transform> _confettiPool = new Stack<Transform>();
        private readonly Stack<Transform> _coinPool = new Stack<Transform>();
        private readonly Stack<Transform> _dotPool = new Stack<Transform>();
        private Mesh _confettiMesh;
        private Mesh _coinMesh;
        private Mesh _dotMesh;
        private Material _coinMat;

        public static FxManager Create(Transform parent)
        {
            var go = new GameObject("Fx");
            go.transform.SetParent(parent, false);
            var fx = go.AddComponent<FxManager>();
            fx._confettiMesh = MeshFactory.RoundedBox(new Vector3(0.16f, 0.03f, 0.1f), 0.01f, 1);
            fx._coinMesh = MeshFactory.Cylinder(0.17f, 0.17f, 0.05f, 18);
            fx._dotMesh = MeshFactory.Sphere(0.08f, 10, 8);
            fx._coinMat = MaterialLibrary.Get(Palette.Gold, 0.8f, 0.2f);
            return fx;
        }

        private Transform Take(Stack<Transform> pool, Mesh mesh, Material mat, string name)
        {
            Transform t;
            if (pool.Count > 0)
            {
                t = pool.Pop();
                t.gameObject.SetActive(true);
            }
            else
            {
                t = MeshFactory.Spawn(name, mesh, mat, transform, Vector3.zero, false).transform;
            }
            t.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return t;
        }

        private void Return(Transform t)
        {
            if (t == null) return;
            t.gameObject.SetActive(false);
            var mf = t.GetComponent<MeshFilter>();
            if (mf.sharedMesh == _coinMesh) _coinPool.Push(t);
            else if (mf.sharedMesh == _dotMesh) _dotPool.Push(t);
            else _confettiPool.Push(t);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            for (int i = _bits.Count - 1; i >= 0; i--)
            {
                var b = _bits[i];
                b.Life += dt;
                if (b.Life >= b.MaxLife || b.Tr == null)
                {
                    Return(b.Tr);
                    _bits.RemoveAt(i);
                    continue;
                }
                b.Vel += Vector3.down * b.Gravity * dt;
                b.Vel *= Mathf.Max(0f, 1f - b.Drag * dt);
                var p = b.Tr.position + b.Vel * dt;
                if (p.y < 0.05f && b.Gravity > 0f)
                {
                    p.y = 0.05f;
                    b.Vel = new Vector3(b.Vel.x * 0.5f, -b.Vel.y * 0.3f, b.Vel.z * 0.5f);
                }
                b.Tr.position = p;
                b.Tr.Rotate(b.AngVel * dt, Space.Self);
                if (b.Shrink)
                {
                    float k = b.Life / b.MaxLife;
                    b.Tr.localScale = b.BaseScale * (k < 0.7f ? 1f : 1f - (k - 0.7f) / 0.3f);
                }
            }
        }

        /// <summary>색종이 폭죽.</summary>
        public void Confetti(Vector3 pos, int count, Color[] colors, float power)
        {
            for (int i = 0; i < count; i++)
            {
                var c = colors[Random.Range(0, colors.Length)];
                var t = Take(_confettiPool, _confettiMesh, MaterialLibrary.Get(c, 0.4f, 0.1f), "Confetti");
                t.position = pos + Random.insideUnitSphere * 0.2f;
                t.rotation = Random.rotation;
                t.localScale = Vector3.one * Random.Range(0.8f, 1.3f);
                var dir = (Random.insideUnitSphere + Vector3.up * 1.6f).normalized;
                _bits.Add(new Bit
                {
                    Tr = t,
                    Vel = dir * power * Random.Range(0.6f, 1.1f),
                    AngVel = Random.insideUnitSphere * 720f,
                    MaxLife = Random.Range(1.2f, 1.9f),
                    Gravity = 7f,
                    Drag = 1.6f,
                    BaseScale = t.localScale,
                    Shrink = true,
                });
            }
        }

        /// <summary>착지 먼지 고리.</summary>
        public void DustRing(Vector3 pos, Color color)
        {
            var mat = MaterialLibrary.Get(Palette.WithAlpha(color, 1f), 0.1f);
            int count = 12;
            for (int i = 0; i < count; i++)
            {
                float a = i * Mathf.PI * 2f / count;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var t = Take(_dotPool, _dotMesh, mat, "Dust");
                t.position = pos + dir * 0.3f + Vector3.up * 0.1f;
                t.localScale = Vector3.one * Random.Range(1.2f, 1.8f);
                _bits.Add(new Bit
                {
                    Tr = t,
                    Vel = dir * Random.Range(2.2f, 3.2f) + Vector3.up * 0.4f,
                    MaxLife = 0.5f,
                    Gravity = 0f,
                    Drag = 5f,
                    BaseScale = t.localScale,
                    Shrink = true,
                });
            }
        }

        /// <summary>위로 솟는 반짝이.</summary>
        public void Sparkle(Vector3 pos, Color color)
        {
            var mat = MaterialLibrary.Get(color, 0.6f, 1.2f);
            for (int i = 0; i < 16; i++)
            {
                var t = Take(_dotPool, _dotMesh, mat, "Sparkle");
                t.position = pos + Random.insideUnitSphere * 0.4f;
                t.localScale = Vector3.one * Random.Range(0.6f, 1.2f);
                _bits.Add(new Bit
                {
                    Tr = t,
                    Vel = (Random.insideUnitSphere * 0.8f + Vector3.up * 2.4f),
                    MaxLife = Random.Range(0.6f, 1.0f),
                    Gravity = -0.5f,
                    Drag = 1.5f,
                    BaseScale = t.localScale,
                    Shrink = true,
                });
            }
        }

        /// <summary>회사 위로 솟는 상승(빨강)/하락(파랑) 화살표.</summary>
        public void Arrow(Vector3 pos, bool up, bool limit)
        {
            StartCoroutine(ArrowRoutine(pos, up, limit));
        }

        private IEnumerator ArrowRoutine(Vector3 pos, bool up, bool limit)
        {
            var color = up ? Palette.Up : Palette.Down;
            var go = MeshFactory.Spawn("Arrow", MeshFactory.Roof(0.7f, 0.22f, 0.55f), MaterialLibrary.Get(color, 0.5f, 0.4f), transform, pos, false);
            go.transform.rotation = Quaternion.LookRotation(Vector3.forward) * (up ? Quaternion.identity : Quaternion.Euler(0f, 0f, 180f));
            float scale = limit ? 1.6f : 1f;
            yield return Tween.Run(0.9f, k =>
            {
                if (go == null) return;
                go.transform.position = pos + Vector3.up * (up ? 1f : -0.3f) * Ease.OutCubic(k) * 0.9f;
                float s = k < 0.25f ? Ease.OutBack(k / 0.25f) : k > 0.75f ? 1f - (k - 0.75f) / 0.25f : 1f;
                go.transform.localScale = Vector3.one * scale * Mathf.Max(0f, s);
                go.transform.Rotate(0f, 240f * Time.deltaTime, 0f, Space.World);
            });
            if (go != null) Destroy(go);
            if (limit) Sparkle(pos, color);
        }

        /// <summary>코인이 from 에서 to 로 포물선을 그리며 날아간다. 모두 도착하면 끝난다.</summary>
        public IEnumerator CoinFlight(Vector3 from, Vector3 to, int count, float duration)
        {
            count = Mathf.Clamp(count, 1, 18);
            float stagger = Mathf.Min(0.06f, duration * 0.5f / count);
            int alive = 0;
            for (int i = 0; i < count; i++)
            {
                alive++;
                StartCoroutine(OneCoin(from + Random.insideUnitSphere * 0.25f, to + Random.insideUnitSphere * 0.2f, duration, () => alive--));
                yield return Tween.Wait(stagger);
            }
            while (alive > 0) yield return null;
        }

        private IEnumerator OneCoin(Vector3 from, Vector3 to, float duration, System.Action done)
        {
            var t = Take(_coinPool, _coinMesh, _coinMat, "Coin");
            t.localScale = Vector3.one;
            float height = 1.2f + Vector3.Distance(from, to) * 0.18f;
            var spin = Random.insideUnitSphere * 900f;
            yield return Tween.Run(duration, k =>
            {
                Vector3 p = Vector3.Lerp(from, to, Ease.InOutSine(k));
                p.y += Ease.Arc(k) * height;
                t.position = p;
                t.Rotate(spin * Time.deltaTime, Space.Self);
                t.localScale = Vector3.one * (k > 0.85f ? 1f - (k - 0.85f) / 0.15f * 0.7f : 1f);
            });
            Return(t);
            done();
        }

        /// <summary>코인 하나가 튀어 올라 돌다가 떨어진다 (코인 거래소).</summary>
        public IEnumerator CoinToss(Vector3 pos, bool win, float speed)
        {
            var t = Take(_coinPool, _coinMesh, _coinMat, "TossCoin");
            t.localScale = Vector3.one * 2.4f;
            float duration = 1.0f / speed;
            yield return Tween.Run(duration, k =>
            {
                Vector3 p = pos;
                p.y += Ease.Arc(k) * 2.6f + 0.2f;
                t.position = p;
                t.rotation = Quaternion.Euler(k * 1440f + (win ? 0f : 180f), 30f, 0f);
            });
            if (win) Confetti(pos + Vector3.up * 0.5f, 30, new[] { Palette.Gold, Palette.Up, Color.white }, 5f);
            else DustRing(pos, Palette.Down);
            yield return Tween.Wait(0.2f / speed);
            Return(t);
        }
    }
}
