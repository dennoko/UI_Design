using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Dennokoworks.UIDesignTest.Mock
{
    /// <summary>
    /// B3 プレビューカードの状態 (空 / 計算中 / 古い / エラー / 表示中) を切り替えるモック。
    /// 更新の方針: 軽い = 即時、中くらい = 最後の変更から 300ms 後、重い = 手動の「更新」+「古い」表示。
    /// </summary>
    internal sealed class MockPreview
    {
        public enum Cost { Light, Medium, Heavy }

        public Cost UpdateCost = Cost.Medium;
        public Func<Texture2D> MakeImage;
        public Func<string> MakeInfo;

        private readonly VisualElement _root;
        private readonly VisualElement _image;
        private readonly Label _message;
        private readonly VisualElement _progress;
        private readonly Button _stale;
        private readonly Label _info;
        private IVisualElementScheduledItem _debounce;
        private IVisualElementScheduledItem _computing;
        private bool _hasImage;

        public MockPreview(VisualElement card, DennokoStatus status)
        {
            _root = card.Q("preview");
            _image = card.Q("preview-image");
            _message = card.Q<Label>("preview-message");
            _progress = card.Q("preview-progress");
            _stale = card.Q<Button>("preview-stale");
            _info = card.Q<Label>("preview-info");
            if (_stale != null) _stale.clicked += Compute;

            DennokoIcons.Apply(card.Q<Button>("preview-fit"), DennokoIcons.Fit, "◎", "表示を合わせる");
            DennokoIcons.Apply(card.Q<Button>("preview-channel"), DennokoIcons.Channel, "◐", "表示するチャンネルを切り替え");
            DennokoIcons.Apply(card.Q<Button>("preview-popout"), DennokoIcons.Popout, "□", "別ウィンドウで開く");
            var fit = card.Q<Button>("preview-fit");
            var channel = card.Q<Button>("preview-channel");
            var popout = card.Q<Button>("preview-popout");
            if (fit != null) fit.clicked += () => status.Set("表示を合わせました (モック)");
            if (channel != null) channel.clicked += () => status.Set("表示チャンネル: RGB → R → G → B (モック)");
            if (popout != null) popout.clicked += () => status.Set("プレビューを別ウィンドウで開きます (モック)");

            MockUI.Show(_progress, false);
            MockUI.Show(_stale, false);
        }

        public void SetEmpty(string message)
        {
            StopComputing();
            _hasImage = false;
            _image.style.backgroundImage = new StyleBackground(StyleKeyword.None);
            SetMessage(message, false);
            MockUI.Show(_stale, false);
            if (_info != null) _info.text = "—";
        }

        public void SetError(string message)
        {
            StopComputing();
            _hasImage = false;
            _image.style.backgroundImage = new StyleBackground(StyleKeyword.None);
            SetMessage(message, true);
            MockUI.Show(_stale, false);
        }

        /// <summary>設定が変わったときに呼ぶ。重さに合わせて更新のしかたを変える。</summary>
        public void Invalidate()
        {
            if (MakeImage == null) return;
            _debounce?.Pause();
            switch (UpdateCost)
            {
                case Cost.Light:
                    Compute();
                    break;
                case Cost.Medium:
                    _debounce = _root.schedule.Execute(Compute).StartingIn(300);
                    break;
                default:
                    // 重い: 自動では更新せず「古い」ことだけ知らせる
                    if (_hasImage) MockUI.Show(_stale, true);
                    else Compute();
                    break;
            }
        }

        /// <summary>計算中: 前の画像を残したまま上端に細い進捗バー。前の画像が無ければ「計算中…」。</summary>
        public void Compute()
        {
            if (MakeImage == null) return;
            StopComputing();
            MockUI.Show(_stale, false);
            MockUI.Show(_progress, true);
            _progress.style.width = Length.Percent(0);
            if (!_hasImage) SetMessage("計算中…", false);

            double start = EditorApplication.timeSinceStartup;
            int duration = UpdateCost == Cost.Heavy ? 1200 : 450;
            _computing = _root.schedule.Execute(() =>
            {
                float t = (float)((EditorApplication.timeSinceStartup - start) * 1000.0 / duration);
                _progress.style.width = Length.Percent(Mathf.Clamp01(t) * 100f);
                if (t < 1f) return;
                StopComputing();
                _image.style.backgroundImage = MakeImage();
                _hasImage = true;
                SetMessage(null, false);
                if (_info != null && MakeInfo != null) _info.text = MakeInfo();
            }).Every(30);
        }

        private void StopComputing()
        {
            _computing?.Pause();
            _computing = null;
            MockUI.Show(_progress, false);
        }

        private void SetMessage(string message, bool error)
        {
            if (_message == null) return;
            MockUI.Show(_message, !string.IsNullOrEmpty(message));
            _message.text = message ?? string.Empty;
            _message.EnableInClassList("dennoko-preview__message--error", error);
        }
    }
}
