using System.Collections.Generic;
using UnityEngine;

namespace Dennokoworks.UIDesignTest.Mock
{
    /// <summary>
    /// モックのシーン作業で Undo に載せる値。実際のツールではコンポーネントや作業データにあたる。
    /// (Undo.RecordObject を使うために ScriptableObject にしている)
    /// </summary>
    internal sealed class MockSceneData : ScriptableObject
    {
        // P5 (Mesh Editor 風)
        public float radius = 0.08f;
        public int falloff;          // 0 滑らか / 1 直線 / 2 鋭い / 3 一定
        public int mirror = 1;       // 0 なし / 1 X / 2 Y / 3 Z
        public int selectedVertex = -1;

        // DenLattice 風
        public Vector3Int lattice = new Vector3Int(3, 3, 3);
        public bool[] latticeMirror = { true, false, false };
        public int latticeMode;      // 0 ケージ / 1 ボックス
        public int boxOp;            // 0 移動 / 1 回転 / 2 拡縮

        // P4 (Mask Maker 風): 選択中のセル
        public List<int> selectedCells = new List<int>();
    }
}
