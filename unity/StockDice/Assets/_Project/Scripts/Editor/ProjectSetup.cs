using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using StockDice.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace StockDice.EditorTools
{
    /// <summary>
    /// 처음 열었을 때 메인 씬과 기본 재질을 자동으로 만든다. 메뉴: StockDice
    /// </summary>
    [InitializeOnLoad]
    public static class ProjectSetup
    {
        private const string ScenePath = "Assets/_Project/Scenes/Main.unity";
        private const string MaterialPath = "Assets/_Project/Resources/StockDice/BaseLit.mat";
        private const string SettingsDir = "Assets/_Project/Settings";
        private const string SessionKey = "StockDice.AutoSetupDone";

        static ProjectSetup()
        {
            EditorApplication.delayCall += AutoSetup;
        }

        private static void AutoSetup()
        {
            if (SessionState.GetBool(SessionKey, false)) return;
            SessionState.SetBool(SessionKey, true);
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            EnsureBaseMaterial(false);
            if (!File.Exists(ScenePath))
            {
                var active = SceneManager.GetActiveScene();
                bool canOpen = !active.isDirty;
                CreateMainScene(canOpen);
                Debug.Log("[StockDice] 메인 씬을 만들었습니다: " + ScenePath + " (Play 를 누르면 시작)");
            }
        }

        [MenuItem("StockDice/메인 씬 만들기 + 열기", false, 1)]
        private static void MenuCreateScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EnsureBaseMaterial(false);
            CreateMainScene(true);
        }

        [MenuItem("StockDice/URP 렌더 파이프라인 자동 설정 (실험적)", false, 20)]
        private static void MenuSetupUrp()
        {
            TrySetupUrp();
            EnsureBaseMaterial(true);
        }

        [MenuItem("StockDice/기본 재질 다시 만들기 (렌더 파이프라인을 바꾼 뒤)", false, 21)]
        private static void MenuRebuildMaterial()
        {
            EnsureBaseMaterial(true);
        }

        private static void CreateMainScene(bool openAsSingle)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, openAsSingle ? NewSceneMode.Single : NewSceneMode.Additive);
            var go = new GameObject("GameBootstrap");
            go.AddComponent<GameBootstrap>();
            if (!openAsSingle) SceneManager.MoveGameObjectToScene(go, scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            if (!openAsSingle) EditorSceneManager.CloseScene(scene, true);
            AddToBuildSettings(ScenePath);
            AssetDatabase.Refresh();
        }

        private static void AddToBuildSettings(string path)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (scenes.Exists(s => s.path == path)) return;
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        /// <summary>
        /// 런타임에 색만 바꿔 복제할 기본 재질. 빌드에 셰이더가 포함되도록 Resources 에 둔다.
        /// </summary>
        private static void EnsureBaseMaterial(bool force)
        {
            bool urp = GraphicsSettings.currentRenderPipeline != null;
            var shader = urp ? Shader.Find("Universal Render Pipeline/Lit") : Shader.Find("Standard");
            if (shader == null) return;
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat != null && mat.shader == shader && !force) return;
            Directory.CreateDirectory(Path.GetDirectoryName(MaterialPath));
            if (mat == null)
            {
                mat = new Material(shader) { name = "BaseLit" };
                AssetDatabase.CreateAsset(mat, MaterialPath);
            }
            else
            {
                mat.shader = shader;
                EditorUtility.SetDirty(mat);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[StockDice] 기본 재질 준비: " + shader.name);
        }

        /// <summary>
        /// URP 패키지는 있는데 파이프라인 에셋이 지정되지 않았을 때 하나 만들어 지정한다.
        /// "Universal 3D" 템플릿으로 만든 프로젝트라면 이미 설정되어 있으니 필요 없다.
        /// </summary>
        private static void TrySetupUrp()
        {
            if (GraphicsSettings.currentRenderPipeline != null)
            {
                EditorUtility.DisplayDialog("StockDice", "이미 렌더 파이프라인이 설정되어 있습니다: " + GraphicsSettings.currentRenderPipeline.name, "확인");
                return;
            }
            try
            {
                const string asm = ", Unity.RenderPipelines.Universal.Runtime";
                var assetType = Type.GetType("UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset" + asm);
                var rendererType = Type.GetType("UnityEngine.Rendering.Universal.UniversalRendererData" + asm);
                if (assetType == null || rendererType == null)
                {
                    EditorUtility.DisplayDialog("StockDice", "URP 패키지(com.unity.render-pipelines.universal)가 없습니다. Package Manager 에서 설치하거나, Built-in 렌더러로 그대로 써도 됩니다.", "확인");
                    return;
                }
                Directory.CreateDirectory(SettingsDir);

                // URP 내부의 렌더러 생성 함수가 있으면 그것을 쓴다 (포스트 프로세싱 데이터까지 채워 줌)
                ScriptableObject rendererData = null;
                var createRenderer = assetType.GetMethod("CreateRendererAsset", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
                if (createRenderer != null && createRenderer.GetParameters().Length >= 2)
                {
                    var rendererEnum = createRenderer.GetParameters()[1].ParameterType;
                    var args = new List<object> { SettingsDir + "/StockDice_URP.asset", Enum.ToObject(rendererEnum, 1) };
                    var ps = createRenderer.GetParameters();
                    for (int i = 2; i < ps.Length; i++) args.Add(ps[i].HasDefaultValue ? ps[i].DefaultValue : null);
                    rendererData = createRenderer.Invoke(null, args.ToArray()) as ScriptableObject;
                }
                if (rendererData == null)
                {
                    rendererData = ScriptableObject.CreateInstance(rendererType);
                    AssetDatabase.CreateAsset(rendererData, SettingsDir + "/StockDice_URP_Renderer.asset");
                }

                var create = assetType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static);
                var asset = (RenderPipelineAsset)create.Invoke(null, new object[] { rendererData });
                AssetDatabase.CreateAsset(asset, SettingsDir + "/StockDice_URP.asset");
                AssetDatabase.SaveAssets();

                var prop = typeof(GraphicsSettings).GetProperty("defaultRenderPipeline", BindingFlags.Public | BindingFlags.Static)
                           ?? typeof(GraphicsSettings).GetProperty("renderPipelineAsset", BindingFlags.Public | BindingFlags.Static);
                if (prop != null) prop.SetValue(null, asset, null);
                QualitySettings.renderPipeline = asset;
                Debug.Log("[StockDice] URP 에셋을 만들고 지정했습니다: " + SettingsDir);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[StockDice] URP 자동 설정 실패: " + e.Message + "\n" +
                                 "Assets > Create > Rendering > URP Asset (with Universal Renderer) 로 만든 뒤 Project Settings > Graphics 에 지정해 주세요.");
            }
        }
    }
}
