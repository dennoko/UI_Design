using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Dennokoworks.UIDesignTest.Mock
{
    // P3 一覧編集: 対象 (B1) + 設定 (B2) + リスト (B4、見出しの件数が B6 確認の絞り込みを兼ねる) + フッター
    internal sealed partial class MockPatternHost
    {
        private enum RenameKind { Changed, Unchanged, Conflict }

        private sealed class RenameItem
        {
            public string Name;
            public string NewName;
            public RenameKind Kind;
        }

        private static readonly string[] MockFiles =
        {
            "Hair_01.png", "Hair_02.png", "Hair_03.png", "Hair_04.png", "Hair_Mask.png", "HAIR_Mask.png",
            "Hair_Normal.png", "Hair_AO.png", "Hair_Emission.png", "Hair_Matcap.png", "Hair_Outline.png",
            "Hair_Shadow.png", "Body.png", "Face.png", "Eye_L.png", "Eye_R.png",
        };

        private void BuildP3(VisualElement root)
        {
            var status = _status;
            var target = root.Q<ObjectField>("target-field");
            var find = root.Q<TextField>("find-field");
            var replace = root.Q<TextField>("replace-field");
            var caseToggle = root.Q<Toggle>("case-toggle");
            var chipChanged = root.Q<Button>("chip-changed");
            var chipUnchanged = root.Q<Button>("chip-unchanged");
            var chipConflict = root.Q<Button>("chip-conflict");
            var search = root.Q<TextField>("search-field");
            var searchClear = root.Q<Button>("search-clear");
            var count = root.Q<Label>("search-count");
            var list = root.Q<ListView>("item-list");
            var empty = root.Q("list-empty");
            var emptyMessage = root.Q<Label>("list-empty-message");
            var emptyAction = root.Q<Button>("list-empty-action");
            var footerInfo = root.Q<Label>("footer-info");
            var undo = root.Q<Button>("undo-button");
            var primary = root.Q<Button>("primary-button");

            var all = new List<RenameItem>();
            var shown = new List<RenameItem>();
            var filters = new HashSet<RenameKind>();
            bool applied = false;
            var fileIcon = DennokoIcons.Find(DennokoIcons.Texture);

            caseToggle.value = false;   // 大文字小文字を区別しない → HAIR_Mask.png が衝突する例
            MockUI.AddPlaceholder(search, "名前で絞り込み");
            DennokoIcons.Apply(searchClear, DennokoIcons.Close, "✕", "絞り込みを解除");
            DennokoIcons.Apply(root.Q<Button>("options-help"), DennokoIcons.Help, "?", "検索した文字を置換の文字に置き換えます");
            var reset = DennokoIcons.Apply(root.Q<Button>("options-reset"), DennokoIcons.Reset, "↺", "このカードを初期値に戻す");
            reset.clicked += () =>
            {
                find.value = string.Empty;
                replace.value = string.Empty;
                caseToggle.value = false;
                status.Set("「置換」を初期値に戻しました");
            };

            // ─── 差分の計算 ─────────────────────────────────────
            void Recompute()
            {
                all.Clear();
                if (target.value == null) { RefreshList(); return; }

                var options = caseToggle.value ? RegexOptions.None : RegexOptions.IgnoreCase;
                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var name in MockFiles)
                {
                    string next = string.IsNullOrEmpty(find.value)
                        ? name
                        : Regex.Replace(name, Regex.Escape(find.value), replace.value.Replace("$", "$$"), options);
                    all.Add(new RenameItem { Name = name, NewName = next });
                }
                // 変わらない名前は先に予約しておき、変わる名前が重なったら衝突にする
                foreach (var item in all.Where(i => i.NewName == i.Name)) { item.Kind = RenameKind.Unchanged; used.Add(item.Name); }
                foreach (var item in all.Where(i => i.NewName != i.Name))
                    item.Kind = used.Add(item.NewName) ? RenameKind.Changed : RenameKind.Conflict;
                RefreshList();
            }

            // ─── 表示 ───────────────────────────────────────────
            void RefreshList()
            {
                int changed = all.Count(i => i.Kind == RenameKind.Changed);
                int unchanged = all.Count(i => i.Kind == RenameKind.Unchanged);
                int conflict = all.Count(i => i.Kind == RenameKind.Conflict);
                chipChanged.text = $"変更 {changed}";
                chipUnchanged.text = $"変更なし {unchanged}";
                chipConflict.text = $"衝突 {conflict}";
                chipChanged.EnableInClassList(MockUI.ActiveClass, filters.Contains(RenameKind.Changed));
                chipUnchanged.EnableInClassList(MockUI.ActiveClass, filters.Contains(RenameKind.Unchanged));
                chipConflict.EnableInClassList(MockUI.ActiveClass, filters.Contains(RenameKind.Conflict));
                MockUI.Show(chipConflict, conflict > 0 || filters.Contains(RenameKind.Conflict));

                string q = search.value ?? string.Empty;
                shown.Clear();
                shown.AddRange(all.Where(i =>
                    (filters.Count == 0 || filters.Contains(i.Kind)) &&
                    (q.Length == 0 || i.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                     i.NewName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)));
                list.itemsSource = shown;
                list.Rebuild();
                count.text = $"{shown.Count} / {all.Count}";
                MockUI.Show(searchClear, q.Length > 0 || filters.Count > 0);

                // 空の状態は 2 種類: 対象が無い / 絞り込みで 0 件
                bool noTarget = target.value == null;
                bool noMatch = !noTarget && shown.Count == 0;
                MockUI.Show(empty, noTarget || noMatch);
                emptyMessage.text = noTarget ? "フォルダを選ぶと一覧が出ます" : "一致する項目がありません";
                emptyAction.text = noTarget ? "選択から" : "絞り込みを解除";

                primary.SetEnabled(changed > 0);
                primary.tooltip = changed > 0 ? string.Empty : "変更される項目がありません";
                undo.SetEnabled(applied);
                RefreshFooter();
            }

            void RefreshFooter()
            {
                int selected = list.selectedIndices.Count();
                int changed = all.Count(i => i.Kind == RenameKind.Changed);
                int conflict = all.Count(i => i.Kind == RenameKind.Conflict);
                footerInfo.text = selected > 0
                    ? $"{selected} 件を選択中"
                    : conflict > 0 ? $"{changed} 件を変更 · {conflict} 件スキップ" : $"{changed} 件を変更";
            }

            // ─── 行テンプレート ─────────────────────────────────
            list.makeItem = () =>
            {
                var row = new VisualElement();
                row.AddToClassList("dennoko-list-row");
                var icon = new VisualElement();
                icon.AddToClassList("dennoko-icon");
                if (fileIcon != null) icon.style.backgroundImage = fileIcon;
                row.Add(icon);
                var name = new Label { name = "name" };
                name.AddToClassList("dennoko-list-row__name");
                row.Add(name);
                var arrow = new Label("→") { name = "arrow" };
                arrow.AddToClassList("dennoko-list-row__arrow");
                row.Add(arrow);
                var next = new Label { name = "new" };
                next.AddToClassList("dennoko-list-row__new");
                row.Add(next);
                var sub = new Label { name = "sub" };
                sub.AddToClassList("dennoko-list-row__sub");
                row.Add(sub);
                return row;
            };
            list.bindItem = (element, index) =>
            {
                var item = shown[index];
                element.EnableInClassList("dennoko-list-row--unchanged", item.Kind == RenameKind.Unchanged);
                element.EnableInClassList("dennoko-list-row--conflict", item.Kind == RenameKind.Conflict);
                element.Q<Label>("name").text = item.Name;
                bool changes = item.Kind != RenameKind.Unchanged;
                MockUI.Show(element.Q("arrow"), changes);
                MockUI.Show(element.Q("new"), changes);
                element.Q<Label>("new").text = item.NewName;
                element.Q<Label>("sub").text = item.Kind switch
                {
                    RenameKind.Conflict  => "名前が重複",
                    RenameKind.Unchanged => "変更なし",
                    _                    => string.Empty,
                };
                element.tooltip = item.Kind == RenameKind.Conflict
                    ? $"{item.NewName} は既にあるため、この項目はスキップされます"
                    : string.Empty;
            };
            list.selectionChanged += _ => RefreshFooter();
            list.itemsChosen += items =>
            {
                var item = items.FirstOrDefault() as RenameItem;
                if (item != null) status.Set($"{item.Name} を Ping します (モック)");
            };

            // ─── 入力 ───────────────────────────────────────────
            void ToggleFilter(RenameKind kind)
            {
                if (!filters.Remove(kind)) filters.Add(kind);
                RefreshList();
            }
            chipChanged.clicked += () => ToggleFilter(RenameKind.Changed);
            chipUnchanged.clicked += () => ToggleFilter(RenameKind.Unchanged);
            chipConflict.clicked += () => ToggleFilter(RenameKind.Conflict);

            void ClearFilters()
            {
                filters.Clear();
                search.value = string.Empty;
                RefreshList();
            }
            searchClear.clicked += ClearFilters;
            search.RegisterValueChangedCallback(_ => RefreshList());
            find.RegisterValueChangedCallback(_ => Recompute());
            replace.RegisterValueChangedCallback(_ => Recompute());
            caseToggle.RegisterValueChangedCallback(_ => Recompute());
            target.RegisterValueChangedCallback(_ => Recompute());

            void FromSelection()
            {
                var obj = Selection.activeObject;
                if (obj is DefaultAsset && AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(obj))) target.value = obj;
                else status.Set("Project ビューでフォルダを選んでください", StatusType.Warning);
            }
            root.Q<Button>("target-from-selection").clicked += FromSelection;
            emptyAction.clicked += () =>
            {
                if (target.value == null) FromSelection();
                else ClearFilters();
            };

            primary.clicked += () =>
            {
                int changed = all.Count(i => i.Kind == RenameKind.Changed);
                int conflict = all.Count(i => i.Kind == RenameKind.Conflict);
                // 取り消せない・影響が大きい操作: 衝突があっても主ボタンは無効にせず、確認ダイアログに件数を出す
                string message = conflict > 0
                    ? $"{changed} 件の名前を変更します。\n{conflict} 件は名前が重複するためスキップします。"
                    : $"{changed} 件の名前を変更します。";
                if (!EditorUtility.DisplayDialog("Batch Renamer", message, "変更する", "キャンセル")) return;
                applied = true;
                status.Set($"{changed} 件の名前を変更しました (モック)", StatusType.Success);
                RefreshList();
            };
            undo.clicked += () =>
            {
                applied = false;
                status.Set("名前の変更を元に戻しました (モック)");
                RefreshList();
            };

            // 最初はフォルダが入った状態で見せる (空の状態はモックメニューから)
            target.value = AssetDatabase.LoadAssetAtPath<DefaultAsset>("Assets");
            Recompute();

            _mockItems = menu =>
            {
                menu.AddItem(new GUIContent("モック/対象: 空にする (空の状態)"), false, () => target.value = null);
                menu.AddItem(new GUIContent("モック/対象: Assets フォルダ"), false,
                    () => target.value = AssetDatabase.LoadAssetAtPath<DefaultAsset>("Assets"));
                menu.AddItem(new GUIContent("モック/絞り込み: 一致なし (空の状態)"), false, () => search.value = "zzz");
                menu.AddItem(new GUIContent("モック/ステータス: 警告"), false,
                    () => status.Set("2 件は読み取り専用のためスキップしました", StatusType.Warning));
            };
        }
    }
}
