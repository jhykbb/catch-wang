using UnityEngine;
#if STOCKDICE_URP
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#endif

namespace StockDice.View
{
    /// <summary>
    /// URP 를 쓸 때만 포스트 프로세싱(블룸, 비네트, 톤매핑)과 안티앨리어싱을 켠다.
    /// STOCKDICE_URP 는 asmdef 의 versionDefines 가 URP 패키지가 있으면 자동으로 정의한다.
    /// </summary>
    public static class UrpSetup
    {
        public static void Apply(Camera cam, Transform parent)
        {
#if STOCKDICE_URP
            if (GraphicsSettings.currentRenderPipeline == null) return;
            var data = cam.GetUniversalAdditionalCameraData();
            if (data != null)
            {
                data.renderPostProcessing = true;
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                data.antialiasingQuality = AntialiasingQuality.High;
            }

            var go = new GameObject("PostFx");
            go.transform.SetParent(parent, false);
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(0.45f);
            bloom.threshold.Override(1.0f);
            bloom.scatter.Override(0.6f);
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.18f);
            vignette.smoothness.Override(0.5f);
            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.Neutral);
            var color = profile.Add<ColorAdjustments>(true);
            color.saturation.Override(8f);
            color.contrast.Override(4f);
            volume.profile = profile;
#endif
        }
    }
}
