using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Dennokoworks.UIDesignTest.Mock
{
    // P5 コンポーネント編集 (Inspector の再現): Idle = 「シーンで編集」/ Working = 作業中バナー
    // 作業中はシーンに S2 編集パネル・S5 描画が出る。ブラシの値はパネルと同じ 1 か所 (MockSceneData) を読む
    internal sealed partial class MockPatternHost
    {
        private static readonly string[] MockEdits = { "頂点 #128 を移動 (半径 0.08)", "頂点 #342 を移動 (半径 0.05)", "頂点 #77 を移動 (ミラー X)" };

        private void BuildP5(VisualElement root)
        {
            var status = _status;
            var primary = root.Q<Button>("primary-button");
            var banner = root.Q("session-banner");
            var sessionTarget = root.Q<Label>("session-target");
            var radius = root.Q<Slider>("brush-radius");
            bool mine = false;

            DennokoIcons.SetImage(root.Q("inspector-icon"), DennokoIcons.Script);
            DennokoIcons.Apply(root.Q<Button>("brush-help"), DennokoIcons.Help, "?",
                "半径: 動かす範囲\n減衰: 中心から離れるほど弱くする形\nミラー: 反対側も同時に動かす軸");

            // ─── B2 ブラシ (シーンのパネルと同じ値) ──────────────
            int radiusGroup = -1;
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

            var data = MockSceneSession.Data;
            var setFalloff = MockUI.BindChoice(root.Q("brush-falloff"), data.falloff,
                i => MockSceneSession.Modify("減衰", d => d.falloff = i));
            var setMirror = MockUI.BindChoice(root.Q("brush-mirror"), data.mirror,
                i => MockSceneSession.Modify("ミラー", d => d.mirror = i));
            var reset = DennokoIcons.Apply(root.Q<Button>("brush-reset"), DennokoIcons.Reset, "↺", "このカードを初期値に戻す");
            reset.clicked += () =>
            {
                MockSceneSession.Modify("ブラシを初期値に戻す", d => { d.radius = 0.08f; d.falloff = 0; d.mirror = 1; });
                status.Set("「ブラシ」を初期値に戻しました (Ctrl+Z で戻せます)");
            };

            // ─── 編集の一覧 (小さなリスト) ──────────────────────
            var list = root.Q("edits-list");
            for (int i = 0; i < MockEdits.Length; i++)
            {
                var row = new VisualElement();
                row.AddToClassList("dennoko-list-row");
                row.style.height = 22;
                if (i % 2 == 1) row.style.backgroundColor = new Color(1f, 1f, 1f, 0.025f);
                var name = new Label(MockEdits[i]);
                name.AddToClassList("dennoko-list-row__new");
                row.Add(name);
                var remove = DennokoIcons.Apply(new Button(), DennokoIcons.Close, "✕", "この編集を取り消す");
                remove.AddToClassList("dennoko-icon-button");
                remove.clicked += () => status.Set("編集を 1 件取り消しました (モック)");
                row.Add(remove);
                list.Add(row);
            }

            // ─── 状態 → 表示 ────────────────────────────────────
            void Refresh()
            {
                bool working = MockSceneSession.Kind == MockSessionKind.Panel;
                MockUI.Show(primary, !working);   // Working では主ボタンをバナーに置き換える
                MockUI.Show(banner, working);
                sessionTarget.text = MockSceneSession.TargetName;
                var d = MockSceneSession.Data;
                radius.SetValueWithoutNotify(d.radius);
                setFalloff(d.falloff);
                setMirror(d.mirror);
            }

            primary.clicked += () =>
            {
                mine = true;
                MockSceneSession.Begin(MockSessionKind.Panel, "Den Mesh Editor", Selection.activeGameObject);
                status.Set("編集を開始しました。シーンで頂点をクリック、Esc で終了");
            };
            sessionTarget.RegisterCallback<ClickEvent>(_ => MockSceneSession.FrameTarget());
            root.Q<Button>("session-end").clicked += () => MockSceneSession.End(MockEndReason.User);

            System.Action onChanged = Refresh;
            System.Action<MockEndReason> onEnded = reason =>
            {
                if (!mine) return;
                mine = false;
                status.Set(MockSceneSession.EndMessage(reason),
                    reason == MockEndReason.Replaced ? StatusType.Warning : StatusType.Info);
            };
            MockSceneSession.Changed += onChanged;
            MockSceneSession.Ended += onEnded;
            AddCleanup(() =>
            {
                MockSceneSession.Changed -= onChanged;
                MockSceneSession.Ended -= onEnded;
            });
            Refresh();

            _mockItems = menu =>
            {
                void Variant(string label, MockPanelVariant v)
                {
                    menu.AddItem(new GUIContent("モック/シーンパネルの中身/" + label), MockSceneSession.Variant == v, () =>
                    {
                        MockSceneSession.Variant = v;
                        MockSceneSession.Raise();
                    });
                }
                Variant("Mesh Editor (半径・減衰・ミラー)", MockPanelVariant.MeshEditor);
                Variant("Normal Editor (+ 効果スライダー)", MockPanelVariant.NormalEditor);
                Variant("DenLattice (格子・モード)", MockPanelVariant.Lattice);
                menu.AddItem(new GUIContent("モック/シーンパネルの警告行 (NDMF)"), MockSceneSession.ShowWarning, () =>
                {
                    MockSceneSession.ShowWarning = !MockSceneSession.ShowWarning;
                    MockSceneSession.Raise();
                });
                menu.AddItem(new GUIContent("モック/編集を終了"), false, () => MockSceneSession.End(MockEndReason.User));
            };
        }
    }
}
