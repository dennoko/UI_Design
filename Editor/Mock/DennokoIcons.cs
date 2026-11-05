using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Dennokoworks.UIDesignTest.Mock
{
    /// <summary>
    /// 提案 (部品案 §C# 共通ヘルパー) の DennokoIcons の試作。
    /// Unity 組み込みアイコンを子要素 (.dennoko-icon) として付け、無ければ文字に戻す。
    /// UI はダーク固定なので、エディタテーマに関係なく d_ 版 (明るい線画) を使う。
    /// "名前|代わりの名前" と書くと、Unity のバージョン差で片方が無くても拾える。
    /// </summary>
    internal static class DennokoIcons
    {
        // よく使うアイコン名 (無ければフォールバック文字)
        public const string Refresh  = "d_Refresh|Refresh";
        public const string Help     = "d__Help|_Help";
        public const string Menu     = "d__Menu|_Menu";
        public const string Close    = "d_winbtn_win_close|winbtn_win_close";
        public const string Reset    = "d_Refresh|Refresh";
        public const string Search   = "d_Search Icon|Search Icon";
        public const string Folder   = "d_Folder Icon|Folder Icon";
        public const string Texture  = "d_Texture Icon|Texture Icon";
        public const string Mesh     = "d_SkinnedMeshRenderer Icon|SkinnedMeshRenderer Icon";
        public const string Script   = "d_cs Script Icon|cs Script Icon";
        public const string Save     = "d_SaveAs|SaveAs";
        public const string Warning  = "d_console.warnicon.sml|console.warnicon.sml";
        public const string Info     = "d_console.infoicon.sml|console.infoicon.sml";
        public const string Success  = "d_Valid|Valid|d_FilterSelectedOnly";
        public const string Check    = "d_Valid|Valid|d_FilterSelectedOnly";
        public const string Fit      = "d_ViewToolZoom|ViewToolZoom";
        public const string Channel  = "d_PreTextureRGB|PreTextureRGB";
        public const string Popout   = "d_winbtn_win_max|winbtn_win_max";
        public const string Plus     = "d_Toolbar Plus|Toolbar Plus";
        public const string Minus    = "d_Toolbar Minus|Toolbar Minus";

        private static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();

        public static Texture2D Find(string names)
        {
            if (string.IsNullOrEmpty(names)) return null;
            if (Cache.TryGetValue(names, out var cached) && cached != null) return cached;
            Texture2D tex = null;
            foreach (var n in names.Split('|'))
            {
                tex = EditorGUIUtility.FindTexture(n);
                if (tex != null) break;
            }
            Cache[names] = tex;
            return tex;
        }

        /// <summary>ボタンにアイコンを付ける。アイコンが無ければ短い文字を出す。</summary>
        public static Button Apply(Button button, string iconNames, string fallbackText, string tooltip = null)
        {
            if (button == null) return null;
            button.Q(className: "dennoko-icon")?.RemoveFromHierarchy();
            var tex = Find(iconNames);
            if (tex != null)
            {
                button.text = string.Empty;
                button.Add(MakeIcon(tex));
            }
            else
            {
                button.text = fallbackText;
            }
            if (tooltip != null) button.tooltip = tooltip;
            return button;
        }

        /// <summary>アイコン画像の要素を作る (チップや見出しに置く用)。</summary>
        public static VisualElement MakeIcon(string iconNames) => MakeIcon(Find(iconNames));

        private static VisualElement MakeIcon(Texture2D tex)
        {
            var icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.AddToClassList("dennoko-icon");
            if (tex != null) icon.style.backgroundImage = tex;
            return icon;
        }

        /// <summary>既にある .dennoko-icon 要素に画像を入れる。</summary>
        public static void SetImage(VisualElement icon, string iconNames)
        {
            if (icon == null) return;
            var tex = Find(iconNames);
            icon.style.backgroundImage = tex != null ? new StyleBackground(tex) : new StyleBackground(StyleKeyword.None);
        }
    }
}
