using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StockDice.View
{
    /// <summary>플레이어 말. 칸마다 통통 튀며 이동하고 착지할 때 찌그러진다.</summary>
    public sealed class TokenView : MonoBehaviour
    {
        public int PlayerId;
        public Vector3 Rest;
        public Quaternion RestRotation = Quaternion.identity;
        public bool Busy;

        private Transform _body;
        private Transform _ring;
        private Material _ringMat;
        private Color _color;
        private bool _highlight;

        public static TokenView Create(int playerId, Color color, Transform parent)
        {
            var go = new GameObject("Token_P" + (playerId + 1));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TokenView>();
            t.PlayerId = playerId;
            t._color = color;

            var body = new GameObject("Body").transform;
            body.SetParent(go.transform, false);
            t._body = body;
            var main = MaterialLibrary.Get(color, 0.55f);
            var dark = MaterialLibrary.Get(Palette.Darken(color, 0.25f), 0.4f);
            MeshFactory.Spawn("Base", MeshFactory.Cylinder(0.34f, 0.3f, 0.1f, 24), dark, body, Vector3.zero);
            MeshFactory.Spawn("Torso", MeshFactory.Cylinder(0.27f, 0.13f, 0.52f, 24), main, body, new Vector3(0f, 0.08f, 0f));
            MeshFactory.Spawn("Head", MeshFactory.Sphere(0.23f), main, body, new Vector3(0f, 0.76f, 0f));
            var eye = MaterialLibrary.Get(Palette.Ink, 0.8f);
            MeshFactory.Spawn("EyeL", MeshFactory.Sphere(0.04f, 10, 8), eye, body, new Vector3(-0.08f, 0.8f, -0.2f), false);
            MeshFactory.Spawn("EyeR", MeshFactory.Sphere(0.04f, 10, 8), eye, body, new Vector3(0.08f, 0.8f, -0.2f), false);
            var shine = MaterialLibrary.Get(Color.white, 0.9f);
            MeshFactory.Spawn("Shine", MeshFactory.Sphere(0.06f, 10, 8), shine, body, new Vector3(0.09f, 0.88f, -0.13f), false);

            t._ringMat = MaterialLibrary.Create(color, 0.6f, 0.8f);
            var ring = MeshFactory.Spawn("Ring", MeshFactory.Cylinder(0.5f, 0.5f, 0.03f, 32), t._ringMat, go.transform, new Vector3(0f, 0.005f, 0f), false);
            t._ring = ring.transform;
            ring.SetActive(false);
            return t;
        }

        public void SetHighlight(bool on)
        {
            _highlight = on;
            if (_ring != null) _ring.gameObject.SetActive(on);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (!Busy)
            {
                transform.position = Vector3.Lerp(transform.position, Rest, 1f - Mathf.Exp(-dt * 10f));
            }
            transform.rotation = Quaternion.Slerp(transform.rotation, RestRotation, 1f - Mathf.Exp(-dt * 8f));
            if (_highlight && _ring != null)
            {
                float s = 1f + Mathf.Sin(Time.time * 5f) * 0.08f;
                _ring.localScale = new Vector3(s, 1f, s);
                if (_ringMat != null) MaterialLibrary.SetEmission(_ringMat, _color, 0.6f + Mathf.Sin(Time.time * 5f) * 0.4f);
            }
        }

        /// <summary>한 칸 점프.</summary>
        public IEnumerator Hop(Vector3 from, Vector3 to, float duration, float height)
        {
            Busy = true;
            yield return Tween.Run(duration, k =>
            {
                Vector3 p = Vector3.Lerp(from, to, k);
                p.y += Ease.Arc(k) * height;
                transform.position = p;
                float stretch = 1f + Mathf.Sin(k * Mathf.PI) * 0.12f;
                _body.localScale = new Vector3(1f / Mathf.Sqrt(stretch), stretch, 1f / Mathf.Sqrt(stretch));
            });
            transform.position = to;
            Rest = to;
            yield return Squash(duration * 0.6f, 0.22f);
            Busy = false;
        }

        public IEnumerator Squash(float duration, float amount)
        {
            yield return Tween.Run(duration, k =>
            {
                float s = 1f - amount * Mathf.Sin(k * Mathf.PI) * (1f - k * 0.3f);
                _body.localScale = new Vector3(1f / Mathf.Sqrt(Mathf.Max(0.3f, s)), s, 1f / Mathf.Sqrt(Mathf.Max(0.3f, s)));
            });
            _body.localScale = Vector3.one;
        }

        public IEnumerator Spin(float duration, float turns = 1f)
        {
            yield return Tween.Run(duration, k =>
            {
                _body.localRotation = Quaternion.Euler(0f, 360f * turns * k, 0f);
            }, Ease.OutCubic);
            _body.localRotation = Quaternion.identity;
        }

        public IEnumerator Shake(float duration, float amount)
        {
            Vector3 basePos = _body.localPosition;
            yield return Tween.Run(duration, k =>
            {
                _body.localPosition = basePos + new Vector3(Mathf.Sin(k * 70f) * amount * (1f - k), 0f, 0f);
            });
            _body.localPosition = basePos;
        }

        public Vector3 HeadPosition { get { return transform.position + Vector3.up * 1.1f; } }
    }

    /// <summary>모든 말의 배치와 이동을 관리한다.</summary>
    public sealed class TokenManager
    {
        private readonly BoardView _board;
        private readonly TokenView[] _tokens;
        private readonly int[] _positions;

        public TokenManager(BoardView board, int count, Transform parent)
        {
            _board = board;
            _tokens = new TokenView[count];
            _positions = new int[count];
            for (int i = 0; i < count; i++)
            {
                _tokens[i] = TokenView.Create(i, Palette.Player(i), parent);
                _positions[i] = 0;
            }
        }

        public TokenView Token(int player)
        {
            return _tokens[player];
        }

        public int Count { get { return _tokens.Length; } }

        public Vector3 Position(int player)
        {
            return _tokens[player].transform.position;
        }

        public void PlaceAll(IList<int> positions, bool snap)
        {
            for (int i = 0; i < _tokens.Length; i++) _positions[i] = positions[i];
            var tiles = new HashSet<int>(_positions);
            foreach (int tile in tiles) Relayout(tile, snap);
        }

        public void Highlight(int player)
        {
            for (int i = 0; i < _tokens.Length; i++) _tokens[i].SetHighlight(i == player);
        }

        private void Relayout(int tile, bool snap)
        {
            var here = new List<int>();
            for (int i = 0; i < _positions.Length; i++) if (_positions[i] == tile) here.Add(i);
            for (int k = 0; k < here.Count; k++)
            {
                var t = _tokens[here[k]];
                t.Rest = _board.TokenSlot(tile, k, here.Count);
                t.RestRotation = _board.SideRotation(tile);
                if (snap && !t.Busy)
                {
                    t.transform.position = t.Rest;
                    t.transform.rotation = t.RestRotation;
                }
            }
        }

        /// <summary>경로를 따라 한 칸씩 통통 튀며 이동.</summary>
        public IEnumerator MoveAlong(int player, int[] path, float stepTime, Action<int, Vector3> onStep)
        {
            var token = _tokens[player];
            int from = _positions[player];
            _positions[player] = -1;
            Relayout(from, false);
            for (int i = 0; i < path.Length; i++)
            {
                int tile = path[i];
                bool last = i == path.Length - 1;
                Vector3 start = token.transform.position;
                Vector3 end = last ? _board.TokenSlot(tile, 0, 1) : _board.TileTop(tile) - _board.Outward(tile) * 0.25f;
                token.RestRotation = _board.SideRotation(tile);
                if (onStep != null) onStep(tile, end);
                yield return token.Hop(start, end, stepTime, last ? 0.9f : 0.55f);
            }
            int dest = path.Length > 0 ? path[path.Length - 1] : from;
            _positions[player] = dest;
            Relayout(dest, false);
        }
    }
}
