using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Dennokoworks.UIDesignTest.Mock
{
    // P1 変換: 入力 (B1) → 設定 (B2) → プレビュー (B3) → 出力ドック (B5) → 主ボタン「生成」
    internal sealed partial class MockPatternHost
    {
        private void BuildP1(VisualElement root)
        {
            var status = _status;
            var target = root.Q<ObjectField>("target-field");
            var info = root.Q("target-info");
            var warning = root.Q("target-warning");
            var primary = root.Q<Button>("primary-button");
            bool readWriteWarning = false;
            bool generating = false;

            var preview = new MockPreview(root.Q("preview-card"), status);

            // ─── B2 設定カード ──────────────────────────────────
            var strength = root.Q<Slider>("opt-strength");
            var blur = root.Q<Slider>("opt-blur");
            var invertG = root.Q<Toggle>("opt-invert-g");
            var sample = root.Q<DropdownField>("opt-sample");
            var wrap = root.Q<DropdownField>("opt-wrap");
            var mip = root.Q<Toggle>("opt-mip");
            var chips = root.Q("opt-details-chips");

            sample.choices = new List<string> { "Sobel", "Scharr", "Prewitt" };
            sample.index = 0;
            wrap.choices = new List<string> { "繰り返し", "端で止める" };
            wrap.index = 1;   // 初期値と違う値にして、閉じた「詳細」にチップが出る例にする

            // 閉じている間は「初期値と違う項目」だけをチップで要約する
            void UpdateChips()
            {
                chips.Clear();
                if (sample.index != 0) chips.Add(MockUI.Chip(null, sample.value));
                if (wrap.index != 0) chips.Add(MockUI.Chip(null, wrap.value));
                if (!mip.value) chips.Add(MockUI.Chip(null, "ミップなし"));
            }

            int heightMode = 0;
            var setHeight = MockUI.BindChoice(root.Q("opt-height"), 0, i => { heightMode = i; OnOptionChanged(); });
            MockUI.BindDetails(root.Q("opt-details"));

            void OnOptionChanged()
            {
                UpdateChips();
                if (target.value != null) preview.Invalidate();
            }

            strength.RegisterValueChangedCallback(_ => OnOptionChanged());
            blur.RegisterValueChangedCallback(_ => OnOptionChanged());
            invertG.RegisterValueChangedCallback(_ => OnOptionChanged());
            sample.RegisterValueChangedCallback(_ => OnOptionChanged());
            wrap.RegisterValueChangedCallback(_ => OnOptionChanged());
            mip.RegisterValueChangedCallback(_ => OnOptionChanged());
            UpdateChips();

            DennokoIcons.Apply(root.Q<Button>("options-help"), DennokoIcons.Help, "?",
                "強さ: 凹凸の強さ\nぼかし: 生成前に元画像をぼかす量\n高さ: 明るい所と暗い所のどちらを高いとみなすか");
            var reset = DennokoIcons.Apply(root.Q<Button>("options-reset"), DennokoIcons.Reset, "↺", "このカードを初期値に戻す");
            reset.clicked += () =>
            {
                // Undo で戻せるので確認ダイアログは出さない (全体のリセットは ⋮ から、確認あり)
                strength.value = 2f;
                blur.value = 1f;
                invertG.value = false;
                setHeight(0);
                heightMode = 0;
                sample.index = 0;
                wrap.index = 0;
                mip.value = true;
                status.Set("「生成」を初期値に戻しました");
            };

            // ─── B3 プレビュー ──────────────────────────────────
            preview.MakeImage = () => MockUI.NormalMapTexture(strength.value * (heightMode == 0 ? 1f : 0.6f));
            preview.MakeInfo = () =>
            {
                var tex = target.value as Texture2D;
                string size = tex != null ? $"{tex.width} × {tex.height}" : "—";
                return $"{size} · 強さ {strength.value:0.0} · 今更新";
            };

            // ─── B1 対象カード ──────────────────────────────────
            void RefreshTarget()
            {
                var tex = target.value as Texture2D;
                bool has = tex != null;
                MockUI.Show(info, has);
                MockUI.Show(warning, has && readWriteWarning);
                primary.SetEnabled(has && !generating);
                primary.tooltip = has ? string.Empty : "入力を選んでください";
                if (has)
                {
                    root.Q<Label>("target-info-size").text = $"{tex.width} × {tex.height}";
                    root.Q<Label>("target-info-format").text = tex.format.ToString();
                    preview.Compute();
                }
                else
                {
                    preview.SetEmpty("入力を選ぶとここに表示されます");
                }
            }

            target.RegisterValueChangedCallback(_ => RefreshTarget());
            root.Q<Button>("target-from-selection").clicked += () =>
            {
                if (Selection.activeObject is Texture2D tex) target.value = tex;
                else status.Set("Project ビューでテクスチャを選んでください", StatusType.Warning);
            };
            root.Q<Button>("target-fix").clicked += () =>
            {
                readWriteWarning = false;
                RefreshTarget();
                status.Set("Read/Write を有効にしました", StatusType.Success);
            };
            RefreshTarget();

            // ─── B5 出力ドック + 主ボタン ───────────────────────
            MockUI.BindChoice(root.Q("output-format"), 0, null);
            root.Q<Button>("output-browse").clicked += () => status.Set("フォルダを選ぶダイアログを開きます (モック)");

            primary.clicked += () =>
            {
                generating = true;
                primary.SetEnabled(false);
                string name = (target.value != null ? target.value.name : "Texture") + "_normal.png";
                status.RunFakeProgress("生成中…", 1400, () =>
                {
                    generating = false;
                    primary.SetEnabled(target.value != null);
                    status.Set($"保存: {name}", StatusType.Success);
                });
            };

            // ─── モックの状態切替 ───────────────────────────────
            _mockItems = menu =>
            {
                menu.AddItem(new GUIContent("モック/対象: ダミー画像を入れる"), false, () =>
                {
                    var dummy = MockUI.NormalMapTexture(0f);
                    dummy.name = "Brick_height (ダミー)";
                    target.value = dummy;
                });
                menu.AddItem(new GUIContent("モック/対象: 空にする"), false, () => target.value = null);
                menu.AddItem(new GUIContent("モック/対象: Read/Write 無効の警告"), readWriteWarning, () =>
                {
                    readWriteWarning = !readWriteWarning;
                    RefreshTarget();
                });
                menu.AddItem(new GUIContent("モック/プレビュー: 軽い (即時更新)"), preview.UpdateCost == MockPreview.Cost.Light,
                    () => preview.UpdateCost = MockPreview.Cost.Light);
                menu.AddItem(new GUIContent("モック/プレビュー: 中くらい (300ms 後に更新)"), preview.UpdateCost == MockPreview.Cost.Medium,
                    () => preview.UpdateCost = MockPreview.Cost.Medium);
                menu.AddItem(new GUIContent("モック/プレビュー: 重い (手動更新 + 古い表示)"), preview.UpdateCost == MockPreview.Cost.Heavy,
                    () => preview.UpdateCost = MockPreview.Cost.Heavy);
                menu.AddItem(new GUIContent("モック/プレビュー: エラーを出す"), false,
                    () => preview.SetError("生成に失敗しました: 圧縮形式のテクスチャは読めません"));
                menu.AddItem(new GUIContent("モック/ステータス: 警告"), false,
                    () => status.Set("一部のピクセルが透明なので、黒として扱いました", StatusType.Warning));
                menu.AddItem(new GUIContent("モック/ステータス: エラー"), false,
                    () => status.Set("保存に失敗しました: フォルダが読み取り専用です", StatusType.Error));
            };
        }
    }
}
