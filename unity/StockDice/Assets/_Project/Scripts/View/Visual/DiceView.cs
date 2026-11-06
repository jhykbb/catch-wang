using System.Collections;
using UnityEngine;

namespace StockDice.View
{
    /// <summary>주사위 2개. 던져서 굴러가다가 결과 눈이 위로 오게 멈춘다 (물리 없이 결정적으로).</summary>
    public sealed class DiceView : MonoBehaviour
    {
        private const float Size = 0.62f;
        private readonly Transform[] _dice = new Transform[2];
        private float _hideAt = -1f;

        public FxManager Fx { get; set; }
        public Vector3 LastCenter { get; private set; }

        public static DiceView Create(Transform parent)
        {
            var go = new GameObject("Dice");
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<DiceView>();
            for (int i = 0; i < 2; i++) view._dice[i] = BuildDie(go.transform, i);
            view.Hide();
            return view;
        }

        private static Vector3 FaceNormal(int value)
        {
            switch (value)
            {
                case 1: return Vector3.up;
                case 6: return Vector3.down;
                case 2: return Vector3.forward;
                case 5: return Vector3.back;
                case 3: return Vector3.right;
                default: return Vector3.left;
            }
        }

        private static Transform BuildDie(Transform parent, int index)
        {
            var root = new GameObject("Die" + index).transform;
            root.SetParent(parent, false);
            MeshFactory.Spawn("Cube", MeshFactory.RoundedBox(Vector3.one * Size, 0.13f, 3), MaterialLibrary.Get(Color.white, 0.6f), root, Vector3.zero);
            var pipMat = MaterialLibrary.Get(Palette.Ink, 0.5f);
            var redMat = MaterialLibrary.Get(Palette.Up, 0.5f);
            float half = Size * 0.5f;
            float gap = 0.15f;
            var pip = MeshFactory.Sphere(0.06f, 12, 8);
            for (int value = 1; value <= 6; value++)
            {
                Vector3 n = FaceNormal(value);
                Vector3 u, v;
                if (Mathf.Abs(n.y) > 0.5f) { u = Vector3.right; v = Vector3.forward; }
                else if (Mathf.Abs(n.z) > 0.5f) { u = Vector3.right; v = Vector3.up; }
                else { u = Vector3.forward; v = Vector3.up; }
                foreach (var p in PipLayout(value))
                {
                    var go = MeshFactory.Spawn("Pip" + value, pip, value == 1 ? redMat : pipMat, root,
                        n * (half - 0.012f) + u * p.x * gap + v * p.y * gap, false);
                    go.transform.localScale = value == 1 ? new Vector3(1.7f, 1.7f, 1.7f) : Vector3.one;
                    go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, n);
                    go.transform.localScale = Vector3.Scale(go.transform.localScale, new Vector3(1f, 0.45f, 1f));
                }
            }
            return root;
        }

        private static Vector2[] PipLayout(int value)
        {
            switch (value)
            {
                case 1: return new[] { Vector2.zero };
                case 2: return new[] { new Vector2(-1, -1), new Vector2(1, 1) };
                case 3: return new[] { new Vector2(-1, -1), Vector2.zero, new Vector2(1, 1) };
                case 4: return new[] { new Vector2(-1, -1), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(1, 1) };
                case 5: return new[] { new Vector2(-1, -1), new Vector2(-1, 1), Vector2.zero, new Vector2(1, -1), new Vector2(1, 1) };
                default: return new[] { new Vector2(-1, -1), new Vector2(-1, 0), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(1, 0), new Vector2(1, 1) };
            }
        }

        public void Hide()
        {
            foreach (var d in _dice) if (d != null) d.gameObject.SetActive(false);
            _hideAt = -1f;
        }

        public void HideAfter(float seconds)
        {
            _hideAt = Time.time + seconds;
        }

        private void Update()
        {
            if (_hideAt > 0f && Time.time >= _hideAt) Hide();
        }

        /// <summary>
        /// center 근처로 throwDir 방향에서 던진다. 끝나면 주사위는 잠시 남아 있다가 사라진다.
        /// </summary>
        public IEnumerator Roll(int d1, int d2, Vector3 center, Vector3 throwDir, float speed, bool controlled)
        {
            _hideAt = -1f;
            LastCenter = center;
            throwDir.y = 0f;
            if (throwDir.sqrMagnitude < 0.01f) throwDir = Vector3.forward;
            throwDir.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, throwDir).normalized;
            int[] values = { d1, d2 };
            var finals = new Vector3[2];
            var starts = new Vector3[2];
            var rotFinal = new Quaternion[2];
            var axes = new Vector3[2];
            var spins = new float[2];

            for (int i = 0; i < 2; i++)
            {
                float offset = (i == 0 ? -0.5f : 0.5f) + Random.Range(-0.08f, 0.08f);
                finals[i] = center + side * offset + throwDir * Random.Range(-0.15f, 0.15f) + Vector3.up * (Size * 0.5f);
                starts[i] = controlled
                    ? finals[i] + Vector3.up * 3f
                    : finals[i] - throwDir * 3.6f + Vector3.up * 2.6f + side * Random.Range(-0.4f, 0.4f);
                float yaw = Random.Range(-30f, 30f) + Vector3.SignedAngle(Vector3.forward, throwDir, Vector3.up);
                rotFinal[i] = Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.FromToRotation(FaceNormal(values[i]), Vector3.up);
                axes[i] = (Vector3.Cross(Vector3.up, throwDir) + Random.insideUnitSphere * 0.4f).normalized;
                spins[i] = controlled ? 0f : Random.Range(620f, 900f);
                _dice[i].gameObject.SetActive(true);
                _dice[i].position = starts[i];
                _dice[i].rotation = Quaternion.AngleAxis(spins[i], axes[i]) * rotFinal[i];
                _dice[i].localScale = Vector3.one;
            }

            float duration = (controlled ? 0.6f : 1.05f) / speed;
            bool landed = false;
            yield return Tween.Run(duration, k =>
            {
                for (int i = 0; i < 2; i++)
                {
                    float kk = Mathf.Clamp01(k * (i == 0 ? 1.0f : 1.06f));
                    Vector3 flat = Vector3.Lerp(starts[i], finals[i], Ease.OutCubic(kk));
                    flat.y = Mathf.Lerp(starts[i].y, finals[i].y, Ease.OutBounce(kk));
                    _dice[i].position = flat;
                    _dice[i].rotation = Quaternion.AngleAxis(spins[i] * (1f - Ease.OutCubic(kk)), axes[i]) * rotFinal[i];
                }
                if (!landed && k > 0.36f)
                {
                    landed = true;
                    if (Fx != null) Fx.DustRing(center, new Color(1f, 1f, 1f, 1f));
                }
            });
            for (int i = 0; i < 2; i++)
            {
                _dice[i].position = finals[i];
                _dice[i].rotation = rotFinal[i];
            }
            if (d1 == d2 && !controlled && Fx != null) Fx.Sparkle(center + Vector3.up * 0.8f, Palette.Gold);
        }

        public Vector3 TopOfDice { get { return LastCenter + Vector3.up * 1.2f; } }
    }
}
