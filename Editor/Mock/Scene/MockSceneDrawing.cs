using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Dennokoworks.UIDesignTest.Mock
{
    /// <summary>
    /// シーンの直接操作と S5 シーン描画のモック。
    /// 対象の正面に 8 × 6 マスの「作業面」(偽のメッシュ) を置き、
    ///   P4 (ツールバー型): マスのホバー / 選択 / 対称 / ブラシ塗り
    ///   P5 (パネル型)   : ブラシ円 / 影響範囲 / ミラー側 / 頂点の選択 / ホイールで半径
    /// を描く。色と線は DennokoSceneColors (S5) を使う。
    /// Alt + ドラッグ・中ボタン・右ボタンはカメラ操作なので消費しない。
    /// </summary>
    [InitializeOnLoad]
    internal static class MockSceneDrawing
    {
        public const int Cols = 8;
        public const int Rows = 6;
        public static int CellCount => Cols * Rows;

        private static bool _pointerDown;
        private static bool _paintRemove;

        static MockSceneDrawing()
        {
            SceneView.duringSceneGui += OnSceneGui;
        }

        // ─── 作業面の形 ───────────────────────────────────────────────

        private static float Cell => MockSceneSession.Scale * 0.8f / Cols;

        private static Vector3 Min
        {
            get
            {
                var c = MockSceneSession.Center;
                return new Vector3(c.x - Cell * Cols * 0.5f, c.y - Cell * Rows * 0.5f, MockSceneSession.FrontZ);
            }
        }

        private static Vector3 GridPoint(float x, float y) => Min + new Vector3(x * Cell, y * Cell, 0f);

        private static Vector3[] RectCorners(int x0, int y0, int x1, int y1) => new[]
        {
            GridPoint(x0, y0), GridPoint(x1, y0), GridPoint(x1, y1), GridPoint(x0, y1),
        };

        /// <summary>選択の単位ごとのまとまり (面 = 1 マス、パーツ = 2 × 2、島 = 4 × 3) の範囲。</summary>
        private static RectInt GroupRect(int cx, int cy, int unit)
        {
            switch (unit)
            {
                case 2: return new RectInt(cx, cy, 1, 1);
                case 1: return new RectInt(cx / 2 * 2, cy / 2 * 2, 2, 2);
                default: return new RectInt(cx < 4 ? 0 : 4, cy < 3 ? 0 : 3, 4, 3);
            }
        }

        private static RectInt Mirror(RectInt r) => new RectInt(Cols - r.xMax, r.y, r.width, r.height);

        private static IEnumerable<int> Cells(RectInt r)
        {
            for (int y = r.yMin; y < r.yMax; y++)
            for (int x = r.xMin; x < r.xMax; x++)
                yield return y * Cols + x;
        }

        // ─── 入力と描画 ───────────────────────────────────────────────

        private static void OnSceneGui(SceneView view)
        {
            if (!MockSceneSession.Active) return;
            var e = Event.current;
            int id = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(id);   // クリックで別のオブジェクトを選ばない

            // ラジアルが開いている間はクリック・Esc をラジアルに任せる (描画だけ続ける)
            if (MockRadialMenu.Menu.IsOpen && e.type != EventType.Repaint) return;

            // Esc: 選択があればまず解除、無ければ終了 (パネル型)。ツールバー型は選択が作業そのものなので終了だけ
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                if (MockSceneSession.Kind == MockSessionKind.Panel && MockSceneSession.Data.selectedVertex >= 0)
                    MockSceneSession.Modify("選択を解除", d => d.selectedVertex = -1);
                else
                    MockSceneSession.End(MockEndReason.Escape);
                e.Use();
                return;
            }

            if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag) view.Repaint();

            bool hasPoint = TryGetPoint(e.mousePosition, out var point, out int cx, out int cy);
            bool camera = e.alt || e.button != 0;   // カメラ操作は通す

            if (MockSceneSession.Kind == MockSessionKind.Toolbar)
            {
                MockSceneHud.SetModifier(view, e.mousePosition, e.shift, e.control || e.command);
                HandleToolbarInput(e, id, camera, hasPoint, point, cx, cy);
                if (e.type == EventType.Repaint) DrawToolbarKind(hasPoint, point, cx, cy);
            }
            else
            {
                MockSceneHud.SetModifier(view, e.mousePosition, false, false);
                HandlePanelInput(e, id, camera, hasPoint, point);
                if (e.type == EventType.Repaint)
                {
                    if (MockSceneSession.Variant == MockPanelVariant.Lattice) DrawLattice();
                    else DrawPanelKind(hasPoint, point);
                }
            }
        }

        private static bool TryGetPoint(Vector2 gui, out Vector3 point, out int cx, out int cy)
        {
            var plane = new Plane(Vector3.forward, Min);
            var ray = HandleUtility.GUIPointToWorldRay(gui);
            point = default;
            cx = cy = -1;
            if (!plane.Raycast(ray, out float d)) return false;
            point = ray.GetPoint(d);
            var local = point - Min;
            cx = Mathf.FloorToInt(local.x / Cell);
            cy = Mathf.FloorToInt(local.y / Cell);
            return cx >= 0 && cx < Cols && cy >= 0 && cy < Rows;
        }

        // ─── P4 ツールバー型 (Mask Maker 風) ──────────────────────────

        private static void HandleToolbarInput(Event e, int id, bool camera, bool hasPoint, Vector3 point, int cx, int cy)
        {
            if (camera) return;
            switch (e.type)
            {
                case EventType.MouseDown when hasPoint:
                    GUIUtility.hotControl = id;
                    _pointerDown = true;
                    MockSceneSession.ClickedSinceStart = true;
                    MockSceneHud.HideHint();
                    if (MockSceneSession.PaintMode)
                    {
                        _paintRemove = MockSceneSession.PaintTool == 3 || e.control;
                        Paint(point);
                    }
                    else
                    {
                        ToggleGroup(cx, cy, e.shift, e.control);
                    }
                    e.Use();
                    break;
                case EventType.MouseDrag when _pointerDown && hasPoint && MockSceneSession.PaintMode:
                    Paint(point);
                    e.Use();
                    break;
                case EventType.MouseUp when _pointerDown:
                    _pointerDown = false;
                    GUIUtility.hotControl = 0;
                    e.Use();
                    break;
            }
        }

        /// <summary>スマートトグル: まとまり全体が選択済みなら外し、そうでなければ足す。Shift = 足す / Ctrl = 外す。</summary>
        private static void ToggleGroup(int cx, int cy, bool forceAdd, bool forceRemove)
        {
            var rect = GroupRect(cx, cy, MockSceneSession.Unit);
            var data = MockSceneSession.Data;
            bool allSelected = true;
            foreach (int c in Cells(rect)) allSelected &= data.selectedCells.Contains(c);
            bool remove = forceRemove || (!forceAdd && allSelected);

            MockSceneSession.Modify(remove ? "選択から外す" : "選択に足す", d =>
            {
                Apply(d, rect, remove);
                if (MockSceneSession.Symmetry) Apply(d, Mirror(rect), remove);
            });
        }

        private static void Apply(MockSceneData d, RectInt rect, bool remove)
        {
            foreach (int c in Cells(rect))
            {
                if (remove) d.selectedCells.Remove(c);
                else if (!d.selectedCells.Contains(c)) d.selectedCells.Add(c);
            }
        }

        private static float BrushWorld => MockSceneSession.Scale * 0.06f * (MockSceneSession.BrushPx / 50f);

        private static void Paint(Vector3 point)
        {
            float r = BrushWorld;
            var hits = new List<int>();
            for (int y = 0; y < Rows; y++)
            for (int x = 0; x < Cols; x++)
            {
                var center = GridPoint(x + 0.5f, y + 0.5f);
                if ((center - point).magnitude <= r + Cell * 0.5f) hits.Add(y * Cols + x);
                if (MockSceneSession.Symmetry)
                {
                    var mirrored = new Vector3(2f * MockSceneSession.Center.x - point.x, point.y, point.z);
                    if ((center - mirrored).magnitude <= r + Cell * 0.5f) hits.Add(y * Cols + x);
                }
            }
            bool remove = _paintRemove;
            MockSceneSession.Modify(remove ? "消しゴム" : "ブラシ", d =>
            {
                foreach (int c in hits)
                {
                    if (remove) d.selectedCells.Remove(c);
                    else if (!d.selectedCells.Contains(c)) d.selectedCells.Add(c);
                }
            });
        }

        private static void DrawToolbarKind(bool hasPoint, Vector3 point, int cx, int cy)
        {
            var prevZ = Handles.zTest;
            Handles.zTest = CompareFunction.Always;
            var data = MockSceneSession.Data;

            // 作業面の格子 (補助線)
            for (int x = 0; x <= Cols; x++)
                DennokoSceneColors.Polyline(new[] { GridPoint(x, 0), GridPoint(x, Rows) }, DennokoSceneColors.Structure, DennokoSceneColors.Thin);
            for (int y = 0; y <= Rows; y++)
                DennokoSceneColors.Polyline(new[] { GridPoint(0, y), GridPoint(Cols, y) }, DennokoSceneColors.Structure, DennokoSceneColors.Thin);

            // 選択済み: selection 色の塗り + 縁
            foreach (int c in data.selectedCells)
            {
                int x = c % Cols, y = c / Cols;
                Handles.DrawSolidRectangleWithOutline(RectCorners(x, y, x + 1, y + 1),
                    DennokoSceneColors.SelectedFill, DennokoSceneColors.SelectedLine);
            }

            if (hasPoint && MockSceneSession.PaintMode)
            {
                // ブラシ円 (消しゴムは「外す」色)
                var color = MockSceneSession.PaintTool == 3 ? DennokoSceneColors.HoverRemove : DennokoSceneColors.BrushInner;
                DennokoSceneColors.Circle(point, Vector3.forward, BrushWorld, color);
                DennokoSceneColors.Circle(point, Vector3.forward, BrushWorld * 1.15f, DennokoSceneColors.BrushOuter, DennokoSceneColors.Thin);
                if (MockSceneSession.Symmetry)
                {
                    var mirrored = new Vector3(2f * MockSceneSession.Center.x - point.x, point.y, point.z);
                    DennokoSceneColors.Circle(mirrored, Vector3.forward, BrushWorld, color, DennokoSceneColors.Bold, true);
                }
            }
            else if (hasPoint)
            {
                // ホバー: 足す = 白縁、外す (全体が選択済み) = 赤の点線。対称が ON なら反対側も点線で予告
                var rect = GroupRect(cx, cy, MockSceneSession.Unit);
                bool allSelected = true;
                foreach (int c in Cells(rect)) allSelected &= data.selectedCells.Contains(c);
                bool remove = allSelected;
                var color = remove ? DennokoSceneColors.HoverRemove : DennokoSceneColors.HoverAdd;
                DennokoSceneColors.Quad(RectCorners(rect.xMin, rect.yMin, rect.xMax, rect.yMax), color, DennokoSceneColors.Bold, remove);
                if (MockSceneSession.Symmetry)
                {
                    var m = Mirror(rect);
                    if (m.x != rect.x)
                        DennokoSceneColors.Quad(RectCorners(m.xMin, m.yMin, m.xMax, m.yMax), color, DennokoSceneColors.Bold, true);
                }
            }

            // 対称の基準線
            if (MockSceneSession.Symmetry)
            {
                float x = Cols * 0.5f;
                DennokoSceneColors.Polyline(new[] { GridPoint(x, -0.3f), GridPoint(x, Rows + 0.3f) },
                    DennokoSceneColors.SelectedLine, DennokoSceneColors.Thin, true);
            }
            Handles.zTest = prevZ;
        }

        // ─── P5 パネル型 (Mesh Editor 風) ─────────────────────────────

        private static void HandlePanelInput(Event e, int id, bool camera, bool hasPoint, Vector3 point)
        {
            var data = MockSceneSession.Data;

            // ハンドル操作中 (押している間) のホイールで半径を変える
            if (e.type == EventType.ScrollWheel && _pointerDown)
            {
                float next = Mathf.Clamp(data.radius * (1f - e.delta.y * 0.03f), 0.01f, 0.3f);
                MockSceneSession.Modify("半径", d => d.radius = next);
                e.Use();
                return;
            }
            if (camera || MockSceneSession.Variant == MockPanelVariant.Lattice) return;

            switch (e.type)
            {
                case EventType.MouseDown when hasPoint:
                    GUIUtility.hotControl = id;
                    _pointerDown = true;
                    MockSceneSession.ClickedSinceStart = true;
                    MockSceneHud.HideHint();
                    int nearest = NearestVertex(point);
                    MockSceneSession.Modify("頂点を選択", d => d.selectedVertex = nearest);
                    e.Use();
                    break;
                case EventType.MouseUp when _pointerDown:
                    _pointerDown = false;
                    GUIUtility.hotControl = 0;
                    e.Use();
                    break;
            }
        }

        private static int NearestVertex(Vector3 point)
        {
            var local = point - Min;
            int x = Mathf.Clamp(Mathf.RoundToInt(local.x / Cell), 0, Cols);
            int y = Mathf.Clamp(Mathf.RoundToInt(local.y / Cell), 0, Rows);
            return y * (Cols + 1) + x;
        }

        private static Vector3 VertexPosition(int index) => GridPoint(index % (Cols + 1), index / (Cols + 1));

        private static Vector3 MirrorPoint(Vector3 p, int mirror)
        {
            var c = MockSceneSession.Center;
            switch (mirror)
            {
                case 1: return new Vector3(2f * c.x - p.x, p.y, p.z);
                case 2: return new Vector3(p.x, 2f * c.y - p.y, p.z);
                default: return p;   // Z は作業面が XY 平面なので同じ点 (モックの都合)
            }
        }

        private static float Falloff(float t, int type)
        {
            t = Mathf.Clamp01(t);
            switch (type)
            {
                case 0: return 1f - t * t * (3f - 2f * t);   // 滑らか
                case 1: return 1f - t;                        // 直線
                case 2: return (1f - t) * (1f - t);           // 鋭い
                default: return 1f;                           // 一定
            }
        }

        private static void DrawPanelKind(bool hasPoint, Vector3 point)
        {
            var prevZ = Handles.zTest;
            Handles.zTest = CompareFunction.Always;
            var data = MockSceneSession.Data;

            // 頂点 (補助)
            var prev = Handles.color;
            for (int i = 0; i < (Cols + 1) * (Rows + 1); i++)
            {
                var p = VertexPosition(i);
                Handles.color = DennokoSceneColors.Structure;
                Handles.DrawSolidDisc(p, Vector3.forward, HandleUtility.GetHandleSize(p) * 0.012f);
            }

            // 影響範囲: 選択した頂点があればそこ、無ければカーソル位置を中心にする
            bool hasCenter = data.selectedVertex >= 0 || hasPoint;
            var center = data.selectedVertex >= 0 ? VertexPosition(data.selectedVertex) : point;
            if (hasCenter)
            {
                DrawInfluence(center, data);
                DennokoSceneColors.Circle(center, Vector3.forward, data.radius, DennokoSceneColors.BrushInner);
                if (data.mirror == 1 || data.mirror == 2)
                {
                    var m = MirrorPoint(center, data.mirror);
                    DrawInfluence(m, data);
                    DennokoSceneColors.Circle(m, Vector3.forward, data.radius, DennokoSceneColors.BrushInner, DennokoSceneColors.Bold, true);
                }
            }

            if (data.selectedVertex >= 0)
            {
                var p = VertexPosition(data.selectedVertex);
                Handles.color = DennokoSceneColors.SelectedLine;
                Handles.DrawSolidDisc(p, Vector3.forward, HandleUtility.GetHandleSize(p) * 0.04f);
            }
            else if (hasPoint)
            {
                // クリックで選ばれる頂点を白縁で予告
                var p = VertexPosition(NearestVertex(point));
                DennokoSceneColors.Circle(p, Vector3.forward, HandleUtility.GetHandleSize(p) * 0.05f, DennokoSceneColors.HoverAdd);
            }

            // ミラーの基準線
            if (data.mirror == 1)
                DennokoSceneColors.Polyline(new[] { GridPoint(Cols * 0.5f, -0.3f), GridPoint(Cols * 0.5f, Rows + 0.3f) },
                    DennokoSceneColors.SelectedLine, DennokoSceneColors.Thin, true);
            else if (data.mirror == 2)
                DennokoSceneColors.Polyline(new[] { GridPoint(-0.3f, Rows * 0.5f), GridPoint(Cols + 0.3f, Rows * 0.5f) },
                    DennokoSceneColors.SelectedLine, DennokoSceneColors.Thin, true);

            Handles.color = prev;
            Handles.zTest = prevZ;
        }

        private static void DrawInfluence(Vector3 center, MockSceneData data)
        {
            for (int i = 0; i < (Cols + 1) * (Rows + 1); i++)
            {
                var p = VertexPosition(i);
                float dist = (p - center).magnitude;
                if (dist > data.radius) continue;
                float w = Falloff(dist / data.radius, data.falloff);
                Handles.color = DennokoSceneColors.Influence(w);
                Handles.DrawSolidDisc(p, Vector3.forward, HandleUtility.GetHandleSize(p) * (0.015f + 0.02f * w));
            }
        }

        // ─── DenLattice 風 ──────────────────────────────────────────

        private static void DrawLattice()
        {
            var prevZ = Handles.zTest;
            Handles.zTest = CompareFunction.Always;
            var data = MockSceneSession.Data;
            var c = MockSceneSession.Center;
            var size = new Vector3(0.6f, 0.8f, 0.4f) * MockSceneSession.Scale;
            var min = c - size * 0.5f;
            var res = new Vector3Int(Mathf.Max(2, data.lattice.x), Mathf.Max(2, data.lattice.y), Mathf.Max(2, data.lattice.z));

            Vector3 P(int i, int j, int k) => min + Vector3.Scale(size,
                new Vector3(i / (float)(res.x - 1), j / (float)(res.y - 1), k / (float)(res.z - 1)));

            var prev = Handles.color;
            for (int i = 0; i < res.x; i++)
            for (int j = 0; j < res.y; j++)
            for (int k = 0; k < res.z; k++)
            {
                var p = P(i, j, k);
                if (i + 1 < res.x) DennokoSceneColors.Polyline(new[] { p, P(i + 1, j, k) }, DennokoSceneColors.Structure, DennokoSceneColors.Thin);
                if (j + 1 < res.y) DennokoSceneColors.Polyline(new[] { p, P(i, j + 1, k) }, DennokoSceneColors.Structure, DennokoSceneColors.Thin);
                if (k + 1 < res.z) DennokoSceneColors.Polyline(new[] { p, P(i, j, k + 1) }, DennokoSceneColors.Structure, DennokoSceneColors.Thin);
                Handles.color = DennokoSceneColors.HoverAdd;
                Handles.DrawSolidDisc(p, SceneView.currentDrawingSceneView != null
                    ? -SceneView.currentDrawingSceneView.camera.transform.forward : Vector3.forward,
                    HandleUtility.GetHandleSize(p) * 0.02f);
            }

            // ミラー面 (X)
            if (data.latticeMirror != null && data.latticeMirror.Length > 0 && data.latticeMirror[0])
            {
                var a = new Vector3(c.x, min.y, min.z);
                var b = new Vector3(c.x, min.y + size.y, min.z);
                var d = new Vector3(c.x, min.y + size.y, min.z + size.z);
                var e = new Vector3(c.x, min.y, min.z + size.z);
                DennokoSceneColors.Quad(new[] { a, b, d, e }, DennokoSceneColors.SelectedLine, DennokoSceneColors.Thin, true);
            }

            // ボックスモード: 影響を受ける範囲の箱を selection 色で
            if (data.latticeMode == 1)
            {
                Handles.color = DennokoSceneColors.SelectedLine;
                Handles.DrawWireCube(c, size * 0.5f);
            }
            Handles.color = prev;
            Handles.zTest = prevZ;
        }
    }
}
