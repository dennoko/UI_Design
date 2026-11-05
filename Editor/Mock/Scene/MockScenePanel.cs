using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;

namespace Dennokoworks.UIDesignTest.Mock
{
    /// <summary>
    /// S2 シーンツールパネルのモック。DennokoMeshEditor / DennokoNormalEditor / DenLattice の
    /// IMGUI パネルを Overlay (パネル表示) で置き換えた形。P5 の作業中だけ表示する。
    /// 自作のドラッグ・画面内への収め込み・位置の記憶は Unity の Overlay に任せる。
    /// 中身はモックメニューで 3 ツール分を切り替える (MockSceneSession.Variant)。
    /// </summary>
    [Overlay(typeof(SceneView), "Dennokoworks.UIMock.Panel", "UI Mock 編集パネル", true,
        defaultDockZone = DockZone.RightColumn, defaultDockPosition = DockPosition.Bottom, defaultLayout = Layout.Panel)]
    internal sealed class MockScenePanel : Overlay, ITransientOverlay
    {
        public bool visible => MockSceneSession.Kind == MockSessionKind.Panel;

        public override VisualElement CreatePanelContent()
        {
            var root = new VisualElement();
            MockAssets.SetupSceneRoot(root);
            root.AddToClassList("dennoko-overlay-root");
            if (MockAssets.CloneTree(MockAssets.SCENE_PANEL_UXML, root)) InitializeUI(root);
            return root;
        }

        private static void InitializeUI(VisualElement root)
        {
            var data = MockSceneSession.Data;

            // ─── セッション行 ──────────────────────────────────
            var target = root.Q<Label>("session-target");
            target.RegisterCallback<ClickEvent>(_ => MockSceneSession.FrameTarget());
            var end = DennokoIcons.Apply(root.Q<Button>("session-end"), DennokoIcons.Close, "✕", "編集を終了  Esc");
            end.clicked += () => MockSceneSession.End(MockEndReason.User);

            // ─── ブラシ ────────────────────────────────────────
            var radius = root.Q<Slider>("radius-slider");
            var radiusField = root.Q<FloatField>("radius-field");
            int radiusGroup = -1;

            // スライダーのドラッグは 1 回で Undo 1 段にまとめる:
            // 最初の変更でグループを開き、ポインタを離したら CollapseUndoOperations する
            radius.RegisterValueChangedCallback(e =>
            {
                if (radiusGroup < 0)
                {
                    Undo.IncrementCurrentGroup();
                    Undo.SetCurrentGroupName("半径");
                    radiusGroup = Undo.GetCurrentGroup();
                }
                MockSceneSession.Modify("半径", d => d.radius = e.newValue);
            });
            void EndRadiusDrag()
            {
                if (radiusGroup < 0) return;
                Undo.CollapseUndoOperations(radiusGroup);
                radiusGroup = -1;
            }
            radius.RegisterCallback<PointerUpEvent>(_ => EndRadiusDrag(), TrickleDown.TrickleDown);
            radius.RegisterCallback<PointerCaptureOutEvent>(_ => EndRadiusDrag(), TrickleDown.TrickleDown);
            radiusField.RegisterValueChangedCallback(e =>
                MockSceneSession.Modify("半径", d => d.radius = Mathf.Clamp(e.newValue, 0.01f, 0.3f)));

            var setFalloff = MockUI.BindChoice(root.Q("falloff-group"), data.falloff,
                i => MockSceneSession.Modify("減衰", d => d.falloff = i));
            var setMirror = MockUI.BindChoice(root.Q("mirror-group"), data.mirror,
                i => MockSceneSession.Modify("ミラー", d => d.mirror = i));

            // ─── 効果 (1 回適用スライダー) ─────────────────────
            string[] effectNames = { "滑らか", "形状復元", "平坦化", "消去" };
            var effects = new Slider[4];
            for (int i = 0; i < 4; i++)
            {
                int index = i;
                var slider = root.Q<Slider>($"effect-{i}");
                var percent = root.Q<Label>($"effect-{i}-percent");
                effects[i] = slider;
                percent.text = string.Empty;
                slider.RegisterValueChangedCallback(e => percent.text = e.newValue > 0f ? $"{Mathf.RoundToInt(e.newValue * 100)}%" : string.Empty);
                void Commit()
                {
                    if (slider.value <= 0f) return;
                    int pct = Mathf.RoundToInt(slider.value * 100);
                    // 離したら確定して 0 に戻る
                    slider.SetValueWithoutNotify(0f);
                    percent.text = string.Empty;
                    MockSceneHud.Toast($"{effectNames[index]} {pct}% を適用", DennokoIcons.Success, atCursor: false);
                }
                slider.RegisterCallback<PointerUpEvent>(_ => Commit(), TrickleDown.TrickleDown);
                slider.RegisterCallback<PointerCaptureOutEvent>(_ => Commit(), TrickleDown.TrickleDown);
            }

            // ─── ラティス ──────────────────────────────────────
            var lu = root.Q<IntegerField>("lattice-u");
            var lv = root.Q<IntegerField>("lattice-v");
            var lw = root.Q<IntegerField>("lattice-w");
            void SetLattice() => MockSceneSession.Modify("格子数", d =>
                d.lattice = new Vector3Int(Mathf.Clamp(lu.value, 2, 8), Mathf.Clamp(lv.value, 2, 8), Mathf.Clamp(lw.value, 2, 8)));
            lu.RegisterValueChangedCallback(_ => SetLattice());
            lv.RegisterValueChangedCallback(_ => SetLattice());
            lw.RegisterValueChangedCallback(_ => SetLattice());
            MockUI.BindToggles(root.Q("lattice-mirror"), data.latticeMirror,
                (i, on) => MockSceneSession.Modify("ミラー", d => d.latticeMirror[i] = on));
            var setMode = MockUI.BindChoice(root.Q("lattice-mode"), data.latticeMode,
                i => MockSceneSession.Modify("モード", d => d.latticeMode = i));
            var setBoxOp = MockUI.BindChoice(root.Q("lattice-box-op"), data.boxOp,
                i => MockSceneSession.Modify("操作", d => d.boxOp = i));
            var reset = DennokoIcons.Apply(root.Q<Button>("lattice-reset"), DennokoIcons.Reset, "↺", "ケージを初期形状に戻す");
            reset.clicked += () =>
            {
                MockSceneSession.Modify("ケージのリセット", d => d.lattice = new Vector3Int(3, 3, 3));
                MockSceneHud.Toast("ケージを初期形状に戻しました (Ctrl+Z で戻せます)", DennokoIcons.Info, atCursor: false);
            };

            // ─── キーヒント / 警告行 ───────────────────────────
            var keyhints = root.Q("keyhints");
            var warning = root.Q("warning");
            DennokoIcons.SetImage(root.Q("warning-icon"), DennokoIcons.Warning);
            warning.tooltip = "NDMF プレビューを取得できませんでした。他のツールによる変形はこの表示に反映されていません。";

            var groupBrush = root.Q("group-brush");
            var groupEffects = root.Q("group-effects");
            var groupLattice = root.Q("group-lattice");
            var boxRow = root.Q("lattice-box-row");

            void Refresh()
            {
                var d = MockSceneSession.Data;
                var variant = MockSceneSession.Variant;
                target.text = MockSceneSession.TargetName;

                MockUI.Show(groupBrush, variant != MockPanelVariant.Lattice);
                MockUI.Show(groupEffects, variant == MockPanelVariant.NormalEditor);
                MockUI.Show(groupLattice, variant == MockPanelVariant.Lattice);

                radius.SetValueWithoutNotify(d.radius);
                radiusField.SetValueWithoutNotify(Mathf.Round(d.radius * 1000f) / 1000f);
                setFalloff(d.falloff);
                setMirror(d.mirror);

                bool hasSelection = d.selectedVertex >= 0;
                foreach (var s in effects)
                {
                    s.SetEnabled(hasSelection);
                    s.tooltip = hasSelection ? string.Empty : "頂点を選ぶと使えます";
                }

                lu.SetValueWithoutNotify(d.lattice.x);
                lv.SetValueWithoutNotify(d.lattice.y);
                lw.SetValueWithoutNotify(d.lattice.z);
                setMode(d.latticeMode);
                setBoxOp(d.boxOp);
                MockUI.Show(boxRow, d.latticeMode == 1);

                // 常設のキーヒントは 2 組まで
                keyhints.Clear();
                if (variant == MockPanelVariant.Lattice) MockUI.AddKeyHint(keyhints, "W E R", "操作");
                else MockUI.AddKeyHint(keyhints, "Wheel", "半径 (ドラッグ中)");
                MockUI.AddKeyHint(keyhints, "Esc", hasSelection ? "選択を解除" : "終了");

                MockUI.Show(warning, MockSceneSession.ShowWarning);
            }

            var panel = root.Q("panel");
            panel.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                MockSceneSession.Changed -= Refresh;
                MockSceneSession.Changed += Refresh;
                Refresh();
            });
            panel.RegisterCallback<DetachFromPanelEvent>(_ => MockSceneSession.Changed -= Refresh);
            Refresh();
        }
    }
}
