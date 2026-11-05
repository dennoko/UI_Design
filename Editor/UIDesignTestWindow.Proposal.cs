using System;
using Dennokoworks.UIDesignTest.Mock;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Dennokoworks.UIDesignTest
{
    // 提案セクション (14 画面パターン / 15 シーン操作 UI / 16 パイメニューのテンプレート)。
    // 中身は Editor/Mock 以下 (Docs/ui-blocks-proposal.md のモック)。
    public partial class UIDesignTestWindow
    {
        private const string PatternStateKey = "dennokoworks.UIDesignTest.pattern";

        private MockPatternHost _patternHost;
        private Action _proposalCleanup;

        private void BindProposalSections(VisualElement root)
        {
            DisposeProposalSections();
            BindPatterns(root);
            BindSceneUI(root);
            BindRadialTemplate(root);
        }

        private void DisposeProposalSections()
        {
            _patternHost?.Dispose();
            _patternHost = null;
            _proposalCleanup?.Invoke();
            _proposalCleanup = null;
        }

        // ─── 14. 画面パターン ───────────────────────────────────────

        private void BindPatterns(VisualElement root)
        {
            var frame = root.Q("pattern-frame");
            var description = root.Q<Label>("pattern-description");
            _patternHost = new MockPatternHost(frame);

            void Select(int index)
            {
                var pattern = (MockPattern)index;
                SessionState.SetInt(PatternStateKey, index);
                description.text = MockPatternHost.Description(pattern);
                _patternHost.Show(pattern);
            }

            int initial = Mathf.Clamp(SessionState.GetInt(PatternStateKey, 0), 0, 4);
            MockUI.BindChoice(root.Q("pattern-tabs"), initial, Select);
            Select(initial);
        }

        // ─── 15. シーン操作 UI ──────────────────────────────────────

        private void BindSceneUI(VisualElement root)
        {
            root.Q<Button>("scene-start-toolbar").clicked += () => StartScene(MockSessionKind.Toolbar);
            root.Q<Button>("scene-start-panel").clicked += () => StartScene(MockSessionKind.Panel);

            var setVariant = MockUI.BindChoice(root.Q("scene-variant"), (int)MockSceneSession.Variant, i =>
            {
                MockSceneSession.Variant = (MockPanelVariant)i;
                MockSceneSession.Raise();
            });

            var warning = root.Q<Toggle>("scene-warning");
            warning.value = MockSceneSession.ShowWarning;
            warning.RegisterValueChangedCallback(e =>
            {
                MockSceneSession.ShowWarning = e.newValue;
                MockSceneSession.Raise();
            });

            root.Q<Button>("scene-toast").clicked += () =>
            {
                if (!RequireSceneView()) return;
                MockSceneHud.Toast("Body_mask.png", DennokoIcons.Save, "適用", () =>
                    MockSceneHud.Toast("適用: _MainColor2ndBlendMask", DennokoIcons.Success, atCursor: false), atCursor: false);
            };
            root.Q<Button>("scene-hint").clicked += () =>
            {
                if (RequireSceneView()) MockSceneHud.ShowHintSample();
            };

            var info = root.Q<Toggle>("scene-info");
            info.value = MockInfoOverlay.Enabled;
            info.RegisterValueChangedCallback(e =>
            {
                MockInfoOverlay.Enabled = e.newValue;
                if (e.newValue) RequireSceneView();
            });

            var keys = root.Q("scene-keys");
            MockUI.AddKeyHint(keys, "C 長押し", "ラジアル");
            MockUI.AddKeyHint(keys, "X", "対称");
            MockUI.AddKeyHint(keys, "Tab", "モード");
            MockUI.AddKeyHint(keys, "Shift / Ctrl", "足す / 外す");
            MockUI.AddKeyHint(keys, "Esc", "終了");

            var session = root.Q<Label>("scene-session");
            var end = root.Q<Button>("scene-end");
            end.clicked += () => MockSceneSession.End(MockEndReason.User);

            void Refresh()
            {
                bool active = MockSceneSession.Active;
                string kind = MockSceneSession.Kind == MockSessionKind.Toolbar ? "ツールバー型" : "パネル型";
                session.text = active ? $"● 作業中: {kind} · {MockSceneSession.TargetName}" : "作業していません";
                session.EnableInClassList("uitest-session-label--active", active);
                end.SetEnabled(active);
                setVariant((int)MockSceneSession.Variant);
                warning.SetValueWithoutNotify(MockSceneSession.ShowWarning);
            }

            Action onChanged = Refresh;
            Action<MockEndReason> onEnded = reason => SetStatus(MockSceneSession.EndMessage(reason), StatusType.Info);
            MockSceneSession.Changed += onChanged;
            MockSceneSession.Ended += onEnded;
            _proposalCleanup += () =>
            {
                MockSceneSession.Changed -= onChanged;
                MockSceneSession.Ended -= onEnded;
            };
            Refresh();
        }

        private void StartScene(MockSessionKind kind)
        {
            if (!RequireSceneView()) return;
            var target = Selection.activeGameObject;
            string tool = kind == MockSessionKind.Toolbar ? "Mask Maker (見本)" : "Den Mesh Editor (見本)";
            MockSceneSession.Begin(kind, tool, target);
            SetStatus(kind == MockSessionKind.Toolbar
                ? "シーンで作業を開始しました。クリックで選択、C 長押しでラジアル、Esc で終了。"
                : "シーンで編集を開始しました。頂点をクリック、押したままホイールで半径、Esc で終了。", StatusType.Info);
        }

        private bool RequireSceneView()
        {
            if (SceneView.lastActiveSceneView != null) return true;
            SetStatus("シーンビューを開いてください。", StatusType.Error);
            return false;
        }

        // ─── 16. パイメニューのテンプレート ─────────────────────────

        private void BindRadialTemplate(VisualElement root)
        {
            var keys = root.Q("radial-keys");
            MockUI.AddKeyHint(keys, "押したまま向けて離す", "実行");
            MockUI.AddKeyHint(keys, "中心で離す", "キャンセル");
            MockUI.AddKeyHint(keys, "タップ", "開いたまま → クリックで選ぶ");
            MockUI.AddKeyHint(keys, "止まって待つ", "キー表記");

            root.Q<Button>("radial-open").clicked += () =>
            {
                if (!RequireSceneView()) return;
                if (MockSceneSession.Kind != MockSessionKind.Toolbar) StartScene(MockSessionKind.Toolbar);
                MockRadialMenu.Menu.OpenSticky();
                SetStatus("シーン中央にラジアルを出しました。向けてクリックで実行、右クリック / Esc で閉じる。", StatusType.Info);
            };
            root.Q<Button>("radial-shortcuts").clicked += () => EditorApplication.ExecuteMenuItem("Edit/Shortcuts...");
        }
    }
}
