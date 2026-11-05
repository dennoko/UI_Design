using System.Collections.Generic;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;

namespace Dennokoworks.UIDesignTest.Mock
{
    /// <summary>
    /// DennokoRadialMenu (テンプレート) をツール側で使う例。ツールが書くのはこのファイルだけ:
    ///   1. メニュー本体を 1 つ作る (static readonly)
    ///   2. [ClutchShortcut] で Begin → OpenAtPointer() / End → Release()
    ///   3. 項目を返す関数 (8 個。0 = →、1 = ↗、2 = ↑、3 = ↖、4 = ←、5 = ↙、6 = ↓、7 = ↘)
    /// ショートカットは作業中だけ有効なタグ付きで登録する (タグは MockSceneSession が作業中だけ RegisterTag)。
    /// </summary>
    [InitializeOnLoad]
    internal static class MockRadialMenu
    {
        public static readonly DennokoRadialMenu Menu = new DennokoRadialMenu(
            "Dennokoworks-UIMock-Radial", BuildItems, MockAssets.SetupSceneRoot)
        {
            CanOpen = () => MockSceneSession.Kind == MockSessionKind.Toolbar,
        };

        static MockRadialMenu()
        {
            Menu.Executed += MockSceneSession.Raise;   // 実行後にツールバーなどの表示を読み直す
            MockSceneSession.Ended += _ => Menu.Close(false);
        }

        // 押している間だけ効くキーは ClutchShortcut。タグ付きなので作業中以外は Unity の既定 (C) に戻る
        [ClutchShortcut("dennokoworks/UI Mock/Radial Menu (Hold)", typeof(SceneView), MockSceneSession.ShortcutTag, KeyCode.C)]
        private static void Shortcut(ShortcutArguments args)
        {
            if (args.stage == ShortcutStage.Begin) Menu.OpenAtPointer();
            else Menu.Release();
        }

        // ─── 項目 (ツールに合わせて書き換える部分) ─────────────────────

        private static DennokoRadialMenu.Item[] BuildItems()
        {
            var items = new DennokoRadialMenu.Item[DennokoRadialMenu.SliceCount];
            bool paint = MockSceneSession.PaintMode;

            // ↑ モード切替・↗ 対称・↖ クリアは両モード共通 (方向を手が覚えられるように)
            items[2] = new DennokoRadialMenu.Item
            {
                Name = paint ? "選択へ" : "ペイントへ", Icon = paint ? "d_Grid.Default|d_RectTool" : "d_Grid.PaintTool",
                Fallback = paint ? "選" : "塗", Key = "Tab", Run = () => MockSceneSession.PaintMode = !paint,
            };
            items[1] = new DennokoRadialMenu.Item
            {
                Name = "対称", Icon = "d_align_horizontally_center", Fallback = "⇋", Key = "X",
                Run = () => MockSceneSession.Symmetry = !MockSceneSession.Symmetry, Active = MockSceneSession.Symmetry,
            };
            items[3] = new DennokoRadialMenu.Item
            {
                Name = "クリア", Icon = "d_TreeEditor.Trash", Fallback = "✕",
                Run = () => MockSceneSession.Modify("クリア", d => d.selectedCells.Clear()),
            };

            if (!paint)
            {
                int unit = MockSceneSession.Unit;
                items[4] = new DennokoRadialMenu.Item { Name = "全選択", Fallback = "全", Key = "Ctrl+A", Run = SelectAll };
                items[5] = new DennokoRadialMenu.Item { Name = "反転", Fallback = "反", Key = "Ctrl+I", Run = Invert };
                items[0] = Unit("島", 0, "1", unit);
                items[7] = Unit("パーツ", 1, "2", unit);
                items[6] = Unit("面", 2, "3", unit);
            }
            else
            {
                int tool = MockSceneSession.PaintTool;
                items[0] = Tool("ブラシ", 0, "d_Grid.PaintTool", "B", tool);
                items[7] = Tool("矩形", 1, "d_RectTool", "M", tool);
                items[6] = Tool("投げ縄", 2, null, "L", tool);
                items[5] = Tool("消しゴム", 3, "d_Grid.EraserTool", "Shift+B", tool);
                // 4 (←) は空き。Run を入れなければ薄く表示されて選べない
            }
            return items;
        }

        private static DennokoRadialMenu.Item Unit(string name, int value, string key, int current) => new DennokoRadialMenu.Item
        {
            Name = name, Fallback = name.Substring(0, 1), Key = key, Active = current == value,
            Run = () => MockSceneSession.Unit = value,
        };

        private static DennokoRadialMenu.Item Tool(string name, int value, string icon, string key, int current) => new DennokoRadialMenu.Item
        {
            Name = name, Icon = icon, Fallback = key.Substring(key.Length - 1), Key = key, Active = current == value,
            Run = () => MockSceneSession.PaintTool = value,
        };

        private static void SelectAll() => MockSceneSession.Modify("全選択", d =>
        {
            d.selectedCells.Clear();
            for (int i = 0; i < MockSceneDrawing.CellCount; i++) d.selectedCells.Add(i);
        });

        private static void Invert() => MockSceneSession.Modify("反転", d =>
        {
            var next = new List<int>();
            for (int i = 0; i < MockSceneDrawing.CellCount; i++) if (!d.selectedCells.Contains(i)) next.Add(i);
            d.selectedCells = next;
        });
    }
}
