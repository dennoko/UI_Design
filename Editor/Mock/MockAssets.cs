using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Dennokoworks.UIDesignTest.Mock
{
    /// <summary>
    /// モックで使う USS / UXML の GUID と、ルートの準備をまとめたもの。
    /// GUID は Mock/UI 以下の .meta に書いてある固定値。
    /// </summary>
    internal static class MockAssets
    {
        public const string THEME_USS  = "3e31c0ea7a244072bb77655bf4a573c8"; // UI/DennokoTheme.uss (既存)
        public const string BLOCKS_USS = "7d1e4c2a9b3f4e6a8c5d0b1f2e3a4c51"; // Mock/UI/DennokoBlocks.uss
        public const string SCENE_USS  = "7d1e4c2a9b3f4e6a8c5d0b1f2e3a4c52"; // Mock/UI/DennokoSceneUI.uss
        public const string MOCK_USS   = "7d1e4c2a9b3f4e6a8c5d0b1f2e3a4c53"; // Mock/UI/UIMock.uss

        public const string P1_UXML = "7d1e4c2a9b3f4e6a8c5d0b1f2e3a4c61";
        public const string P2_UXML = "7d1e4c2a9b3f4e6a8c5d0b1f2e3a4c62";
        public const string P3_UXML = "7d1e4c2a9b3f4e6a8c5d0b1f2e3a4c63";
        public const string P4_UXML = "7d1e4c2a9b3f4e6a8c5d0b1f2e3a4c64";
        public const string P5_UXML = "7d1e4c2a9b3f4e6a8c5d0b1f2e3a4c65";

        public const string SCENE_TOOLBAR_UXML = "7d1e4c2a9b3f4e6a8c5d0b1f2e3a4c71";
        public const string SCENE_PANEL_UXML   = "7d1e4c2a9b3f4e6a8c5d0b1f2e3a4c72";

        /// <summary>ウィンドウのルート: テーマ → 提案ブロック → モック専用 の順に USS を足す。</summary>
        public static void SetupWindowRoot(VisualElement root)
        {
            root.AddToClassList("dennoko-root");
            // USS が読めなくても明るい背景にならないよう Surface0 を C# 側でも保証
            root.style.backgroundColor = (Color)new Color32(0x12, 0x12, 0x12, 0xFF);
            root.style.flexGrow = 1;
            DennokoUIFont.Apply(root);
            AddStyle(root, THEME_USS);
            AddStyle(root, BLOCKS_USS);
            AddStyle(root, MOCK_USS);
        }

        /// <summary>SceneView 上の UI のルート。シーンを透かすので背景色は付けない。</summary>
        public static void SetupSceneRoot(VisualElement root)
        {
            root.AddToClassList("dennoko-root");
            root.AddToClassList("dennoko-scene");
            AddStyle(root, THEME_USS);
            AddStyle(root, BLOCKS_USS);
            AddStyle(root, SCENE_USS);   // 同じ詳細度のルールを後勝ちで上書きするので最後に足す
            DennokoUIFont.Apply(root);
        }

        public static bool CloneTree(string guid, VisualElement into)
        {
            var tree = Load<VisualTreeAsset>(guid);
            if (tree == null)
            {
                into.Add(new Label($"UXML が見つかりません (GUID {guid})。Unity でインポートされているか確認してください。"));
                return false;
            }
            tree.CloneTree(into);
            return true;
        }

        private static void AddStyle(VisualElement root, string guid)
        {
            var uss = Load<StyleSheet>(guid);
            if (uss != null) root.styleSheets.Add(uss);
        }

        private static T Load<T>(string guid) where T : Object
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var asset = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
                Debug.LogWarning($"[UI Mock] アセットが見つかりません。GUID を確認してください: {guid}");
            return asset;
        }
    }
}
