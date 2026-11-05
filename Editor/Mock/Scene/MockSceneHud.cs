using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace Dennokoworks.UIDesignTest.Mock
{
    /// <summary>
    /// S4 シーンフィードバック (トースト / カーソルヒント / 修飾キーのバッジ) のモック。
    /// 各 SceneView の rootVisualElement に透明な全面ルートを 1 つずつ重ねて描く。
    /// (S3 ラジアルはテンプレート DennokoRadialMenu + MockRadialMenu に分けた)
    /// </summary>
    [InitializeOnLoad]
    internal static class MockSceneHud
    {
        private const string RootName = "Dennokoworks-UIMock-Hud";
        private const double LostHintDelay = 3.0;   // 開始後この秒数クリックが無ければ案内を出す
        private const int LostHintMax = 3;          // 各ヒントは 3 回まで

        private sealed class Hud
        {
            public VisualElement Root;
            public VisualElement Toast;
            public VisualElement ToastIcon;
            public Label ToastText;
            public Button ToastAction;
            public Label Hint;
            public VisualElement Badge;
            public Label BadgeText;
            public IVisualElementScheduledItem ToastHide;
            public IVisualElementScheduledItem HintHide;
        }

        private static readonly Dictionary<SceneView, Hud> Huds = new Dictionary<SceneView, Hud>();
        private static double _nextPoll;
        private static SceneView _pointerView;
        private static Vector2 _pointerGui;
        private static int _lostHintCount;
        private static bool _hintFollowsCursor;
        private static Action _toastAction;

        static MockSceneHud()
        {
            EditorApplication.update += Poll;
            SceneView.duringSceneGui += OnSceneGui;
            MockSceneSession.Ended += _ =>
            {
                HideHint();
                foreach (var hud in Huds.Values) MockUI.Show(hud.Badge, false);
            };
        }

        // ─── ショートカット (作業中だけ有効なタグ付き) ─────────────────

        [Shortcut("dennokoworks/UI Mock/Toggle Symmetry", typeof(SceneView), MockSceneSession.ShortcutTag, KeyCode.X)]
        private static void SymmetryShortcut()
        {
            if (MockSceneSession.Kind != MockSessionKind.Toolbar) return;
            MockSceneSession.Symmetry = !MockSceneSession.Symmetry;
            MockSceneSession.Raise();
        }

        [Shortcut("dennokoworks/UI Mock/Toggle Mode", typeof(SceneView), MockSceneSession.ShortcutTag, KeyCode.Tab)]
        private static void ModeShortcut()
        {
            if (MockSceneSession.Kind != MockSessionKind.Toolbar) return;
            MockSceneSession.PaintMode = !MockSceneSession.PaintMode;
            MockSceneSession.Raise();
        }

        // ─── 全面ルートの用意 ─────────────────────────────────────────

        private static void Poll()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now >= _nextPoll)
            {
                _nextPoll = now + 0.5;
                foreach (SceneView view in SceneView.sceneViews) EnsureHud(view);
                var dead = new List<SceneView>();
                foreach (var kv in Huds) if (kv.Key == null) dead.Add(kv.Key);
                foreach (var d in dead) Huds.Remove(d);
            }

            // 迷い検知: 作業開始から一定時間クリックが無ければ、カーソル脇に次の操作を 1 行だけ出す
            if (MockSceneSession.Active && !MockSceneSession.ClickedSinceStart && !_hintFollowsCursor &&
                _lostHintCount < LostHintMax && now - MockSceneSession.StartTime > LostHintDelay &&
                EditorWindow.mouseOverWindow is SceneView)
            {
                _lostHintCount++;
                ShowHint(LostHintText());
            }
        }

        private static string LostHintText() => MockSceneSession.Kind == MockSessionKind.Toolbar
            ? (MockSceneSession.PaintMode ? "ドラッグで塗る" : "クリックで選択")
            : "クリックで頂点を選ぶ";

        private static Hud EnsureHud(SceneView view)
        {
            if (view == null) return null;
            if (Huds.TryGetValue(view, out var existing) && existing.Root.panel != null) return existing;

            var parent = view.rootVisualElement;
            parent.Q(RootName)?.RemoveFromHierarchy();   // ドメインリロード前の残りを外す (二重にしない)

            var hud = new Hud { Root = new VisualElement { name = RootName, pickingMode = PickingMode.Ignore } };
            MockAssets.SetupSceneRoot(hud.Root);
            hud.Root.AddToClassList("dennoko-hud");

            // トースト (ホバー中は消さないので picking を受ける)
            hud.Toast = new VisualElement();
            hud.Toast.AddToClassList("dennoko-toast");
            hud.ToastIcon = new VisualElement { pickingMode = PickingMode.Ignore };
            hud.ToastIcon.AddToClassList("dennoko-icon");
            hud.Toast.Add(hud.ToastIcon);
            hud.ToastText = new Label();
            hud.ToastText.AddToClassList("dennoko-toast__text");
            hud.ToastText.tooltip = "クリックで Ping、ダブルクリックでフォルダを開く";
            hud.ToastText.RegisterCallback<ClickEvent>(e =>
                Toast(e.clickCount >= 2 ? "フォルダを開きます (モック)" : "Project ビューで Ping します (モック)", DennokoIcons.Info, atCursor: false));
            hud.Toast.Add(hud.ToastText);
            hud.ToastAction = new Button();
            hud.ToastAction.AddToClassList("dennoko-button-secondary");
            hud.ToastAction.AddToClassList("dennoko-toast__action");
            hud.ToastAction.clicked += () =>
            {
                var action = _toastAction;
                _toastAction = null;
                action?.Invoke();
            };
            hud.Toast.Add(hud.ToastAction);
            hud.Toast.RegisterCallback<MouseEnterEvent>(_ => hud.ToastHide?.Pause());
            hud.Toast.RegisterCallback<MouseLeaveEvent>(_ => ScheduleToastHide(hud, 1500));
            hud.Root.Add(hud.Toast);
            MockUI.Show(hud.Toast, false);

            hud.Hint = new Label { pickingMode = PickingMode.Ignore };
            hud.Hint.AddToClassList("dennoko-cursor-hint");
            hud.Root.Add(hud.Hint);
            MockUI.Show(hud.Hint, false);

            hud.Badge = new VisualElement { pickingMode = PickingMode.Ignore };
            hud.Badge.AddToClassList("dennoko-mod-badge");
            hud.BadgeText = new Label { pickingMode = PickingMode.Ignore };
            hud.BadgeText.AddToClassList("dennoko-mod-badge__text");
            hud.Badge.Add(hud.BadgeText);
            hud.Root.Add(hud.Badge);
            MockUI.Show(hud.Badge, false);

            parent.Add(hud.Root);
            Huds[view] = hud;
            return hud;
        }

        /// <summary>SceneView の GUI 座標 → rootVisualElement の座標 (上部のシーンツールバーの分だけずらす)。</summary>
        private static Vector2 ToRoot(SceneView view, Vector2 gui)
        {
            float camHeight = view.camera != null ? view.camera.pixelHeight / EditorGUIUtility.pixelsPerPoint : view.position.height;
            float offset = Mathf.Max(0f, view.rootVisualElement.layout.height - camHeight);
            return gui + new Vector2(0f, offset);
        }

        private static Vector2 ViewCenter(SceneView view)
        {
            var layout = view.rootVisualElement.layout;
            return new Vector2(layout.width * 0.5f, layout.height * 0.5f);
        }

        private static void OnSceneGui(SceneView view)
        {
            var e = Event.current;
            _pointerView = view;
            _pointerGui = e.mousePosition;

            // カーソルヒントはカーソルに付いていく
            if (_hintFollowsCursor && Huds.TryGetValue(view, out var hud) && e.type == EventType.MouseMove)
                Place(hud.Hint, ToRoot(view, e.mousePosition) + new Vector2(14f, 16f));
        }

        private static void Place(VisualElement e, Vector2 p)
        {
            e.style.left = p.x;
            e.style.top = p.y;
        }

        // ─── トースト ─────────────────────────────────────────────────

        /// <summary>
        /// 1 行のトーストを出す。シーンから実行したときはカーソルのすぐ脇、それ以外は SceneView 下端の中央。
        /// 4 秒で消える。ホバー中は消えない。操作ボタンは 1 つまで。
        /// </summary>
        public static void Toast(string text, string icon, string actionLabel = null, Action action = null, bool atCursor = true)
        {
            var view = atCursor && _pointerView != null ? _pointerView : SceneView.lastActiveSceneView;
            var hud = EnsureHud(view);
            if (hud == null) return;

            DennokoIcons.SetImage(hud.ToastIcon, icon);
            hud.ToastText.text = text;
            _toastAction = action;
            hud.ToastAction.text = actionLabel ?? string.Empty;
            MockUI.Show(hud.ToastAction, actionLabel != null);
            MockUI.Show(hud.Toast, true);

            if (atCursor && view == _pointerView)
            {
                Place(hud.Toast, ToRoot(view, _pointerGui) + new Vector2(16f, 12f));
                hud.Toast.style.bottom = StyleKeyword.Auto;
                hud.Toast.style.translate = new Translate(0, 0);
            }
            else
            {
                hud.Toast.style.left = Length.Percent(50);
                hud.Toast.style.top = StyleKeyword.Auto;
                hud.Toast.style.bottom = 16f;
                hud.Toast.style.translate = new Translate(Length.Percent(-50), 0);
            }
            ScheduleToastHide(hud, 4000);
            view.Repaint();
        }

        private static void ScheduleToastHide(Hud hud, long ms)
        {
            hud.ToastHide?.Pause();
            hud.ToastHide = hud.Toast.schedule.Execute(() => MockUI.Show(hud.Toast, false)).StartingIn(ms);
        }

        // ─── カーソルヒント / 修飾キーのバッジ ─────────────────────────

        /// <summary>カーソル脇にヒントを出す (カーソルに付いていき、クリックで消える)。</summary>
        public static void ShowHint(string text)
        {
            if (_pointerView == null) return;
            var hud = EnsureHud(_pointerView);
            hud.HintHide?.Pause();
            hud.Hint.text = text;
            Place(hud.Hint, ToRoot(_pointerView, _pointerGui) + new Vector2(14f, 16f));
            MockUI.Show(hud.Hint, true);
            _hintFollowsCursor = true;
        }

        /// <summary>見本用: SceneView の中央にヒントを 3 秒だけ出す (ボタンから呼ぶ)。</summary>
        public static void ShowHintSample(string text = null)
        {
            var view = SceneView.lastActiveSceneView;
            var hud = EnsureHud(view);
            if (hud == null) return;
            hud.Hint.text = text ?? (MockSceneSession.Active ? LostHintText() : "クリックで選択");
            Place(hud.Hint, ViewCenter(view) + new Vector2(14f, 16f));
            MockUI.Show(hud.Hint, true);
            hud.HintHide?.Pause();
            hud.HintHide = hud.Hint.schedule.Execute(() => MockUI.Show(hud.Hint, false)).StartingIn(3000);
            view.Repaint();
        }

        public static void HideHint()
        {
            _hintFollowsCursor = false;
            foreach (var hud in Huds.Values) MockUI.Show(hud.Hint, false);
        }

        /// <summary>Shift / Ctrl を押している間、カーソル脇に「足す (+) / 引く (−)」を出す。</summary>
        public static void SetModifier(SceneView view, Vector2 gui, bool shift, bool ctrl)
        {
            if (!Huds.TryGetValue(view, out var hud)) return;
            bool show = shift || ctrl;
            MockUI.Show(hud.Badge, show);
            if (!show) return;
            hud.BadgeText.text = ctrl ? "−" : "+";
            hud.Badge.tooltip = ctrl ? "外す" : "足す";
            Place(hud.Badge, ToRoot(view, gui) + new Vector2(12f, -20f));
        }
    }
}
