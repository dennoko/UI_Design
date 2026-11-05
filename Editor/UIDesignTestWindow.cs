using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Dennokoworks.UIDesignTest
{
    /// <summary>
    /// dennokoworks デザインシステム (DennokoTheme.uss) の UI コンポーネントを
    /// 一覧表示して、見た目・テーマ切替・縮小時の挙動を確認するためのサンプルウィンドウ。
    /// </summary>
    public partial class UIDesignTestWindow : EditorWindow
    {
        private const string UXML_GUID      = "fbe6a863b0224b27ab1b883f36fcb4ab"; // UI/UIDesignTestWindow.uxml
        private const string THEME_USS_GUID = "3e31c0ea7a244072bb77655bf4a573c8"; // UI/DennokoTheme.uss
        private const string LOCAL_USS_GUID = "c60d2e304f5f4c5f8e6e5d65ed54ccad"; // UI/UIDesignTest.uss

        private const string ActiveClass = "dennoko-button-active";

        public enum StatusType { Info, Success, Error }

        private enum SampleBlendMode { Normal, Multiply, Screen, Overlay, Additive }

        [Flags]
        private enum SampleChannels { None = 0, Red = 1, Green = 2, Blue = 4, Alpha = 8 }

        private Label _statusLabel;
        private IVisualElementScheduledItem _statusResetSchedule;
        private IVisualElementScheduledItem _progressSchedule;

        private readonly List<string> _listItems = new List<string>();
        private int _listCounter;

        [MenuItem("dennokoworks/UI_design_test")]
        public static void ShowWindow()
        {
            var window = GetWindow<UIDesignTestWindow>();
            window.titleContent = new GUIContent("UI Design Test");
            window.minSize = new Vector2(360, 480);
        }

        public void CreateGUI()
        {
            VisualElement root = rootVisualElement;

            root.AddToClassList("dennoko-root");
            // USS ロード失敗時も背景が明るくならないよう Surface0 を C# 側でも保証
            root.style.backgroundColor = (Color)new Color32(0x12, 0x12, 0x12, 0xFF);
            root.style.flexGrow = 1;

            DennokoUIFont.Apply(root);

            AddStyleSheet(root, THEME_USS_GUID);
            AddStyleSheet(root, Mock.MockAssets.BLOCKS_USS);   // 提案中のブロック (Mock/UI/DennokoBlocks.uss)
            AddStyleSheet(root, Mock.MockAssets.MOCK_USS);     // 画面パターンの見本専用 (Mock/UI/UIMock.uss)
            AddStyleSheet(root, LOCAL_USS_GUID);   // テーマより後に追加する (同詳細度は後勝ち)

            string uxmlPath = AssetDatabase.GUIDToAssetPath(UXML_GUID);
            var uxml = string.IsNullOrEmpty(uxmlPath)
                ? null
                : AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxmlPath);
            if (uxml == null)
            {
                root.Add(new Label("UXML Asset が見つかりません。GUID を確認してください。"));
                return;
            }
            uxml.CloneTree(root);

            InitializeUI(root);
        }

        private void OnDisable()
        {
            _progressSchedule?.Pause();
            _statusResetSchedule?.Pause();
            DisposeProposalSections();
        }

        private static void AddStyleSheet(VisualElement root, string guid)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var uss = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
            if (uss != null)
                root.styleSheets.Add(uss);
            else
                Debug.LogWarning($"[{nameof(UIDesignTestWindow)}] USS が見つかりません。GUID を確認してください: {guid}");
        }

        // ─── バインディング ─────────────────────────────────────────────

        private void InitializeUI(VisualElement root)
        {
            _statusLabel = root.Q<Label>("status-label");

            BindHeader(root);
            BindButtons(root);
            BindSelectionFields(root);
            BindEditorFields(root);
            BindSliders(root);
            BindToggles(root);
            BindToolbarAndTabs(root);
            BindList(root);
            BindFeedback(root);
            BindDisabledState(root);

            root.Q<Button>("apply-button").clicked += () => SetStatus("保存しました。", StatusType.Success);
            root.Q<Button>("reset-all-button").clicked += ResetAll;

            // 提案セクション (14〜16)。画面パターンの枠にも "status-label" などがあるので、
            // ウィンドウ側の Q() を済ませてから最後に組み立てる
            BindProposalSections(root);
        }

        // ヘッダー: バージョン表示の状態切替 / 全体の無効化
        private void BindHeader(VisualElement root)
        {
            var versionLabel = root.Q<Label>("version-label");
            int versionState = 0;
            root.Q<Button>("version-reload-button").clicked += () =>
            {
                versionState = (versionState + 1) % 3;
                versionLabel.text = versionState switch
                {
                    1 => "v1.0.0 → v1.1.0",
                    2 => "v1.0.0 (取得失敗)",
                    _ => "v1.0.0",
                };
                versionLabel.EnableInClassList("dennoko-version-label--update", versionState == 1);
                versionLabel.EnableInClassList("dennoko-version-label--error",  versionState == 2);
            };

            var scroll = root.Q<ScrollView>("main-scroll");
            var disableAll = root.Q<Button>("disable-all-button");
            disableAll.clicked += () =>
            {
                bool disable = scroll.enabledSelf;
                scroll.SetEnabled(!disable);
                disableAll.text = disable ? "全体を有効化" : "全体を無効化";
                disableAll.EnableInClassList(ActiveClass, disable);
                SetStatus(disable ? "スクロール領域を無効化しました。" : "スクロール領域を有効化しました。", StatusType.Info);
            };
        }

        // ボタン: 排他選択グループ / ON-OFF トグルボタン
        private void BindButtons(VisualElement root)
        {
            root.Q<Button>("disabled-button").SetEnabled(false);

            BindExclusiveGroup(root, new[] { "mode-select", "mode-paint" }, 0,
                name => SetStatus($"モード: {name}", StatusType.Info));
            BindExclusiveGroup(root, new[] { "tool-brush", "tool-rect", "tool-lasso", "tool-eraser" }, 0,
                name => SetStatus($"ツール: {name}", StatusType.Info));

            var onOff = root.Q<Button>("onoff-button");
            var onOffLabel = root.Q<Label>("onoff-label");
            bool isOn = false;
            onOff.clicked += () =>
            {
                isOn = !isOn;
                onOff.text = isOn ? "Preview: ON" : "Preview: OFF";
                onOff.EnableInClassList(ActiveClass, isOn);
                onOffLabel.text = isOn ? "プレビューは有効です" : "プレビューは無効です";
            };
        }

        /// <summary>複数ボタンのうち 1 つだけに青枠 (.dennoko-button-active) を付ける排他グループ。</summary>
        private static void BindExclusiveGroup(VisualElement root, string[] names, int initialIndex, Action<string> onSelect)
        {
            var buttons = names.Select(n => root.Q<Button>(n)).ToArray();

            void Select(int index)
            {
                for (int i = 0; i < buttons.Length; i++)
                    buttons[i].EnableInClassList(ActiveClass, i == index);
            }

            for (int i = 0; i < buttons.Length; i++)
            {
                int index = i;
                buttons[i].clicked += () =>
                {
                    Select(index);
                    onSelect?.Invoke(buttons[index].text);
                };
            }
            Select(initialIndex);
        }

        // 選択系: 選択肢は C# から与える
        private static void BindSelectionFields(VisualElement root)
        {
            var dropdown = root.Q<DropdownField>("dropdown-field");
            dropdown.choices = new List<string> { "オプション A", "オプション B", "オプション C" };
            dropdown.index = 0;

            root.Q<EnumField>("enum-field").Init(SampleBlendMode.Normal);
            root.Q<EnumFlagsField>("enum-flags-field").Init(SampleChannels.Red | SampleChannels.Green);

            var mask = root.Q<MaskField>("mask-field");
            mask.choices = new List<string> { "Body", "Hair", "Face", "Cloth" };
            mask.value = 0b0101;

            var radio = root.Q<RadioButtonGroup>("radio-group");
            radio.choices = new List<string> { "Low", "Medium", "High" };
            radio.value = 1;
        }

        // エディタ専用フィールド
        private void BindEditorFields(VisualElement root)
        {
            var hrowField = root.Q<ObjectField>("hrow-object-field");
            root.Q<Button>("hrow-clear-button").clicked += () =>
            {
                hrowField.value = null;
                SetStatus("Target をクリアしました。", StatusType.Info);
            };

            root.Q<ColorField>("color-field").value = new Color(0.39f, 0.71f, 0.96f, 1f);
            root.Q<ColorField>("hdr-color-field").value = new Color(2f, 1f, 0.5f, 1f);
            root.Q<CurveField>("curve-field").value = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.black, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            root.Q<GradientField>("gradient-field").value = gradient;
        }

        // スライダー / プログレス
        private void BindSliders(VisualElement root)
        {
            var minMax = root.Q<MinMaxSlider>("minmax-slider");
            var minMaxLabel = root.Q<Label>("minmax-label");
            minMax.RegisterValueChangedCallback(evt =>
                minMaxLabel.text = $"範囲: {evt.newValue.x:F1} – {evt.newValue.y:F1}");

            var progress = root.Q<ProgressBar>("progress-bar");
            root.Q<Button>("progress-run-button").clicked += () =>
            {
                _progressSchedule?.Pause();
                progress.value = 0f;
                _progressSchedule = progress.schedule.Execute(() =>
                {
                    progress.value = Mathf.Min(progress.value + 2f, 100f);
                    progress.title = $"ProgressBar {progress.value:F0}%";
                    if (progress.value >= 100f)
                    {
                        _progressSchedule.Pause();
                        SetStatus("処理が完了しました。", StatusType.Success);
                    }
                }).Every(30);
            };
        }

        // トグル: 従属項目の有効化 / トグル付きセクション
        private static void BindToggles(VisualElement root)
        {
            var overwrite = root.Q<Toggle>("overwrite-toggle");
            var group = root.Q<VisualElement>("overwrite-group");
            overwrite.RegisterValueChangedCallback(evt => group.SetEnabled(evt.newValue));
            group.SetEnabled(overwrite.value);

            BindToggleSection(root,
                toggleName:  "color-correction-toggle",
                contentName: "color-correction-content",
                resetName:   "color-correction-reset",
                onReset: () =>
                {
                    root.Q<Slider>("hue-slider").value = 0f;
                    root.Q<Slider>("sat-slider").value = 1f;
                });

            root.Q<ColorField>("outline-color-field").value = Color.black;
            BindToggleSection(root,
                toggleName:  "outline-toggle",
                contentName: "outline-content",
                resetName:   "outline-reset",
                onReset: () =>
                {
                    root.Q<Slider>("outline-width-slider").value = 2f;
                    root.Q<ColorField>("outline-color-field").value = Color.black;
                    root.Q<Toggle>("outline-shadow-toggle").value = false;
                });
        }

        private const string CollapsedClass = "dennoko-toggle-section--collapsed";

        /// <summary>
        /// トグル・コンテンツ・Reset ボタンを接続する共通ヘルパー。
        /// トグル OFF でセクションを折りたたむ (カード = コンテンツの親に
        /// .dennoko-toggle-section--collapsed を付ける。見た目は USS 側で定義)。
        /// </summary>
        private static void BindToggleSection(
            VisualElement root, string toggleName, string contentName,
            string resetName, Action onReset)
        {
            var toggle  = root.Q<Toggle>(toggleName);
            var content = root.Q<VisualElement>(contentName);
            var section = content.parent;

            toggle.RegisterValueChangedCallback(evt => section.EnableInClassList(CollapsedClass, !evt.newValue));
            section.EnableInClassList(CollapsedClass, !toggle.value);

            var reset = root.Q<Button>(resetName);
            if (reset != null && onReset != null)
                reset.clicked += () => onReset();
        }

        // ツールバー / タブ
        private void BindToolbarAndTabs(VisualElement root)
        {
            root.Q<Button>("toolbar-new").clicked  += () => SetStatus("New", StatusType.Info);
            root.Q<Button>("toolbar-open").clicked += () => SetStatus("Open", StatusType.Info);

            var grid = root.Q<Button>("toolbar-grid");
            bool gridOn = false;
            grid.clicked += () =>
            {
                gridOn = !gridOn;
                grid.EnableInClassList(ActiveClass, gridOn);
            };

            var output = root.Q<DropdownField>("output-format");
            output.choices = new List<string> { "PNG", "TGA", "EXR" };
            output.index = 0;

            string[] tabs   = { "tab-general",   "tab-output",   "tab-advanced" };
            string[] panels = { "panel-general", "panel-output", "panel-advanced" };
            var tabButtons   = tabs.Select(n => root.Q<Button>(n)).ToArray();
            var panelElements = panels.Select(n => root.Q<VisualElement>(n)).ToArray();

            void SelectTab(int index)
            {
                for (int i = 0; i < tabButtons.Length; i++)
                {
                    tabButtons[i].EnableInClassList(ActiveClass, i == index);
                    panelElements[i].EnableInClassList("uitest-hidden", i != index);
                }
            }

            for (int i = 0; i < tabButtons.Length; i++)
            {
                int index = i;
                tabButtons[i].clicked += () => SelectTab(index);
            }
            SelectTab(0);
        }

        // リスト: 動的リストアイテムのみ C# で生成してよい (クラスで装飾する)
        private void BindList(VisualElement root)
        {
            _listItems.Clear();
            for (_listCounter = 1; _listCounter <= 5; _listCounter++)
                _listItems.Add($"リスト項目 {_listCounter:00}");

            var list = root.Q<ListView>("sample-list");
            list.itemsSource = _listItems;
            list.makeItem = () =>
            {
                var label = new Label();
                label.AddToClassList("uitest-list-item");
                return label;
            };
            list.bindItem = (element, index) => ((Label)element).text = _listItems[index];

            root.Q<Button>("list-add").clicked += () =>
            {
                _listItems.Add($"リスト項目 {_listCounter++:00}");
                list.Rebuild();
                list.ScrollToItem(_listItems.Count - 1);
            };
            root.Q<Button>("list-remove").clicked += () =>
            {
                var selected = list.selectedIndices.OrderByDescending(i => i).ToList();
                if (selected.Count == 0)
                {
                    SetStatus("削除する項目を選択してください。", StatusType.Error);
                    return;
                }
                foreach (int i in selected) _listItems.RemoveAt(i);
                list.ClearSelection();
                list.Rebuild();
                SetStatus($"{selected.Count} 件を削除しました。", StatusType.Success);
            };
        }

        // フィードバック: ステータスバーの状態切替
        private void BindFeedback(VisualElement root)
        {
            root.Q<Button>("status-info").clicked    += () => SetStatus("情報メッセージ (Info)", StatusType.Info);
            root.Q<Button>("status-success").clicked += () => SetStatus("処理が完了しました。", StatusType.Success);
            root.Q<Button>("status-error").clicked   += () => SetStatus("エラーが発生しました。", StatusType.Error);
        }

        // 無効状態の見本
        private static void BindDisabledState(VisualElement root)
        {
            var dropdown = root.Q<DropdownField>("disabled-dropdown");
            dropdown.choices = new List<string> { "選択肢" };
            dropdown.index = 0;

            root.Q<VisualElement>("disabled-content").SetEnabled(false);
        }

        // ─── ステータスバー ─────────────────────────────────────────────

        /// <summary>ステータスを表示する。Success / Error は 3 秒後に Ready へ自動復帰。</summary>
        private void SetStatus(string message, StatusType type, long autoResetMs = 3000)
        {
            if (_statusLabel == null) return;

            _statusLabel.text = message;
            _statusLabel.EnableInClassList("dennoko-status--success", type == StatusType.Success);
            _statusLabel.EnableInClassList("dennoko-status--error",   type == StatusType.Error);

            _statusResetSchedule?.Pause();
            if (type != StatusType.Info)
            {
                _statusResetSchedule = _statusLabel.schedule
                    .Execute(() => SetStatus("Ready", StatusType.Info))
                    .StartingIn(autoResetMs);
            }
        }

        // ─── アクション ─────────────────────────────────────────────────

        /// <summary>UI を作り直して全サンプルを初期状態へ戻す。</summary>
        private void ResetAll()
        {
            _progressSchedule?.Pause();
            _statusResetSchedule?.Pause();
            DisposeProposalSections();

            var root = rootVisualElement;
            root.Clear();
            root.styleSheets.Clear();
            CreateGUI();

            SetStatus("すべてのサンプルを初期状態に戻しました。", StatusType.Info);
        }
    }
}
