using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Dennokoworks.UIDesignTest.Mock
{
    // P4 シーン作業: Idle = 対象カード + 「作業開始」だけ / Working = 作業中バナー + UV ビュー + 出力カード + 「出力」
    // 作業中はシーンに S1 ツールバー・S3 ラジアル (C 長押し)・S4 フィードバック・S5 描画が出る
    internal sealed partial class MockPatternHost
    {
        private static readonly string[] MockRendererNames = { "Body", "Hair", "Cloth_Top", "Shoes" };

        private void BuildP4(VisualElement root)
        {
            var status = _status;
            var banner = root.Q("session-banner");
            var sessionTarget = root.Q<Label>("session-target");
            var targetIdle = root.Q("target-idle");
            var targetWorking = root.Q("target-working");
            var field = root.Q<ObjectField>("target-field");
            var targetSub = root.Q("target-sub");
            var candidates = root.Q("target-candidates");
            var warning = root.Q("target-warning");
            var info = root.Q("target-info");
            var uvCard = root.Q("uv-card");
            var outputCard = root.Q("output-card");
            var dock = root.Q("output-dock");
            var dockChips = root.Q("output-dock-chips");
            var primary = root.Q<Button>("primary-button");
            var scroll = root.Q<ScrollView>("main-scroll");

            bool forceCandidates = false;
            bool readWriteWarning = false;
            bool mine = false;   // 今のセッションがこのウィンドウから始めたものか

            bool Working() => MockSceneSession.Kind == MockSessionKind.Toolbar;

            // ─── B1 対象カード ──────────────────────────────────
            var submesh = root.Q<DropdownField>("target-submesh");
            submesh.choices = new List<string> { "すべて", "Body_skin", "Body_cloth", "Body_eye" };
            submesh.index = 1;
            var uv = root.Q<DropdownField>("target-uv");
            uv.choices = new List<string> { "UV0", "UV1", "UV2" };
            uv.index = 0;
            DennokoIcons.SetImage(root.Q("target-switch-icon"), DennokoIcons.Mesh);

            List<string> CandidateNames()
            {
                var go = field.value as GameObject;
                if (go != null)
                {
                    var names = go.GetComponentsInChildren<Renderer>(true).Select(r => r.name).Distinct().ToList();
                    if (names.Count >= 2) return names;
                }
                return forceCandidates ? MockRendererNames.ToList() : new List<string>();
            }

            void BuildCandidates()
            {
                candidates.Clear();
                foreach (var name in CandidateNames())
                {
                    var n = name;
                    var row = new Button();
                    row.AddToClassList("dennoko-candidate");
                    row.Add(DennokoIcons.MakeIcon(DennokoIcons.Mesh));
                    var label = new Label(n) { pickingMode = PickingMode.Ignore };
                    label.AddToClassList("dennoko-candidate__name");
                    row.Add(label);
                    var sub = new Label("頂点 " + (n.Length * 2311).ToString("N0")) { pickingMode = PickingMode.Ignore };
                    sub.AddToClassList("dennoko-candidate__sub");
                    row.Add(sub);
                    row.tooltip = "クリックでこの対象の作業を始める";
                    row.clicked += () => Start(n);   // 候補はクリックで即開始
                    candidates.Add(row);
                }
            }

            // ─── B3 UV ビュー ───────────────────────────────────
            var preview = new MockPreview(uvCard, status) { MakeImage = MockUI.UVTexture, UpdateCost = MockPreview.Cost.Light };

            // ─── B5 出力カード + ドックの要約チップ ─────────────
            string[] resolutions = { "512", "1024", "2048", "4096" };
            string[] channels = { "RGBA", "R", "A" };
            int resolution = 2, channel = 0;
            var padding = root.Q<IntegerField>("output-padding");
            var invert = root.Q<Toggle>("output-invert");
            var outputName = root.Q<TextField>("output-name");
            var detailChips = root.Q("output-details-chips");

            void UpdateChips()
            {
                // 詳細の要約: 初期値と違う項目だけ
                detailChips.Clear();
                if (channel != 0) detailChips.Add(MockUI.Chip(null, channels[channel]));
                if (padding.value != 2) detailChips.Add(MockUI.Chip(null, $"余白 {padding.value}"));
                if (invert.value) detailChips.Add(MockUI.Chip(null, "反転"));

                // ドック: 出力カードと同じ値を要約チップで出す (クリックでカードへスクロール)
                dockChips.Clear();
                void ScrollToCard() => scroll.ScrollTo(outputCard);
                dockChips.Add(MockUI.Chip(DennokoIcons.Folder, "Textures", "出力先: Assets/Avatar/Textures", ScrollToCard));
                dockChips.Add(MockUI.Chip(null, outputName.value + ".png", "ファイル名", ScrollToCard));
                dockChips.Add(MockUI.Chip(null, resolutions[resolution], "解像度", ScrollToCard));
                if (channel != 0) dockChips.Add(MockUI.Chip(null, channels[channel], "チャンネル", ScrollToCard));
            }

            var setResolution = MockUI.BindChoice(root.Q("output-resolution"), resolution, i => { resolution = i; UpdateChips(); });
            var setChannel = MockUI.BindChoice(root.Q("output-channel"), channel, i => { channel = i; UpdateChips(); });
            MockUI.BindDetails(root.Q("output-details"));
            padding.RegisterValueChangedCallback(_ => UpdateChips());
            invert.RegisterValueChangedCallback(_ => UpdateChips());
            outputName.RegisterValueChangedCallback(_ => UpdateChips());
            var outputReset = DennokoIcons.Apply(root.Q<Button>("output-reset"), DennokoIcons.Reset, "↺", "このカードを初期値に戻す");
            outputReset.clicked += () =>
            {
                outputName.value = "Body_mask";
                resolution = 2; setResolution(2);
                channel = 0; setChannel(0);
                padding.value = 2;
                invert.value = false;
                UpdateChips();
                status.Set("「出力」を初期値に戻しました");
            };
            UpdateChips();

            // 出力カードが見えている間はドックを隠す (同じ情報を 2 か所に出さない)
            void UpdateDock()
            {
                bool cardVisible = outputCard.resolvedStyle.display != DisplayStyle.None &&
                                   scroll.contentViewport.worldBound.Overlaps(outputCard.worldBound);
                MockUI.Show(dock, Working() && !cardVisible);
            }
            scroll.verticalScroller.valueChanged += _ => UpdateDock();
            scroll.RegisterCallback<GeometryChangedEvent>(_ => UpdateDock());

            // ─── 状態 → 表示 ────────────────────────────────────
            void Refresh()
            {
                bool working = Working();
                MockUI.Show(banner, working);
                sessionTarget.text = MockSceneSession.TargetName;
                root.Q<Label>("target-switch-name").text = MockSceneSession.TargetName;

                MockUI.Show(targetIdle, !working);
                MockUI.Show(targetWorking, working);
                MockUI.Show(targetSub, working);
                MockUI.Show(info, working);
                var names = working ? new List<string>() : CandidateNames();
                MockUI.Show(candidates, names.Count > 0);
                if (names.Count > 0) BuildCandidates();
                MockUI.Show(warning, !working && readWriteWarning && (field.value != null || forceCandidates));

                // Idle は対象カードと主ボタンだけ。他のカードは描画しない
                MockUI.Show(uvCard, working);
                MockUI.Show(outputCard, working);

                primary.text = working ? "出力" : "作業開始";
                bool canStart = field.value != null && !readWriteWarning;
                primary.SetEnabled(working || canStart);
                primary.tooltip = working ? "出力  Ctrl+Enter"
                    : field.value == null ? "対象を選んでください"
                    : readWriteWarning ? "先に Read/Write を有効にしてください" : string.Empty;
                UpdateDock();
            }

            void Start(string targetName)
            {
                mine = true;
                MockSceneSession.Begin(MockSessionKind.Toolbar, "Mask Maker", field.value as GameObject, targetName);
                preview.Compute();
                status.Set("作業を開始しました。シーンでクリック、C 長押しでメニュー");
            }

            field.RegisterValueChangedCallback(_ => Refresh());
            root.Q<Button>("target-from-selection").clicked += () =>
            {
                if (Selection.activeGameObject != null) field.value = Selection.activeGameObject;
                else status.Set("Hierarchy で対象を選んでください", StatusType.Warning);
            };
            root.Q<Button>("target-fix").clicked += () =>
            {
                readWriteWarning = false;
                Refresh();
                status.Set("Read/Write を有効にしました", StatusType.Success);
            };
            root.Q<Button>("target-switch").clicked += () =>
            {
                var menu = new GenericMenu();
                var names = CandidateNames();
                if (names.Count == 0) names = MockRendererNames.ToList();
                foreach (var n in names)
                {
                    var name = n;
                    menu.AddItem(new GUIContent(name), name == MockSceneSession.TargetName, () =>
                    {
                        MockSceneSession.SwitchTarget(name);
                        status.Set($"対象を {name} に切り替えました");
                    });
                }
                menu.DropDown(root.Q("target-switch").worldBound);
            };
            sessionTarget.RegisterCallback<ClickEvent>(_ => MockSceneSession.FrameTarget());
            root.Q<Button>("session-end").clicked += () => MockSceneSession.End(MockEndReason.User);

            primary.clicked += () =>
            {
                if (!Working())
                {
                    var go = field.value as GameObject;
                    Start(go != null ? go.name : null);
                    return;
                }
                // ウィンドウから出力したので、通知はステータスバーに出す (トーストには出さない)
                primary.SetEnabled(false);
                status.RunFakeProgress("出力中…", 900, () =>
                {
                    primary.SetEnabled(true);
                    status.Set($"保存: {outputName.value}.png", StatusType.Success);
                });
            };

            // ─── セッションの購読 (作り直し・破棄で外す) ─────────
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
                menu.AddItem(new GUIContent("モック/作業開始 (ダミーの対象で)"), false, () => Start("Body (ダミー)"));
                menu.AddItem(new GUIContent("モック/作業を終了"), false, () => MockSceneSession.End(MockEndReason.User));
                menu.AddItem(new GUIContent("モック/対象: 空にする"), false, () =>
                {
                    field.value = null;
                    forceCandidates = false;
                    Refresh();
                });
                menu.AddItem(new GUIContent("モック/対象: 候補が複数 (ルートを選んだとき)"), forceCandidates, () =>
                {
                    forceCandidates = !forceCandidates;
                    Refresh();
                });
                menu.AddItem(new GUIContent("モック/対象: Read/Write 無効の警告"), readWriteWarning, () =>
                {
                    readWriteWarning = !readWriteWarning;
                    Refresh();
                });
                menu.AddItem(new GUIContent("モック/シーン: トーストを出す (下端中央)"), false, () =>
                    MockSceneHud.Toast("Body_mask.png", DennokoIcons.Save, "適用", () =>
                        MockSceneHud.Toast("適用: _MainColor2ndBlendMask", DennokoIcons.Success, atCursor: false), atCursor: false));
            };
        }
    }
}
