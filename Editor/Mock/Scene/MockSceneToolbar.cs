using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine.UIElements;

namespace Dennokoworks.UIDesignTest.Mock
{
    /// <summary>
    /// S1 シーンツールバー (カプセル) のモック。P4 (Mask Maker 風) の作業中だけ表示する。
    /// 既定は SceneView の左下。ユーザーが動かした位置は Unity が覚える。
    /// 状態は持たず、MockSceneSession.Changed の合図で読み直す。
    /// </summary>
    [Overlay(typeof(SceneView), "Dennokoworks.UIMock.Toolbar", "UI Mock ツールバー", true,
        defaultDockZone = DockZone.LeftColumn, defaultDockPosition = DockPosition.Bottom, defaultLayout = Layout.Panel)]
    internal sealed class MockSceneToolbar : Overlay, ITransientOverlay
    {
        public bool visible => MockSceneSession.Kind == MockSessionKind.Toolbar;

        // ドック位置の変更や SceneView の作り直しのたびに呼ばれるので、毎回作り直せる形にする
        public override VisualElement CreatePanelContent()
        {
            var root = new VisualElement();
            MockAssets.SetupSceneRoot(root);
            root.AddToClassList("dennoko-overlay-root");
            if (MockAssets.CloneTree(MockAssets.SCENE_TOOLBAR_UXML, root)) InitializeUI(root);
            return root;
        }

        private static void InitializeUI(VisualElement root)
        {
            Button B(string name, string icon, string fallback, string tooltip, System.Action click)
            {
                var b = DennokoIcons.Apply(root.Q<Button>(name), icon, fallback, tooltip);
                if (click != null) b.clicked += () => { click(); MockSceneSession.Raise(); };
                return b;
            }

            // アイコンのみ。名前はツールチップに「名前  キー」で出す
            var modeSelect = B("mode-select", "d_Grid.Default|d_RectTool", "選", "選択  Tab", () => MockSceneSession.PaintMode = false);
            var modePaint = B("mode-paint", "d_Grid.PaintTool", "塗", "ペイント  Tab", () => MockSceneSession.PaintMode = true);
            var island = root.Q<Button>("unit-island");
            var part = root.Q<Button>("unit-part");
            var face = root.Q<Button>("unit-face");
            island.clicked += () => { MockSceneSession.Unit = 0; MockSceneSession.Raise(); };
            part.clicked += () => { MockSceneSession.Unit = 1; MockSceneSession.Raise(); };
            face.clicked += () => { MockSceneSession.Unit = 2; MockSceneSession.Raise(); };
            var brush = B("tool-brush", "d_Grid.PaintTool", "B", "ブラシ  B", () => MockSceneSession.PaintTool = 0);
            var rect = B("tool-rect", "d_RectTool", "M", "矩形  M", () => MockSceneSession.PaintTool = 1);
            var lasso = B("tool-lasso", "d_Grid.FillTool|d_FreeformLayoutGroup Icon", "L", "投げ縄  L", () => MockSceneSession.PaintTool = 2);
            var eraser = B("tool-eraser", "d_Grid.EraserTool", "E", "消しゴム  Shift+B", () => MockSceneSession.PaintTool = 3);
            var symmetry = B("symmetry", "d_align_horizontally_center", "⇋", "左右対称  X", () => MockSceneSession.Symmetry = !MockSceneSession.Symmetry);
            var xray = B("xray", "d_scenevis_visible_hover|d_scenevis_visible", "透", "X-Ray (裏の面も選べる)", () => MockSceneSession.XRay = !MockSceneSession.XRay);
            var isolate = B("isolate", "d_SceneViewVisibility|d_ViewToolOrbit", "◎", "対象だけを表示", () => MockSceneSession.Isolate = !MockSceneSession.Isolate);
            B("undo", "d_back|d_Animation.PrevKey", "↶", "元に戻す  Ctrl+Z", Undo.PerformUndo);
            B("redo", "d_forward|d_Animation.NextKey", "↷", "やり直す  Ctrl+Y", Undo.PerformRedo);
            B("clear", "d_TreeEditor.Trash", "✕", "選択をすべて外す", () => MockSceneSession.Modify("クリア", d => d.selectedCells.Clear()));

            var export = DennokoIcons.Apply(root.Q<Button>("export"), DennokoIcons.Save, "↓", "出力  Ctrl+Enter");
            export.clicked += () =>
            {
                FlashSuccess(export);
                // シーンから出力したので、通知はカーソル脇のトーストに出す (ステータスバーには出さない)
                MockSceneHud.Toast("Body_mask.png", DennokoIcons.Save, "適用", () =>
                    MockSceneHud.Toast("適用: _MainColor2ndBlendMask", DennokoIcons.Success, atCursor: false));
            };

            var segUnits = root.Q("seg-units");
            var segTools = root.Q("seg-tools");
            var brushSize = root.Q<Label>("brush-size");
            var count = root.Q<Label>("count");

            // 状態 → 表示。選択中・ON は selection 色の枠
            void Refresh()
            {
                bool paint = MockSceneSession.PaintMode;
                modeSelect.EnableInClassList(MockUI.ActiveClass, !paint);
                modePaint.EnableInClassList(MockUI.ActiveClass, paint);
                // 同じ位置をモードで差し替える
                MockUI.Show(segUnits, !paint);
                MockUI.Show(segTools, paint);
                island.EnableInClassList(MockUI.ActiveClass, MockSceneSession.Unit == 0);
                part.EnableInClassList(MockUI.ActiveClass, MockSceneSession.Unit == 1);
                face.EnableInClassList(MockUI.ActiveClass, MockSceneSession.Unit == 2);
                int t = MockSceneSession.PaintTool;
                brush.EnableInClassList(MockUI.ActiveClass, t == 0);
                rect.EnableInClassList(MockUI.ActiveClass, t == 1);
                lasso.EnableInClassList(MockUI.ActiveClass, t == 2);
                eraser.EnableInClassList(MockUI.ActiveClass, t == 3);
                symmetry.EnableInClassList(MockUI.ActiveClass, MockSceneSession.Symmetry);
                xray.EnableInClassList(MockUI.ActiveClass, MockSceneSession.XRay);
                isolate.EnableInClassList(MockUI.ActiveClass, MockSceneSession.Isolate);
                brushSize.text = $"{MockSceneSession.BrushPx}px";
                count.text = $"{MockSceneSession.Data.selectedCells.Count}/{MockSceneDrawing.CellCount}";
            }

            // パネルに付いている間だけ購読する (外れたまま購読が残ると、作り直すたびに増える)
            var capsule = root.Q("capsule");
            capsule.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                MockSceneSession.Changed -= Refresh;
                MockSceneSession.Changed += Refresh;
                Refresh();
            });
            capsule.RegisterCallback<DetachFromPanelEvent>(_ => MockSceneSession.Changed -= Refresh);
            Refresh();
        }

        /// <summary>実行の成功を 2 秒だけ枠の色で知らせる (アイコンは差し替えない)。</summary>
        private static void FlashSuccess(Button b)
        {
            b.AddToClassList("dennoko-success-frame");
            b.schedule.Execute(() => b.RemoveFromClassList("dennoko-success-frame")).StartingIn(2000);
        }
    }
}
