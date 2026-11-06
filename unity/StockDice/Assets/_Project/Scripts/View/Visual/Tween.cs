using System;
using System.Collections;
using UnityEngine;

namespace StockDice.View
{
    /// <summary>외부 라이브러리 없이 쓰는 이징 함수.</summary>
    public static class Ease
    {
        public static float Linear(float t) { return t; }
        public static float InQuad(float t) { return t * t; }
        public static float OutQuad(float t) { return 1f - (1f - t) * (1f - t); }
        public static float OutCubic(float t) { float u = 1f - t; return 1f - u * u * u; }
        public static float InCubic(float t) { return t * t * t; }
        public static float InOutCubic(float t) { return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f; }
        public static float InOutSine(float t) { return -(Mathf.Cos(Mathf.PI * t) - 1f) / 2f; }

        public static float OutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }

        public static float OutElastic(float t)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            const float c4 = (2f * Mathf.PI) / 3f;
            return Mathf.Pow(2f, -10f * t) * Mathf.Sin((t * 10f - 0.75f) * c4) + 1f;
        }

        public static float OutBounce(float t)
        {
            const float n1 = 7.5625f;
            const float d1 = 2.75f;
            if (t < 1f / d1) return n1 * t * t;
            if (t < 2f / d1) { t -= 1.5f / d1; return n1 * t * t + 0.75f; }
            if (t < 2.5f / d1) { t -= 2.25f / d1; return n1 * t * t + 0.9375f; }
            t -= 2.625f / d1;
            return n1 * t * t + 0.984375f;
        }

        /// <summary>0 → 1 → 0 (위로 튀는 곡선).</summary>
        public static float Arc(float t) { return 4f * t * (1f - t); }
    }

    public static class Tween
    {
        /// <summary>duration 동안 step(0~1, 이징 적용)을 매 프레임 호출.</summary>
        public static IEnumerator Run(float duration, Action<float> step, Func<float, float> ease = null)
        {
            if (ease == null) ease = Ease.Linear;
            if (duration <= 0f)
            {
                step(ease(1f));
                yield break;
            }
            float t = 0f;
            while (t < duration)
            {
                step(ease(Mathf.Clamp01(t / duration)));
                yield return null;
                t += Time.deltaTime;
            }
            step(ease(1f));
        }

        public static IEnumerator Wait(float seconds)
        {
            float t = 0f;
            while (t < seconds)
            {
                yield return null;
                t += Time.deltaTime;
            }
        }

        public static IEnumerator Scale(Transform tr, Vector3 from, Vector3 to, float duration, Func<float, float> ease = null)
        {
            return Run(duration, k => { if (tr != null) tr.localScale = Vector3.LerpUnclamped(from, to, k); }, ease ?? Ease.OutBack);
        }

        public static IEnumerator Move(Transform tr, Vector3 from, Vector3 to, float duration, Func<float, float> ease = null)
        {
            return Run(duration, k => { if (tr != null) tr.position = Vector3.LerpUnclamped(from, to, k); }, ease ?? Ease.OutCubic);
        }

        /// <summary>펀치 스케일 (살짝 커졌다 돌아옴).</summary>
        public static IEnumerator Punch(Transform tr, float amount, float duration)
        {
            if (tr == null) yield break;
            Vector3 baseScale = tr.localScale;
            yield return Run(duration, k =>
            {
                if (tr == null) return;
                float s = 1f + amount * Mathf.Sin(k * Mathf.PI) * (1f - k * 0.5f);
                tr.localScale = baseScale * s;
            });
            if (tr != null) tr.localScale = baseScale;
        }
    }
}
