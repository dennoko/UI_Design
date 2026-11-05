# パイメニュー (ラジアルメニュー) テンプレート

2026-11-06 · dennoko

SceneView 上でキーを押している間だけ、カーソル直下に出る 8 方向のメニュー。MaskMakerV4 の `SceneHud`（C 長押しのラジアル）から、ツール固有の部分を抜いてテンプレートにしたもの。**スキルへ反映済み**（2026-11-06。`.claude/skills/dennokoworks_color_schema/blocks/radial-menu.md` と `assets/SceneView/DennokoRadialMenu.cs`）。以降はスキル側を正とする。

| ファイル | 役割 | コピー先での扱い |
| --- | --- | --- |
| `Editor/SceneUI/DennokoRadialMenu.cs` | 本体（表示・方向の判定・キー表記・タップで固定・SceneView への重ね方） | **そのままコピー**し、namespace だけ変える |
| `Editor/Mock/UI/DennokoSceneUI.uss` の「S3 ラジアルメニュー」 | 見た目（`.dennoko-radial-*`） | DennokoSceneUI.uss ごとコピー（DennokoTheme.uss の後に追加） |
| `Editor/Mock/Scene/MockRadialMenu.cs` | ツール側の書き方の例 | これを見ながら、自分のツールの項目とキーを書く |

UI Design Test の「RADIAL MENU (テンプレート)」セクションの「シーンに出す」で、動きを確認できる。

## 動き

| 操作 | 結果 |
| --- | --- |
| キーを押したまま方向へ向けて離す | その項目を実行して閉じる |
| 中心付近（18px 以内）で離す | 何もしないで閉じる（キャンセル） |
| 押してすぐ離す（0.25 秒以内・どこも向いていない） | 開いたままになる。向けて左クリックで実行、右クリック / Esc で閉じる |
| 動かさずに 0.6 秒待つ | 各項目の下にキー表記が出る |
| カーソルが SceneView の外にある | 開かない（キー入力が SceneView に残っていても出さない） |

中心には、向いている項目の名前だけを出す。項目は常に 8 個で、空きは薄く表示して選べなくする。

## ツール側で書くもの

```csharp
[InitializeOnLoad]
internal static class YourToolRadialMenu
{
    // 1. 本体を 1 つ作る。名前は他のツールと重ならないものにする
    public static readonly DennokoRadialMenu Menu = new DennokoRadialMenu(
        "YourTool-Radial", BuildItems, DennokoSceneUI.SetupRoot)
    {
        CanOpen = () => YourSceneToolState.Active,   // 作業中だけ開く
    };

    static YourToolRadialMenu()
    {
        Menu.Executed += YourSceneToolState.Raise;   // 実行後に表示を読み直す
    }

    // 2. 押している間だけ効くキーは ClutchShortcut。作業中だけ登録するタグを付ける
    [ClutchShortcut("Your Tool/Radial Menu (Hold)", typeof(SceneView), YourSceneToolState.ShortcutTag, KeyCode.C)]
    private static void Shortcut(ShortcutArguments args)
    {
        if (args.stage == ShortcutStage.Begin) Menu.OpenAtPointer();
        else Menu.Release();
    }

    // 3. 項目。0 = →、1 = ↗、2 = ↑、3 = ↖、4 = ←、5 = ↙、6 = ↓、7 = ↘ (反時計回り)
    private static DennokoRadialMenu.Item[] BuildItems()
    {
        var items = new DennokoRadialMenu.Item[DennokoRadialMenu.SliceCount];
        items[2] = new DennokoRadialMenu.Item { Name = "ペイントへ", Icon = "d_Grid.PaintTool", Fallback = "P", Key = "Tab", Run = ... };
        items[0] = new DennokoRadialMenu.Item { Name = "ブラシ", Icon = "d_Grid.PaintTool", Fallback = "B", Key = "B", Run = ..., Active = tool == Brush };
        // Run を入れない項目は空き (薄く表示)
        return items;
    }
}
```

- `BuildItems` は開くたびに呼ばれるので、モードで中身を差し替えてよい。
- `Active = true` の項目には selection 色の枠が付く（今のモード・ツール）。
- ボタンから開きたいとき（チュートリアルや見本）は `Menu.OpenSticky()`。SceneView の中央に固定表示で出る。

## 項目の決め方

- 項目は常に 8 個。数と位置を固定すると、方向を手が覚えられる。
- モードで中身を差し替えるときも、よく使う方向は全モードで揃える（例：↑ = モード切替、↗ = 対称、↖ = クリア）。
- Undo / Redo のように Ctrl+Z で済むものは入れない。
- 名前は 8 字まで。アイコンが無いときの `Fallback` は 1〜2 字。
- 切り替える項目が 4 個以下のツールでは使わない（ツールバーとキーで足りる）。

## 注意

- ショートカットは必ずタグ付きにし、作業中だけ `ShortcutManager.RegisterTag` する。C は Unity の既定キーと重なるが、タグを外せば作業後は既定に戻る。static はドメインリロードで消えるので、`beforeAssemblyReload` でタグも外す。
- メニューが開いている間は、ツール側のシーン入力（クリックで選択・Esc で終了など）を止める（`if (Menu.IsOpen) return;`）。固定表示のクリックや Esc がツールに届いてしまうため。
- 寸法（180px / 36px / 半径 64px）を変えるときは、USS と `DennokoRadialMenu` の定数を両方直す。
- 容れ物（`.dennoko-radial-menu`）には枠を付けない。absolute の子が枠の太さだけずれる。円の背景は子の `.dennoko-radial-bg` で描く。

## 確認すること

- [ ] 8 方向それぞれで、中心の名前と白枠が切り替わり、離すと実行されるか
- [ ] 中心付近で離すと何も起きないか
- [ ] タップで開いたままになり、クリックで実行、右クリック / Esc で閉じるか
- [ ] カーソルを別のウィンドウに置いてキーを押しても、どこにも出ないか
- [ ] SceneView を 2 つ開き、カーソルのある方にだけ出るか
- [ ] 再コンパイル後に二重に出ないか
- [ ] 表示スケール 150% / 200% でカーソルとメニューの中心がずれないか
