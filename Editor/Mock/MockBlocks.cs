using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Dennokoworks.UIDesignTest.Mock
{
    internal enum StatusType { Info, Success, Warning, Error, Busy }

    /// <summary>
    /// 提案の DennokoStatus の試作。ステータスバーの状態切替と自動復帰。
    /// Success / Warning / Error は 3 秒で Ready に戻る。Busy は進捗バー付きで、自動では戻らない。
    /// </summary>
    internal sealed class DennokoStatus
    {
        private readonly Label _label;
        private readonly VisualElement _progress;
        private IVisualElementScheduledItem _reset;

        public DennokoStatus(Label label)
        {
            _label = label;
            if (_label == null) return;
            _progress = new VisualElement { pickingMode = PickingMode.Ignore };
            _progress.AddToClassList("dennoko-status__progress");
            _label.Add(_progress);
            MockUI.Show(_progress, false);
        }

        public void Set(string message, StatusType type = StatusType.Info, float progress = -1f)
        {
            if (_label == null) return;
            _label.text = message;
            _label.EnableInClassList("dennoko-status--success", type == StatusType.Success);
            _label.EnableInClassList("dennoko-status--warning", type == StatusType.Warning);
            _label.EnableInClassList("dennoko-status--error",   type == StatusType.Error);
            _label.EnableInClassList("dennoko-status--busy",    type == StatusType.Busy);

            bool showProgress = type == StatusType.Busy && progress >= 0f;
            MockUI.Show(_progress, showProgress);
            if (showProgress) _progress.style.width = Length.Percent(Mathf.Clamp01(progress) * 100f);

            _reset?.Pause();
            if (type == StatusType.Success || type == StatusType.Warning || type == StatusType.Error)
                _reset = _label.schedule.Execute(() => Set("Ready")).StartingIn(3000);
        }

        /// <summary>Busy の進捗バーを 0 → 1 まで進め、終わったら onDone を呼ぶ (モック用の疑似処理)。</summary>
        public void RunFakeProgress(string message, int durationMs, Action onDone)
        {
            if (_label == null) { onDone?.Invoke(); return; }
            double start = EditorApplication.timeSinceStartup;
            Set(message + "  0%", StatusType.Busy, 0f);
            IVisualElementScheduledItem item = null;
            item = _label.schedule.Execute(() =>
            {
                float t = (float)((EditorApplication.timeSinceStartup - start) * 1000.0 / durationMs);
                if (t >= 1f)
                {
                    item?.Pause();
                    onDone?.Invoke();
                    return;
                }
                Set($"{message}  {Mathf.RoundToInt(t * 100)}%", StatusType.Busy, t);
            }).Every(30);
        }
    }

    /// <summary>
    /// 提案の DennokoAppBar の試作。バージョン表示と ↻、言語ボタン、マニュアル、設定メニュー (⋮)。
    /// 設定メニューは「ツール固有の項目 (上) → 共通項目 (下)」。モックではさらに「モック」サブメニューを足す。
    /// </summary>
    internal sealed class DennokoAppBar
    {
        public event Action<bool> LanguageChanged;   // true = English
        public Action<GenericMenu> AddToolMenuItems;  // ツール固有 (上段)
        public Action<GenericMenu> AddMockMenuItems;  // モックの状態切替

        private readonly DennokoStatus _status;
        private readonly Label _version;
        private readonly string _currentVersion;
        private int _checkCount;
        private bool _english;

        public DennokoAppBar(VisualElement root, DennokoStatus status, string toolName)
        {
            _status = status;
            _version = root.Q<Label>("version-label");
            _currentVersion = _version?.text ?? "v1.0.0";

            var reload = root.Q<Button>("version-reload-button");
            DennokoIcons.Apply(reload, DennokoIcons.Refresh, "↻", "更新を確認");
            if (reload != null) reload.clicked += CheckForUpdate;

            _version?.RegisterCallback<ClickEvent>(_ =>
            {
                if (_version.ClassListContains("dennoko-version-label--update"))
                    _status.Set("配布ページを開きます (モック)", StatusType.Info);
            });

            var lang = root.Q<Button>("appbar-lang");
            if (lang != null)
            {
                lang.tooltip = "Switch to English";
                lang.clicked += () =>
                {
                    _english = !_english;
                    lang.text = _english ? "JA" : "EN";
                    lang.tooltip = _english ? "日本語に切り替え" : "Switch to English";
                    LanguageChanged?.Invoke(_english);
                    _status.Set(_english
                        ? "Language: English (モックでは文言は切り替わりません)"
                        : "言語: 日本語", StatusType.Info);
                };
            }

            var manual = root.Q<Button>("appbar-manual");
            DennokoIcons.Apply(manual, DennokoIcons.Help, "?", "マニュアルを開く");
            if (manual != null) manual.clicked += () => _status.Set($"{toolName} のマニュアル (日本語) を開きます (モック)", StatusType.Info);

            var settings = root.Q<Button>("appbar-settings");
            DennokoIcons.Apply(settings, DennokoIcons.Menu, "⋮", "設定");
            if (settings != null) settings.clicked += () => ShowMenu(settings, toolName);
        }

        private void ShowMenu(VisualElement anchor, string toolName)
        {
            var menu = new GenericMenu();
            AddToolMenuItems?.Invoke(menu);
            if (menu.GetItemCount() > 0) menu.AddSeparator(string.Empty);

            menu.AddItem(new GUIContent("設定を初期値に戻す…"), false, () =>
            {
                if (EditorUtility.DisplayDialog(toolName, "すべての設定を初期値に戻しますか？", "戻す", "キャンセル"))
                    _status.Set("設定を初期値に戻しました (モック)", StatusType.Success);
            });
            menu.AddItem(new GUIContent("更新を確認"), false, CheckForUpdate);
            menu.AddItem(new GUIContent("配布ページを開く"), false, () => _status.Set("配布ページを開きます (モック)", StatusType.Info));
            menu.AddDisabledItem(new GUIContent($"{toolName} {_currentVersion}  (最新 v9.9.9)"));

            if (AddMockMenuItems != null)
            {
                menu.AddSeparator(string.Empty);
                AddMockMenuItems(menu);
            }
            menu.DropDown(anchor.worldBound);
        }

        /// <summary>↻: 確認中 → (更新あり / 取得失敗 / 最新) を順番に見せる。</summary>
        private void CheckForUpdate()
        {
            if (_version == null) return;
            _version.text = "確認中…";
            _version.AddToClassList("dennoko-version-label--checking");
            _version.RemoveFromClassList("dennoko-version-label--update");
            _version.RemoveFromClassList("dennoko-version-label--error");
            _version.RemoveFromClassList("dennoko-version-label--clickable");

            int result = _checkCount++ % 3;
            _version.schedule.Execute(() =>
            {
                _version.RemoveFromClassList("dennoko-version-label--checking");
                switch (result)
                {
                    case 0:
                        _version.text = $"{_currentVersion} → v9.9.9";
                        _version.tooltip = "クリックで配布ページを開く";
                        _version.AddToClassList("dennoko-version-label--update");
                        _version.AddToClassList("dennoko-version-label--clickable");
                        break;
                    case 1:
                        _version.text = $"{_currentVersion} (取得失敗)";
                        _version.tooltip = "通信できませんでした。↻ でもう一度確認します";
                        _version.AddToClassList("dennoko-version-label--error");
                        break;
                    default:
                        _version.text = _currentVersion;
                        _version.tooltip = "最新です";
                        break;
                }
            }).StartingIn(700);
        }
    }

    /// <summary>モックの各パターンで共通に使う小さな処理。</summary>
    internal static class MockUI
    {
        public const string ActiveClass = "dennoko-button-active";

        public static void Show(VisualElement e, bool visible)
        {
            if (e != null) e.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>
        /// グループ直下のボタンを排他選択にする (選択中に selection 色の枠)。
        /// 戻り値で外から選択を変えられる (通知はしない)。
        /// </summary>
        public static Action<int> BindChoice(VisualElement group, int initial, Action<int> onSelect)
        {
            if (group == null) return _ => { };
            var buttons = group.Children().OfType<Button>().ToArray();

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
                    onSelect?.Invoke(index);
                };
            }
            Select(initial);
            return Select;
        }

        /// <summary>グループ直下のボタンを個別の ON / OFF にする。</summary>
        public static void BindToggles(VisualElement group, bool[] initial, Action<int, bool> onToggle)
        {
            if (group == null) return;
            var buttons = group.Children().OfType<Button>().ToArray();
            for (int i = 0; i < buttons.Length; i++)
            {
                int index = i;
                bool on = initial != null && i < initial.Length && initial[i];
                buttons[i].EnableInClassList(ActiveClass, on);
                buttons[i].clicked += () =>
                {
                    bool next = !buttons[index].ClassListContains(ActiveClass);
                    buttons[index].EnableInClassList(ActiveClass, next);
                    onToggle?.Invoke(index, next);
                };
            }
        }

        /// <summary>
        /// 「▶ 詳細」の開閉。閉じている間は chips に要約チップを出す (USS で開いている間は隠す)。
        /// </summary>
        public static void BindDetails(VisualElement details, bool open = false, Action<bool> onChanged = null)
        {
            if (details == null) return;
            var header = details.Q(className: "dennoko-details__header");
            var arrow = details.Q<Label>(className: "dennoko-details__arrow");

            void Apply(bool isOpen)
            {
                details.EnableInClassList("dennoko-details--collapsed", !isOpen);
                details.EnableInClassList("dennoko-details--open", isOpen);
                if (arrow != null) arrow.text = isOpen ? "▼" : "▶";
                onChanged?.Invoke(isOpen);
            }

            header?.RegisterCallback<ClickEvent>(_ => Apply(details.ClassListContains("dennoko-details--collapsed")));
            Apply(open);
        }

        /// <summary>要約チップ (アイコン + 値)。</summary>
        public static VisualElement Chip(string iconNames, string text, string tooltip = null, Action onClick = null)
        {
            var chip = new VisualElement { tooltip = tooltip };
            chip.AddToClassList("dennoko-summary-chip");
            if (iconNames != null) chip.Add(DennokoIcons.MakeIcon(iconNames));
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("dennoko-summary-chip__text");
            chip.Add(label);
            if (onClick != null)
            {
                chip.AddToClassList("dennoko-summary-chip--clickable");
                chip.RegisterCallback<ClickEvent>(e =>
                {
                    onClick();
                    e.StopPropagation();
                });
            }
            return chip;
        }

        /// <summary>キーキャップ + 説明の 1 組を container に足す。</summary>
        public static void AddKeyHint(VisualElement container, string key, string text)
        {
            var cap = new Label(key);
            cap.AddToClassList("dennoko-keycap");
            container.Add(cap);
            var label = new Label(text);
            label.AddToClassList("dennoko-keyhint-label");
            container.Add(label);
        }

        /// <summary>TextField に薄い案内文を重ねる (2022.3 の TextField にはプレースホルダが無い)。</summary>
        public static void AddPlaceholder(TextField field, string text)
        {
            if (field == null) return;
            var placeholder = new Label(text) { pickingMode = PickingMode.Ignore };
            placeholder.AddToClassList("dennoko-searchbar__placeholder");
            field.Add(placeholder);
            void Refresh() => Show(placeholder, string.IsNullOrEmpty(field.value));
            field.RegisterValueChangedCallback(_ => Refresh());
            Refresh();
        }

        // ─── モック用の画像 ─────────────────────────────────────────

        private static readonly Dictionary<string, Texture2D> Textures = new Dictionary<string, Texture2D>();

        /// <summary>ノーマルマップ風の画像 (P1 のプレビュー)。strength で凹凸の強さを変える。</summary>
        public static Texture2D NormalMapTexture(float strength)
        {
            string key = "normal" + Mathf.RoundToInt(strength * 10);
            if (Textures.TryGetValue(key, out var cached) && cached != null) return cached;

            const int size = 128;
            var tex = NewTexture(size);
            var px = new Color32[size * size];
            float k = 0.15f + strength * 0.08f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // レンガ風の高さ関数の勾配をそのまま法線にする
                float u = x / (float)size * Mathf.PI * 8f;
                float v = y / (float)size * Mathf.PI * 4f;
                float dx = Mathf.Cos(u) * Mathf.Sin(v * 2f) * k;
                float dy = Mathf.Sin(u) * Mathf.Cos(v * 2f) * k;
                var n = new Vector3(-dx, -dy, 1f).normalized;
                px[y * size + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
            }
            tex.SetPixels32(px);
            tex.Apply();
            Textures[key] = tex;
            return tex;
        }

        /// <summary>UV 展開図風の画像 (P4 の UV ビュー)。</summary>
        public static Texture2D UVTexture()
        {
            const string key = "uv";
            if (Textures.TryGetValue(key, out var cached) && cached != null) return cached;

            const int size = 256;
            var tex = NewTexture(size);
            var bg = new Color32(0x12, 0x12, 0x12, 0xFF);
            var grid = new Color32(0x22, 0x22, 0x22, 0xFF);
            var island = new Color32(0x3a, 0x3a, 0x3a, 0xFF);
            var selected = new Color32(0x3a, 0x6a, 0x92, 0xFF);
            var edge = new Color32(0x8e, 0x8e, 0x8e, 0xFF);
            var px = new Color32[size * size];
            RectInt[] islands =
            {
                new RectInt(16, 140, 100, 96), new RectInt(132, 160, 52, 76), new RectInt(196, 160, 44, 76),
                new RectInt(16, 24, 64, 100), new RectInt(96, 24, 64, 100), new RectInt(176, 24, 64, 120),
            };
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                Color32 c = (x % 32 == 0 || y % 32 == 0) ? grid : bg;
                for (int i = 0; i < islands.Length; i++)
                {
                    var r = islands[i];
                    if (!r.Contains(new Vector2Int(x, y))) continue;
                    bool border = x == r.xMin || y == r.yMin || x == r.xMax - 1 || y == r.yMax - 1;
                    c = border ? edge : (i == 1 || i == 2 ? selected : island);
                }
                px[y * size + x] = c;
            }
            tex.SetPixels32(px);
            tex.Apply();
            Textures[key] = tex;
            return tex;
        }

        private static Texture2D NewTexture(int size) => new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            hideFlags = HideFlags.HideAndDontSave,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
    }
}
