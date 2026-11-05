using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.ShortcutManagement;
using UnityEngine;

namespace Dennokoworks.UIDesignTest.Mock
{
    internal enum MockSessionKind { None, Toolbar, Panel }
    internal enum MockPanelVariant { MeshEditor, NormalEditor, Lattice }
    internal enum MockEndReason { User, Escape, PlayMode, Reload, SceneChanged, TargetLost, Replaced }

    /// <summary>
    /// 提案 §3.4 セッション規約 (DennokoSceneSession) の試作 + モックの作業状態。
    /// - 開始はウィンドウ / Inspector の主ボタンから。シーンに開始ボタンを出さない
    /// - 終了は Esc / パネルの ✕ / バナーの「終了」。どれも End() を呼ぶ
    /// - Play モード・再コンパイル・シーン切替・対象の削除で自動終了
    /// - dennokoworks の編集セッションは同時に 1 つ (別の開始で前のセッションを終了)
    /// - 作業中だけ: Overlay を表示 (ITransientOverlay)、ショートカットのタグを登録、Unity のハンドルを隠す
    /// UI はここに状態を持たず、Changed の合図で読み直す。
    /// </summary>
    [InitializeOnLoad]
    internal static class MockSceneSession
    {
        public const string ShortcutTag = "Dennokoworks.UIMock.Editing";

        public static event Action Changed;
        public static event Action<MockEndReason> Ended;

        public static MockSessionKind Kind { get; private set; }
        public static bool Active => Kind != MockSessionKind.None;
        public static string ToolName { get; private set; } = string.Empty;
        public static string TargetName { get; private set; } = string.Empty;
        public static GameObject Target { get; private set; }
        public static Vector3 Center { get; private set; }
        public static float Scale { get; private set; } = 1f;
        public static float FrontZ { get; private set; }
        public static double StartTime { get; private set; }
        public static bool ClickedSinceStart;

        // ─── P4 (Mask Maker 風) の作業中の切替 ─────────────────────────
        public static bool PaintMode;
        public static int Unit = 1;          // 0 島 / 1 パーツ / 2 面
        public static int PaintTool;         // 0 ブラシ / 1 矩形 / 2 投げ縄 / 3 消しゴム
        public static bool Symmetry = true;
        public static bool XRay;
        public static bool Isolate = true;
        public static int BrushPx = 50;

        // ─── P5 (Mesh Editor 風) ───────────────────────────────────────
        public static MockPanelVariant Variant = MockPanelVariant.MeshEditor;
        public static bool ShowWarning = true;

        private static bool _tagRegistered;
        private static bool _hadTarget;
        private static bool _prevToolsHidden;

        private static MockSceneData _data;

        public static MockSceneData Data
        {
            get
            {
                if (_data == null)
                {
                    _data = ScriptableObject.CreateInstance<MockSceneData>();
                    _data.hideFlags = HideFlags.HideAndDontSave;
                    _data.name = "UI Mock Scene Data";
                }
                return _data;
            }
        }

        static MockSceneSession()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingEditMode) End(MockEndReason.PlayMode);
            };
            // static はドメインリロードで消えるので、タグ・ハンドルの後始末を先にしておく
            AssemblyReloadEvents.beforeAssemblyReload += () => End(MockEndReason.Reload);
            EditorSceneManager.activeSceneChangedInEditMode += (_, __) => End(MockEndReason.SceneChanged);
            EditorApplication.hierarchyChanged += () =>
            {
                if (Active && _hadTarget && Target == null) End(MockEndReason.TargetLost);
            };
            Undo.undoRedoPerformed += Raise;
        }

        // ─── 開始 / 終了 ───────────────────────────────────────────────

        public static void Begin(MockSessionKind kind, string toolName, GameObject target, string targetName = null)
        {
            if (Active) End(MockEndReason.Replaced);   // 同時に 1 つ

            Kind = kind;
            ToolName = toolName;
            Target = target;
            _hadTarget = target != null;
            TargetName = targetName ?? (target != null ? target.name : "Body (ダミー)");
            StartTime = EditorApplication.timeSinceStartup;
            ClickedSinceStart = false;
            ComputeBounds();

            SetShortcutTag(true);
            _prevToolsHidden = Tools.hidden;
            Tools.hidden = true;   // 移動 / 回転のハンドルを隠す (終了で戻す)

            FrameTarget();
            Raise();
        }

        public static void End(MockEndReason reason)
        {
            if (!Active) return;
            Kind = MockSessionKind.None;
            SetShortcutTag(false);
            Tools.hidden = _prevToolsHidden;
            Ended?.Invoke(reason);
            Raise();
        }

        /// <summary>対象の切替 (作業中に B1 の ▼ から)。</summary>
        public static void SwitchTarget(string targetName)
        {
            TargetName = targetName;
            Raise();
        }

        public static string EndMessage(MockEndReason reason) => reason switch
        {
            MockEndReason.Escape       => "Esc で作業を終了しました (作業は保存済み)",
            MockEndReason.PlayMode     => "Play モードに入るため作業を終了しました (作業は保存済み)",
            MockEndReason.Reload       => "再コンパイルのため作業を終了しました",
            MockEndReason.SceneChanged => "シーンが切り替わったため作業を終了しました",
            MockEndReason.TargetLost   => "対象が削除されたため作業を終了しました",
            MockEndReason.Replaced     => "別のツールで作業を始めたため、こちらは終了しました (作業は保存済み)",
            _                          => "作業を終了しました (作業は保存済み)",
        };

        // ─── 値の変更 (Undo に載せる) ─────────────────────────────────

        public static void Modify(string undoName, Action<MockSceneData> change)
        {
            Undo.RecordObject(Data, undoName);
            change(Data);
            Raise();
        }

        public static void Raise()
        {
            Changed?.Invoke();
            SceneView.RepaintAll();
        }

        // ─── 補助 ───────────────────────────────────────────────────

        public static void FrameTarget()
        {
            var view = SceneView.lastActiveSceneView;
            if (view == null) return;
            view.Frame(new Bounds(Center, Vector3.one * Scale * 1.3f), false);
        }

        private static void ComputeBounds()
        {
            Center = new Vector3(0f, 0.6f, 0f);
            Scale = 1f;
            FrontZ = 0.1f;
            if (Target == null) return;

            var renderers = Target.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                Center = Target.transform.position;
                FrontZ = Center.z + 0.05f;
                return;
            }
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            Center = b.center;
            Scale = Mathf.Clamp(Mathf.Max(b.size.x, b.size.y), 0.3f, 5f);
            FrontZ = b.max.z + 0.01f;   // 正面 (+Z) 側の手前に「作業面」を置く
        }

        private static void SetShortcutTag(bool on)
        {
            if (on == _tagRegistered) return;
            _tagRegistered = on;
            if (on) ShortcutManager.RegisterTag(ShortcutTag);
            else ShortcutManager.UnregisterTag(ShortcutTag);
        }
    }
}
