using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace StockDice.View
{
    /// <summary>
    /// 색상별 재질 캐시. URP 를 쓰면 URP Lit, 아니면 Built-in Standard 로 자동 선택한다.
    /// 빌드에서 셰이더가 빠지지 않도록 에디터 셋업이 Resources/StockDice/BaseLit.mat 를 만들어 둔다.
    /// </summary>
    public static class MaterialLibrary
    {
        private static Material _base;
        private static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();

        public static bool IsUrp { get { return GraphicsSettings.currentRenderPipeline != null; } }

        private static Material Base
        {
            get
            {
                if (_base != null) return _base;
                var loaded = Resources.Load<Material>("StockDice/BaseLit");
                if (loaded != null && loaded.shader != null && loaded.shader.isSupported && ShaderMatchesPipeline(loaded.shader))
                {
                    _base = loaded;
                    return _base;
                }
                Shader shader = null;
                if (IsUrp) shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Universal Render Pipeline/Simple Lit");
                if (shader == null) shader = Shader.Find("Standard");
                if (shader == null) shader = Shader.Find("Diffuse");
                _base = new Material(shader) { name = "StockDice_Base" };
                return _base;
            }
        }

        private static bool ShaderMatchesPipeline(Shader shader)
        {
            bool urpShader = shader.name.StartsWith("Universal Render Pipeline");
            return urpShader == IsUrp;
        }

        /// <summary>단색 재질. smoothness 0~1, emission 은 빛나는 정도.</summary>
        public static Material Get(Color color, float smoothness = 0.35f, float emission = 0f)
        {
            string key = ColorUtility.ToHtmlStringRGBA(color) + "_" + smoothness.ToString("0.00") + "_" + emission.ToString("0.00");
            Material m;
            if (Cache.TryGetValue(key, out m) && m != null) return m;
            m = Create(color, smoothness, emission);
            Cache[key] = m;
            return m;
        }

        /// <summary>캐시하지 않는 개별 재질 (색이 계속 바뀌는 연출용).</summary>
        public static Material Create(Color color, float smoothness = 0.35f, float emission = 0f)
        {
            var m = new Material(Base);
            m.name = "SD_" + ColorUtility.ToHtmlStringRGB(color);
            SetColor(m, color);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            SetEmission(m, color, emission);
            m.enableInstancing = true;
            return m;
        }

        public static void SetColor(Material m, Color color)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
        }

        public static void SetEmission(Material m, Color color, float emission)
        {
            if (!m.HasProperty("_EmissionColor")) return;
            if (emission > 0f)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", color * emission);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                m.DisableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", Color.black);
            }
        }
    }
}
