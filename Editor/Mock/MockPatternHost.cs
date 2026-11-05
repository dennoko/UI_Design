using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Dennokoworks.UIDesignTest.Mock
{
    internal enum MockPattern { Convert, Steps, List, SceneWork, Inspector }

    /// <summary>
    /// Docs/ui-blocks-proposal.md の画面パターン (P1〜P5) を、UI Design Test の枠の中に 1 つずつ表示する。
    /// 中身はパターンごとの UXML + partial (MockPatternHost.P1.cs など)。
    /// 状態の切り替え (空 / 警告 / エラーなど) は、表示中のパターンの ⋮ →「モック」から行う。
    /// </summary>
    internal sealed partial class MockPatternHost : IDisposable
    {
        private readonly VisualElement _container;
        private MockPattern _pattern;

        private DennokoStatus _status;
        private DennokoAppBar _appBar;
        private Action _cleanup;                  // 作り直し・破棄のときに購読を外す
        private Action<GenericMenu> _mockItems;   // パターンごとの「モック」メニュー

        public MockPattern Pattern => _pattern;

        public MockPatternHost(VisualElement container)
        {
            _container = container;
        }

        public static string Title(MockPattern p) => p switch
        {
            MockPattern.Convert   => "P1 変換",
            MockPattern.Steps     => "P2 手順",
            MockPattern.List      => "P3 一覧編集",
            MockPattern.SceneWork => "P4 シーン作業",
            _                     => "P5 Inspector",
        };

        public static string Description(MockPattern p) => p switch
        {
            MockPattern.Convert   => "入力を選び、設定して、1 回出力する (Normal Map Generator 風)。入力 → 設定 (詳細はチップで要約) → プレビュー → 出力ドック → 主ボタン「生成」。",
            MockPattern.Steps     => "前の手順の結果を次に使う (Hair Chimera 風)。手順タブ (完了で ✓)、主ボタンは「次へ：○○」、最後だけ「出力」。",
            MockPattern.List      => "一覧を見て、絞って、まとめて変える (Batch Renamer 風)。ページ全体はスクロールせず、リストが伸びる。見出しの件数が絞り込みを兼ねる。",
            MockPattern.SceneWork => "ウィンドウで始め、作業はシーンでする (Mask Maker 風)。Idle は対象と「作業開始」だけ。開始するとシーンにツールバー型の UI が出る。",
            _                     => "Inspector から編集モードに入る (Mesh Editor 風)。「シーンで編集」で開始するとシーンに編集パネルが出る。値はパネルと連動。",
        };

        private static string ToolName(MockPattern p) => p switch
        {
            MockPattern.Convert   => "Normal Map Generator",
            MockPattern.Steps     => "Hair Chimera",
            MockPattern.List      => "Batch Renamer",
            MockPattern.SceneWork => "Mask Maker",
            _                     => "Den Mesh Editor",
        };

        private static string Uxml(MockPattern p) => p switch
        {
            MockPattern.Convert   => MockAssets.P1_UXML,
            MockPattern.Steps     => MockAssets.P2_UXML,
            MockPattern.List      => MockAssets.P3_UXML,
            MockPattern.SceneWork => MockAssets.P4_UXML,
            _                     => MockAssets.P5_UXML,
        };

        /// <summary>表示するパターンを切り替える (UI を作り直す)。</summary>
        public void Show(MockPattern pattern)
        {
            _pattern = pattern;
            Build();
        }

        public void Dispose()
        {
            _cleanup?.Invoke();
            _cleanup = null;
        }

        private void Build()
        {
            Dispose();
            _mockItems = null;
            _container.Clear();

            if (!MockAssets.CloneTree(Uxml(_pattern), _container)) return;

            _status = new DennokoStatus(_container.Q<Label>("status-label"));
            _appBar = new DennokoAppBar(_container, _status, ToolName(_pattern));
            _appBar.AddMockMenuItems = menu =>
            {
                _mockItems?.Invoke(menu);
                menu.AddItem(new GUIContent("モック/最初の状態に戻す"), false, Build);
            };

            switch (_pattern)
            {
                case MockPattern.Convert:   BuildP1(_container); break;
                case MockPattern.Steps:     BuildP2(_container); break;
                case MockPattern.List:      BuildP3(_container); break;
                case MockPattern.SceneWork: BuildP4(_container); break;
                default:                    BuildP5(_container); break;
            }
        }

        private void AddCleanup(Action action) => _cleanup += action;
    }
}
