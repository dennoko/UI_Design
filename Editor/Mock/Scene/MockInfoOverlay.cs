using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;

namespace Dennokoworks.UIDesignTest.Mock
{
    /// <summary>
    /// S6 情報オーバーレイのモック (HowManyPolygons 風)。選択中のオブジェクトの三角形数を出す。
    /// 提案では「普通の Overlay にして、表示はユーザーが Overlay メニューで決める」だが、
    /// モックが勝手に全 SceneView に出ないよう、UI Design Test のトグルで ON にしたときだけ出す (ITransientOverlay)。
    /// 操作は置かない (読み取り専用)。段階の色は semantic を文字色にだけ使う。
    /// </summary>
    [Overlay(typeof(SceneView), "Dennokoworks.UIMock.Info", "UI Mock 情報", true,
        defaultDockZone = DockZone.RightColumn, defaultDockPosition = DockPosition.Top, defaultLayout = Layout.Panel)]
    internal sealed class MockInfoOverlay : Overlay, ITransientOverlay
    {
        private const string PrefsKey = "dennokoworks.UIMock.infoOverlay";

        /// <summary>UI Design Test の「SCENE UI」セクションのトグルで切り替える。</summary>
        public static bool Enabled
        {
            get => EditorPrefs.GetBool(PrefsKey, false);
            set
            {
                EditorPrefs.SetBool(PrefsKey, value);
                SceneView.RepaintAll();
            }
        }

        public bool visible => Enabled;

        public override VisualElement CreatePanelContent()
        {
            var root = new VisualElement();
            MockAssets.SetupSceneRoot(root);
            root.AddToClassList("dennoko-overlay-root");

            var box = new VisualElement();
            box.AddToClassList("dennoko-info-overlay");
            var value = new Label();
            value.AddToClassList("dennoko-info-overlay__value");
            var sub = new Label();
            sub.AddToClassList("dennoko-info-overlay__sub");
            box.Add(value);
            box.Add(sub);
            root.Add(box);

            void Refresh()
            {
                var go = Selection.activeGameObject;
                if (go == null)
                {
                    value.text = "—";
                    sub.text = "オブジェクトを選ぶと三角形数を表示";
                    SetLevel(box, -1);
                    return;
                }
                long tris = 0;
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true)) tris += Count(mf.sharedMesh);
                foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true)) tris += Count(smr.sharedMesh);
                value.text = tris.ToString("N0");
                sub.text = $"{go.name} · 三角形 (目安 70,000 以下)";
                SetLevel(box, tris <= 70000 ? 0 : tris <= 140000 ? 1 : 2);
            }

            box.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                Selection.selectionChanged -= Refresh;
                Selection.selectionChanged += Refresh;
                Refresh();
            });
            box.RegisterCallback<DetachFromPanelEvent>(_ => Selection.selectionChanged -= Refresh);
            Refresh();
            return root;
        }

        private static long Count(Mesh mesh)
        {
            if (mesh == null) return 0;
            long n = 0;
            for (int i = 0; i < mesh.subMeshCount; i++) n += mesh.GetIndexCount(i) / 3;
            return n;
        }

        private static void SetLevel(VisualElement box, int level)
        {
            box.EnableInClassList("dennoko-info-overlay--good", level == 0);
            box.EnableInClassList("dennoko-info-overlay--warn", level == 1);
            box.EnableInClassList("dennoko-info-overlay--bad", level == 2);
        }
    }
}
