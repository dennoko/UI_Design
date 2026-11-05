using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Dennokoworks.UIDesignTest.Mock
{
    // P2 手順: 手順タブ + その手順のカード + フッター (← 戻る / 次へ：<次の手順> / 最後だけ「出力」)
    internal sealed partial class MockPatternHost
    {
        private static readonly string[] StepNames = { "切り分け", "パーツ", "調整", "出力" };

        private void BuildP2(VisualElement root)
        {
            var status = _status;
            int count = StepNames.Length;
            var tabs = new Button[count];
            var panels = new VisualElement[count];
            var done = new bool[count];
            var changedWarning = new bool[count];
            int current = 0;

            var back = root.Q<Button>("back-button");
            var primary = root.Q<Button>("primary-button");
            var scroll = root.Q<ScrollView>("main-scroll");

            for (int i = 0; i < count; i++)
            {
                int index = i;
                tabs[i] = root.Q<Button>($"step-{i}");
                tabs[i].tooltip = $"{i + 1}. {StepNames[i]}";
                tabs[i].clicked += () => Select(index);   // 前後への移動は自由
                panels[i] = root.Q($"step-panel-{i}");
                var go = root.Q<Button>($"step-empty-go-{i}");
                if (go != null) go.clicked += () => Select(index - 1);
            }

            void Refresh()
            {
                for (int i = 0; i < count; i++)
                {
                    tabs[i].EnableInClassList(MockUI.ActiveClass, i == current);
                    tabs[i].EnableInClassList("dennoko-tab--done", done[i]);
                    // 未到達 = 前の手順が終わっていない
                    tabs[i].EnableInClassList("dennoko-tab--pending", i > 0 && !done[i - 1] && i != current);
                    var badge = tabs[i].Q<Label>("badge");
                    badge.text = done[i] ? "✓" : (i + 1).ToString();

                    MockUI.Show(panels[i], i == current);
                    // 前の手順が終わっていなければ、中身の代わりに空の状態を出す
                    bool reachable = i == 0 || done[i - 1];
                    MockUI.Show(panels[i].Q($"step-empty-{i}"), !reachable);
                    MockUI.Show(panels[i].Q($"step-content-{i}"), reachable);
                    MockUI.Show(panels[i].Q($"step-warning-{i}"), reachable && changedWarning[i]);
                }

                back.SetEnabled(current > 0);
                bool last = current == count - 1;
                primary.text = last ? "出力" : $"次へ：{StepNames[current + 1]}  →";
                bool canRun = current == 0 || done[current - 1];
                primary.SetEnabled(canRun);
                primary.tooltip = canRun ? string.Empty : $"先に「{StepNames[current - 1]}」を完了してください";
            }

            void Select(int index)
            {
                current = Mathf.Clamp(index, 0, count - 1);
                scroll.scrollOffset = Vector2.zero;
                Refresh();
            }

            back.clicked += () => Select(current - 1);
            primary.clicked += () =>
            {
                done[current] = true;
                changedWarning[current] = false;
                if (current == count - 1)
                {
                    status.RunFakeProgress("出力中…", 1200, () => status.Set("保存: Hair_Chimera.prefab", StatusType.Success));
                    Refresh();
                    return;
                }
                status.Set($"「{StepNames[current]}」を完了しました", StatusType.Success);
                Select(current + 1);
            };

            // 手順 1 のフォーム
            var hair = root.Q<ObjectField>("hair-field");
            root.Q<Button>("hair-from-selection").clicked += () =>
            {
                if (Selection.activeGameObject != null) hair.value = Selection.activeGameObject;
                else status.Set("Hierarchy で髪のオブジェクトを選んでください", StatusType.Warning);
            };
            MockUI.BindChoice(root.Q("split-unit"), 0, null);
            DennokoIcons.Apply(root.Q<Button>("split-reset"), DennokoIcons.Reset, "↺", "このカードを初期値に戻す");
            DennokoIcons.Apply(root.Q<Button>("adjust-reset"), DennokoIcons.Reset, "↺", "このカードを初期値に戻す");

            Refresh();

            _mockItems = menu =>
            {
                menu.AddItem(new GUIContent("モック/手順 1 をやり直す (後の手順の ✓ が外れる)"), false, () =>
                {
                    for (int i = 1; i < count; i++)
                    {
                        if (done[i]) changedWarning[i] = true;
                        done[i] = false;
                    }
                    // 手順 2 以降は、前の手順が終わった状態で警告を見せるために 1 だけ完了扱いのまま残す
                    done[0] = true;
                    changedWarning[1] = true;
                    Select(1);
                    status.Set("手順 1 の設定が変わりました。後の手順を確認してください", StatusType.Warning);
                });
                menu.AddItem(new GUIContent("モック/すべての手順を完了にする"), false, () =>
                {
                    for (int i = 0; i < count; i++) done[i] = true;
                    Refresh();
                });
            };
        }
    }
}
