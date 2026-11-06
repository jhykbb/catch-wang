using System.Collections;
using System.Collections.Generic;
using StockDice.Core;
using UnityEngine;
using UnityEngine.UI;

namespace StockDice.View
{
    /// <summary>
    /// 3D 보드. 칸, 이름표, 땅 주인 표시, 건물, 회사 타워를 만들고 이벤트에 맞춰 애니메이션한다.
    /// 칸 수는 BoardDefinition 에서 읽는다 (4로 나누어 떨어지는 정사각형 둘레).
    /// </summary>
    public sealed class BoardView : MonoBehaviour
    {
        public const float Spacing = 2.2f;
        public const float TileSize = 2.0f;
        public const float TileHeight = 0.36f;

        private BoardDefinition _board;
        private int _perSide;
        private Transform[] _tiles;
        private MeshRenderer[] _tileRenderers;
        private GameObject[] _flags;
        private GameObject[] _buildings;
        private int[] _shownOwner;
        private int[] _shownLevel;
        private GameObject[] _towers;       // 회사 id 별
        private MeshRenderer[] _towerBody;
        private bool[] _shownListed;
        private Text[] _subLabels;
        private Canvas _labelCanvas;
        private readonly List<Transform> _spinners = new List<Transform>();
        private readonly List<Transform> _bobbers = new List<Transform>();

        public BoardDefinition Definition { get { return _board; } }
        public FxManager Fx { get; set; }

        public static BoardView Create(BoardDefinition board, Transform parent = null)
        {
            var go = new GameObject("Board");
            if (parent != null) go.transform.SetParent(parent, false);
            var view = go.AddComponent<BoardView>();
            view.Build(board);
            return view;
        }

        // ------------------------------------------------------------------
        // 좌표
        // ------------------------------------------------------------------

        public int SideOf(int tile)
        {
            return Mathf.Clamp(tile / Mathf.Max(1, _perSide), 0, 3);
        }

        public Vector3 TilePosition(int tile)
        {
            int side = SideOf(tile);
            int k = tile % _perSide;
            float half = _perSide * Spacing * 0.5f;
            switch (side)
            {
                case 0: return new Vector3(half - k * Spacing, 0f, -half);
                case 1: return new Vector3(-half, 0f, -half + k * Spacing);
                case 2: return new Vector3(-half + k * Spacing, 0f, half);
                default: return new Vector3(half, 0f, half - k * Spacing);
            }
        }

        public Vector3 TileTop(int tile)
        {
            return TilePosition(tile) + Vector3.up * TileHeight;
        }

        /// <summary>보드 바깥쪽 방향 (카메라가 있는 쪽).</summary>
        public Vector3 Outward(int tile)
        {
            switch (SideOf(tile))
            {
                case 0: return Vector3.back;
                case 1: return Vector3.left;
                case 2: return Vector3.forward;
                default: return Vector3.right;
            }
        }

        public Quaternion SideRotation(int tile)
        {
            return Quaternion.Euler(0f, SideOf(tile) * 90f, 0f);
        }

        /// <summary>한 칸에 여러 토큰이 있을 때 자리.</summary>
        public Vector3 TokenSlot(int tile, int slot, int count)
        {
            Vector3 center = TileTop(tile) - Outward(tile) * 0.25f;
            if (count <= 1) return center;
            int cols = count <= 4 ? 2 : 3;
            int rows = Mathf.CeilToInt(count / (float)cols);
            int r = slot / cols, c = slot % cols;
            float gap = 0.58f;
            Vector3 right = SideRotation(tile) * Vector3.right;
            Vector3 fwd = SideRotation(tile) * Vector3.forward;
            float x = (c - (cols - 1) * 0.5f) * gap;
            float z = ((rows - 1) * 0.5f - r) * gap;
            return center + right * x + fwd * z;
        }

        // ------------------------------------------------------------------
        // 생성
        // ------------------------------------------------------------------

        private void Build(BoardDefinition board)
        {
            _board = board;
            int n = board.TileCount;
            _perSide = Mathf.Max(1, n / 4);
            _tiles = new Transform[n];
            _tileRenderers = new MeshRenderer[n];
            _flags = new GameObject[n];
            _buildings = new GameObject[n];
            _shownOwner = new int[n];
            _shownLevel = new int[n];
            _subLabels = new Text[n];
            for (int i = 0; i < n; i++) _shownOwner[i] = -1;
            _towers = new GameObject[board.Companies.Count];
            _towerBody = new MeshRenderer[board.Companies.Count];
            _shownListed = new bool[board.Companies.Count];

            float extent = _perSide * Spacing;
            // 테이블과 보드 바닥
            var table = MeshFactory.Spawn("Table", MeshFactory.RoundedBox(new Vector3(extent + 30f, 1f, extent + 30f), 0.5f, 2),
                MaterialLibrary.Get(Palette.Table, 0.1f), transform, new Vector3(0f, -0.75f, 0f), false);
            table.GetComponent<MeshRenderer>().receiveShadows = true;
            MeshFactory.Spawn("BoardBase", MeshFactory.RoundedBox(new Vector3(extent + 2.6f, 0.4f, extent + 2.6f), 0.5f, 3),
                MaterialLibrary.Get(Palette.BoardBase, 0.25f), transform, new Vector3(0f, -0.2f, 0f));
            MeshFactory.Spawn("Center", MeshFactory.RoundedBox(new Vector3(extent - Spacing - 0.4f, 0.16f, extent - Spacing - 0.4f), 0.4f, 3),
                MaterialLibrary.Get(Palette.BoardCenter, 0.2f), transform, new Vector3(0f, 0.02f, 0f), false);

            BuildLabelCanvas(extent);

            for (int i = 0; i < n; i++) BuildTile(board.Tiles[i]);
            BuildCenterTitle();
        }

        private void BuildLabelCanvas(float extent)
        {
            var go = new GameObject("TileLabels", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            _labelCanvas = go.AddComponent<Canvas>();
            _labelCanvas.renderMode = RenderMode.WorldSpace;
            _labelCanvas.sortingOrder = 1;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 4f;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(extent * 100f, extent * 100f);
            rt.localScale = Vector3.one * 0.01f;
            rt.localPosition = Vector3.zero;
        }

        private Text AddTileText(int tile, string text, int size, Color color, float outwardOffset, FontStyle style = FontStyle.Bold, float height = 0.02f)
        {
            var label = UIKit.Label(_labelCanvas.transform, text, size, color, TextAnchor.MiddleCenter, style);
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            var rt = label.rectTransform;
            rt.sizeDelta = new Vector2(TileSize * 100f, size * 1.4f);
            Vector3 world = TileTop(tile) + Outward(tile) * outwardOffset + Vector3.up * height;
            rt.position = transform.TransformPoint(world);
            rt.rotation = transform.rotation * Quaternion.Euler(90f, SideOf(tile) * 90f, 0f);
            return label;
        }

        private void BuildTile(TileDef def)
        {
            int i = def.Index;
            var tileGo = new GameObject("Tile_" + i + "_" + def.Name);
            tileGo.transform.SetParent(transform, false);
            tileGo.transform.localPosition = TilePosition(i);
            tileGo.transform.localRotation = SideRotation(i);
            _tiles[i] = tileGo.transform;

            Color top = TileColor(def);
            var baseGo = MeshFactory.Spawn("Base", MeshFactory.RoundedBox(new Vector3(TileSize, TileHeight, TileSize), 0.16f, 3),
                MaterialLibrary.Get(top, 0.3f), tileGo.transform, new Vector3(0f, TileHeight * 0.5f, 0f));
            _tileRenderers[i] = baseGo.GetComponent<MeshRenderer>();

            // 이 칸의 로컬 좌표: +z = 보드 안쪽, -z = 바깥(카메라 쪽)
            switch (def.Type)
            {
                case TileType.Land:
                {
                    var band = MeshFactory.Spawn("Band", MeshFactory.RoundedBox(new Vector3(TileSize - 0.16f, 0.08f, 0.5f), 0.04f, 2),
                        MaterialLibrary.Get(Palette.Side(def.Side), 0.3f), tileGo.transform, new Vector3(0f, TileHeight + 0.02f, TileSize * 0.5f - 0.33f), false);
                    band.name = "Band";
                    AddTileText(i, def.Name, 30, Palette.Ink, 0.5f);
                    _subLabels[i] = AddTileText(i, Fmt.Won(def.LandPrice), 24, Palette.InkSoft, 0.12f, FontStyle.Normal);
                    break;
                }
                case TileType.Company:
                {
                    var cdef = _board.Companies[def.CompanyId];
                    var tower = new GameObject("Tower");
                    tower.transform.SetParent(tileGo.transform, false);
                    tower.transform.localPosition = new Vector3(0f, TileHeight, 0.35f);
                    var body = MeshFactory.Spawn("Body", MeshFactory.RoundedBox(new Vector3(0.72f, 1.3f, 0.72f), 0.12f, 2),
                        MaterialLibrary.Get(new Color(0.8f, 0.82f, 0.85f), 0.4f), tower.transform, new Vector3(0f, 0.65f, 0f));
                    MeshFactory.Spawn("Cap", MeshFactory.RoundedBox(new Vector3(0.82f, 0.12f, 0.82f), 0.05f, 2),
                        MaterialLibrary.Get(Color.white, 0.5f), tower.transform, new Vector3(0f, 1.32f, 0f));
                    tower.transform.localScale = new Vector3(1f, 0.35f, 1f);
                    _towers[cdef.Id] = tower;
                    _towerBody[cdef.Id] = body.GetComponent<MeshRenderer>();
                    AddTileText(i, cdef.Name, 28, Palette.Ink, 0.55f);
                    _subLabels[i] = AddTileText(i, BoardDefinition.SectorName(cdef.Sector) + " · 비상장", 22, Palette.InkSoft, 0.2f, FontStyle.Normal);
                    break;
                }
                case TileType.Item:
                {
                    var box = new GameObject("GiftBox");
                    box.transform.SetParent(tileGo.transform, false);
                    box.transform.localPosition = new Vector3(0f, TileHeight + 0.42f, 0.3f);
                    MeshFactory.Spawn("Box", MeshFactory.RoundedBox(new Vector3(0.7f, 0.6f, 0.7f), 0.1f, 2),
                        MaterialLibrary.Get(Palette.Hex("#FF8FB1"), 0.4f), box.transform, Vector3.zero);
                    MeshFactory.Spawn("RibbonA", MeshFactory.RoundedBox(new Vector3(0.74f, 0.64f, 0.16f), 0.04f, 1),
                        MaterialLibrary.Get(Palette.Gold, 0.5f), box.transform, Vector3.zero);
                    MeshFactory.Spawn("RibbonB", MeshFactory.RoundedBox(new Vector3(0.16f, 0.64f, 0.74f), 0.04f, 1),
                        MaterialLibrary.Get(Palette.Gold, 0.5f), box.transform, Vector3.zero);
                    _bobbers.Add(box.transform);
                    AddTileText(i, "행운 상자", 28, Palette.Hex("#D6336C"), 0.55f);
                    break;
                }
                case TileType.Start:
                {
                    var arrow = MeshFactory.Spawn("Arrow", MeshFactory.Roof(0.9f, 0.18f, 0.6f), MaterialLibrary.Get(Palette.Up, 0.4f),
                        tileGo.transform, new Vector3(0.1f, TileHeight + 0.05f, 0.35f));
                    arrow.transform.localRotation = Quaternion.Euler(90f, 0f, 90f);
                    AddTileText(i, "출발", 40, Palette.Ink, 0.45f);
                    AddTileText(i, "지나가면 월급", 22, Palette.InkSoft, 0.05f, FontStyle.Normal);
                    break;
                }
                case TileType.Vacation:
                {
                    MeshFactory.Spawn("Pole", MeshFactory.Cylinder(0.05f, 0.05f, 1.1f, 10), MaterialLibrary.Get(Palette.Hex("#ADB5BD"), 0.4f),
                        tileGo.transform, new Vector3(0f, TileHeight, 0.35f));
                    var canopy = MeshFactory.Spawn("Canopy", MeshFactory.Cylinder(0.75f, 0.02f, 0.4f, 12), MaterialLibrary.Get(Palette.Hex("#FF8787"), 0.4f),
                        tileGo.transform, new Vector3(0f, TileHeight + 1.0f, 0.35f));
                    _spinners.Add(canopy.transform);
                    AddTileText(i, "휴가", 40, Palette.Ink, 0.45f);
                    AddTileText(i, "한 턴 쉬기", 22, Palette.InkSoft, 0.05f, FontStyle.Normal);
                    break;
                }
                case TileType.TradeHalt:
                {
                    var red = MaterialLibrary.Get(Palette.Up, 0.4f);
                    var white = MaterialLibrary.Get(Color.white, 0.4f);
                    MeshFactory.Spawn("PostL", MeshFactory.Cylinder(0.07f, 0.07f, 0.7f, 10), white, tileGo.transform, new Vector3(-0.6f, TileHeight, 0.35f));
                    MeshFactory.Spawn("PostR", MeshFactory.Cylinder(0.07f, 0.07f, 0.7f, 10), white, tileGo.transform, new Vector3(0.6f, TileHeight, 0.35f));
                    for (int s = 0; s < 4; s++)
                    {
                        MeshFactory.Spawn("Bar" + s, MeshFactory.RoundedBox(new Vector3(0.32f, 0.16f, 0.1f), 0.03f, 1), s % 2 == 0 ? red : white,
                            tileGo.transform, new Vector3(-0.48f + s * 0.32f, TileHeight + 0.62f, 0.35f));
                    }
                    AddTileText(i, "거래정지", 36, Palette.Up, 0.45f);
                    AddTileText(i, "2턴 매매 불가", 22, Palette.InkSoft, 0.05f, FontStyle.Normal);
                    break;
                }
                case TileType.CoinExchange:
                {
                    var coin = MeshFactory.Spawn("BigCoin", MeshFactory.Cylinder(0.5f, 0.5f, 0.14f, 28), MaterialLibrary.Get(Palette.Gold, 0.75f, 0.15f),
                        tileGo.transform, new Vector3(0f, TileHeight + 0.75f, 0.35f));
                    coin.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    var pivot = new GameObject("CoinPivot").transform;
                    pivot.SetParent(tileGo.transform, false);
                    pivot.localPosition = coin.transform.localPosition;
                    coin.transform.SetParent(pivot, true);
                    _spinners.Add(pivot);
                    AddTileText(i, "코인 거래소", 32, Palette.Hex("#7048E8"), 0.45f);
                    AddTileText(i, "2배 또는 반토막", 22, Palette.InkSoft, 0.05f, FontStyle.Normal);
                    break;
                }
            }
        }

        private void BuildCenterTitle()
        {
            var title = UIKit.Label(_labelCanvas.transform, "떡상 다이스", 150, Palette.WithAlpha(Palette.Ink, 0.85f), TextAnchor.MiddleCenter, FontStyle.Bold);
            title.horizontalOverflow = HorizontalWrapMode.Overflow;
            title.rectTransform.sizeDelta = new Vector2(1000f, 220f);
            title.rectTransform.position = transform.TransformPoint(new Vector3(0f, 0.12f, 0.6f));
            title.rectTransform.rotation = transform.rotation * Quaternion.Euler(90f, 0f, 0f);
            var sub = UIKit.Label(_labelCanvas.transform, "주사위 · 땅 · 주식  (가제)", 50, Palette.WithAlpha(Palette.InkSoft, 0.9f), TextAnchor.MiddleCenter, FontStyle.Normal);
            sub.horizontalOverflow = HorizontalWrapMode.Overflow;
            sub.rectTransform.sizeDelta = new Vector2(1000f, 80f);
            sub.rectTransform.position = transform.TransformPoint(new Vector3(0f, 0.12f, -0.8f));
            sub.rectTransform.rotation = transform.rotation * Quaternion.Euler(90f, 0f, 0f);
        }

        private static Color TileColor(TileDef def)
        {
            switch (def.Type)
            {
                case TileType.Start: return Palette.Hex("#FFE8A3");
                case TileType.Vacation: return Palette.Hex("#C3FAE8");
                case TileType.TradeHalt: return Palette.Hex("#FFE3E3");
                case TileType.CoinExchange: return Palette.Hex("#E5DBFF");
                case TileType.Item: return Palette.Hex("#FFF0F6");
                default: return Palette.TileTop;
            }
        }

        private void Update()
        {
            float t = Time.time;
            for (int i = 0; i < _spinners.Count; i++)
                if (_spinners[i] != null) _spinners[i].Rotate(0f, 40f * Time.deltaTime, 0f, Space.Self);
            for (int i = 0; i < _bobbers.Count; i++)
            {
                var b = _bobbers[i];
                if (b == null) continue;
                var p = b.localPosition;
                p.y = TileHeight + 0.42f + Mathf.Sin(t * 2.2f + i) * 0.06f;
                b.localPosition = p;
                b.localRotation = Quaternion.Euler(0f, Mathf.Sin(t * 0.8f + i) * 12f, 0f);
            }
        }

        // ------------------------------------------------------------------
        // 땅
        // ------------------------------------------------------------------

        private GameObject MakeFlag(int tile, Color color)
        {
            var flag = new GameObject("Flag");
            flag.transform.SetParent(_tiles[tile], false);
            flag.transform.localPosition = new Vector3(-0.72f, TileHeight, 0.62f);
            MeshFactory.Spawn("Pole", MeshFactory.Cylinder(0.035f, 0.035f, 0.9f, 8), MaterialLibrary.Get(Color.white, 0.4f), flag.transform, Vector3.zero);
            MeshFactory.Spawn("Cloth", MeshFactory.RoundedBox(new Vector3(0.42f, 0.28f, 0.05f), 0.03f, 1), MaterialLibrary.Get(color, 0.4f),
                flag.transform, new Vector3(0.22f, 0.74f, 0f));
            return flag;
        }

        private GameObject MakeBuilding(int tile, int level, Color owner)
        {
            var root = new GameObject("Building_L" + level);
            root.transform.SetParent(_tiles[tile], false);
            root.transform.localPosition = new Vector3(0.18f, TileHeight, 0.38f);
            var bodyMat = MaterialLibrary.Get(Palette.Lighten(owner, 0.55f), 0.35f);
            var roofMat = MaterialLibrary.Get(owner, 0.4f);
            var windowMat = MaterialLibrary.Get(Palette.Hex("#FFF3BF"), 0.6f, 0.4f);
            float h;
            switch (level)
            {
                case 1:
                    h = 0.55f;
                    MeshFactory.Spawn("Body", MeshFactory.RoundedBox(new Vector3(0.8f, h, 0.7f), 0.08f, 2), bodyMat, root.transform, new Vector3(0f, h * 0.5f, 0f));
                    MeshFactory.Spawn("Roof", MeshFactory.Roof(0.95f, 0.8f, 0.4f), roofMat, root.transform, new Vector3(0f, h, 0f));
                    break;
                case 2:
                    h = 1.05f;
                    MeshFactory.Spawn("Body", MeshFactory.RoundedBox(new Vector3(0.85f, h, 0.75f), 0.1f, 2), bodyMat, root.transform, new Vector3(0f, h * 0.5f, 0f));
                    MeshFactory.Spawn("Roof", MeshFactory.Roof(1.0f, 0.85f, 0.42f), roofMat, root.transform, new Vector3(0f, h, 0f));
                    break;
                default:
                    h = 1.75f;
                    MeshFactory.Spawn("Body", MeshFactory.RoundedBox(new Vector3(0.85f, h, 0.85f), 0.12f, 2), bodyMat, root.transform, new Vector3(0f, h * 0.5f, 0f));
                    MeshFactory.Spawn("Top", MeshFactory.RoundedBox(new Vector3(0.95f, 0.16f, 0.95f), 0.05f, 2), roofMat, root.transform, new Vector3(0f, h + 0.08f, 0f));
                    MeshFactory.Spawn("Antenna", MeshFactory.Cylinder(0.03f, 0.01f, 0.45f, 6), roofMat, root.transform, new Vector3(0f, h + 0.16f, 0f));
                    break;
            }
            // 창문 줄
            int floors = level == 1 ? 1 : level == 2 ? 2 : 4;
            for (int f = 0; f < floors; f++)
            {
                float y = 0.22f + f * 0.36f;
                MeshFactory.Spawn("Window" + f, MeshFactory.RoundedBox(new Vector3(0.5f, 0.14f, 0.04f), 0.02f, 1), windowMat, root.transform,
                    new Vector3(0f, y, -0.36f - (level == 3 ? 0.06f : 0f)), false);
            }
            return root;
        }

        public IEnumerator AnimateLandBought(int tile, int owner, float speed)
        {
            SetOwnerImmediate(tile, -1);
            Color c = Palette.Player(owner);
            _tileRenderers[tile].sharedMaterial = MaterialLibrary.Get(Palette.Lighten(c, 0.72f), 0.3f);
            var flag = MakeFlag(tile, c);
            _flags[tile] = flag;
            _shownOwner[tile] = owner;
            flag.transform.localScale = Vector3.zero;
            if (Fx != null) Fx.Confetti(TileTop(tile) + Vector3.up * 0.5f, 18, new[] { c, Color.white, Palette.Gold }, 3.5f);
            yield return Tween.Scale(flag.transform, Vector3.zero, Vector3.one, 0.45f / speed, Ease.OutBack);
            if (_tiles[tile] != null) yield return Tween.Punch(_tiles[tile], 0.08f, 0.25f / speed);
        }

        public IEnumerator AnimateBuild(int tile, int level, int owner, float speed)
        {
            if (_buildings[tile] != null)
            {
                var old = _buildings[tile];
                yield return Tween.Scale(old.transform, Vector3.one, new Vector3(1.1f, 0.01f, 1.1f), 0.15f / speed, Ease.InCubic);
                Destroy(old);
            }
            var b = MakeBuilding(tile, level, Palette.Player(owner));
            _buildings[tile] = b;
            _shownLevel[tile] = level;
            b.transform.localScale = new Vector3(1f, 0.01f, 1f);
            if (Fx != null) Fx.DustRing(TileTop(tile), Palette.Lighten(Palette.Player(owner), 0.5f));
            yield return Tween.Scale(b.transform, new Vector3(1f, 0.01f, 1f), Vector3.one, 0.55f / speed, Ease.OutBack);
        }

        public IEnumerator AnimateLandSold(int tile, float speed)
        {
            var flag = _flags[tile];
            var b = _buildings[tile];
            yield return Tween.Run(0.35f / speed, k =>
            {
                if (flag != null) flag.transform.localScale = Vector3.one * (1f - k);
                if (b != null) b.transform.localScale = new Vector3(1f, 1f - k, 1f);
            }, Ease.InCubic);
            SetOwnerImmediate(tile, -1);
        }

        private void SetOwnerImmediate(int tile, int owner)
        {
            if (_flags[tile] != null) Destroy(_flags[tile]);
            if (_buildings[tile] != null) Destroy(_buildings[tile]);
            _flags[tile] = null;
            _buildings[tile] = null;
            _shownLevel[tile] = 0;
            _shownOwner[tile] = owner;
            var def = _board.Tiles[tile];
            if (owner >= 0)
            {
                Color c = Palette.Player(owner);
                _tileRenderers[tile].sharedMaterial = MaterialLibrary.Get(Palette.Lighten(c, 0.72f), 0.3f);
                _flags[tile] = MakeFlag(tile, c);
            }
            else
            {
                _tileRenderers[tile].sharedMaterial = MaterialLibrary.Get(TileColor(def), 0.3f);
            }
        }

        // ------------------------------------------------------------------
        // 회사
        // ------------------------------------------------------------------

        public IEnumerator AnimateListing(int companyId, long price, bool big, float speed)
        {
            var tower = _towers[companyId];
            if (tower == null) yield break;
            var def = _board.Companies[companyId];
            _shownListed[companyId] = true;
            _towerBody[companyId].sharedMaterial = MaterialLibrary.Get(Palette.SectorColor(def.Sector), 0.45f, 0.1f);
            SetCompanySub(companyId, price, 0);
            Vector3 top = TileTop(def.TileIndex) + Vector3.up * 1.4f;
            if (Fx != null)
            {
                Fx.Confetti(top, big ? 46 : 16, new[] { Palette.SectorColor(def.Sector), Palette.Gold, Color.white, Palette.Up }, big ? 6f : 3.5f);
                if (big) Fx.Sparkle(top, Palette.Gold);
            }
            yield return Tween.Run((big ? 0.7f : 0.35f) / speed, k =>
            {
                if (tower != null) tower.transform.localScale = new Vector3(1f, Mathf.LerpUnclamped(0.35f, 1f, k), 1f);
            }, Ease.OutElastic);
        }

        public IEnumerator AnimateDelisting(int companyId, float speed)
        {
            var tower = _towers[companyId];
            if (tower == null) yield break;
            _shownListed[companyId] = false;
            var start = tower.transform.localPosition;
            yield return Tween.Run(0.6f / speed, k =>
            {
                if (tower == null) return;
                tower.transform.localPosition = start + new Vector3(Mathf.Sin(k * 60f) * 0.05f * (1f - k), 0f, 0f);
                tower.transform.localScale = new Vector3(1f, Mathf.Lerp(1f, 0.35f, k), 1f);
            }, Ease.InCubic);
            tower.transform.localPosition = start;
            _towerBody[companyId].sharedMaterial = MaterialLibrary.Get(new Color(0.8f, 0.82f, 0.85f), 0.4f);
            var def = _board.Companies[companyId];
            if (_subLabels[def.TileIndex] != null) _subLabels[def.TileIndex].text = BoardDefinition.SectorName(def.Sector) + " · 비상장";
        }

        public void SetCompanySub(int companyId, long price, double change)
        {
            var def = _board.Companies[companyId];
            var label = _subLabels[def.TileIndex];
            if (label == null) return;
            string arrow = Palette.ChangeArrow(change);
            label.text = Fmt.Won(price) + " " + UIKit.Colored(arrow, Palette.Change(change));
        }

        /// <summary>장 마감 때 회사 타워 위로 화살표가 솟는다.</summary>
        public void PulseCompany(int companyId, double rate, bool limit)
        {
            var tower = _towers[companyId];
            if (tower == null || Fx == null) return;
            var def = _board.Companies[companyId];
            Vector3 pos = TileTop(def.TileIndex) + Vector3.up * 1.7f - Outward(def.TileIndex) * 0.35f;
            Fx.Arrow(pos, rate >= 0, limit);
            StartCoroutine(Tween.Punch(tower.transform, limit ? 0.25f : 0.12f, 0.35f));
        }

        public Vector3 CompanyTop(int companyId)
        {
            var def = _board.Companies[companyId];
            return TileTop(def.TileIndex) + Vector3.up * 1.5f;
        }

        public IEnumerator PunchTile(int tile, float amount, float duration)
        {
            if (_tiles[tile] == null) yield break;
            yield return Tween.Punch(_tiles[tile], amount, duration);
        }

        // ------------------------------------------------------------------
        // 상태 동기화 (애니메이션 없이 최종 상태 맞추기)
        // ------------------------------------------------------------------

        public void SyncFromState(GameState s)
        {
            for (int i = 0; i < s.Lands.Length; i++)
            {
                var land = s.Lands[i];
                if (land == null) continue;
                if (_shownOwner[i] != land.Owner) SetOwnerImmediate(i, land.Owner);
                if (land.Owner >= 0 && _shownLevel[i] != land.Level)
                {
                    if (_buildings[i] != null) Destroy(_buildings[i]);
                    _buildings[i] = land.Level > 0 ? MakeBuilding(i, land.Level, Palette.Player(land.Owner)) : null;
                    _shownLevel[i] = land.Level;
                }
            }
            for (int c = 0; c < s.Companies.Length; c++)
            {
                var company = s.Companies[c];
                if (_shownListed[c] != company.Listed)
                {
                    _shownListed[c] = company.Listed;
                    var def = _board.Companies[c];
                    _towerBody[c].sharedMaterial = company.Listed
                        ? MaterialLibrary.Get(Palette.SectorColor(def.Sector), 0.45f, 0.1f)
                        : MaterialLibrary.Get(new Color(0.8f, 0.82f, 0.85f), 0.4f);
                    _towers[c].transform.localScale = new Vector3(1f, company.Listed ? 1f : 0.35f, 1f);
                }
                if (company.Listed) SetCompanySub(c, company.Price, company.LastChangeRate);
                else
                {
                    var def = _board.Companies[c];
                    if (_subLabels[def.TileIndex] != null) _subLabels[def.TileIndex].text = BoardDefinition.SectorName(def.Sector) + " · 비상장";
                }
            }
        }
    }
}
