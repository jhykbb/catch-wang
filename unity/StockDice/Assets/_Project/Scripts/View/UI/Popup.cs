using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace StockDice.View
{
    public sealed class PopupChoice
    {
        public string Label;
        public string Detail;
        public Color Color;
        public Action OnPick;
        public bool Enabled = true;

        public PopupChoice(string label, Action onPick, Color? color = null, string detail = null, bool enabled = true)
        {
            Label = label;
            OnPick = onPick;
            Color = color ?? Palette.ButtonNeutral;
            Detail = detail;
            Enabled = enabled;
        }
    }

    /// <summary>화면 가운데 뜨는 선택 창. 뒤는 어둡게 막는다.</summary>
    public sealed class Popup : MonoBehaviour
    {
        private Action _onCancel;

        public static Popup Show(Transform parent, string title, string subtitle, IList<PopupChoice> choices, Action onCancel = null, int columns = 1, float width = 620f)
        {
            var root = UIKit.Create("Popup", parent);
            UIKit.Fill(root.GetComponent<RectTransform>());
            var popup = root.AddComponent<Popup>();
            popup._onCancel = onCancel;

            var dim = root.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.45f);
            dim.raycastTarget = true;

            var card = UIKit.Panel(root.transform, "Card", Palette.PanelBg);
            var rt = card.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(width, 100f);
            var fitter = card.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            UIKit.VStack(card.gameObject, 12f, new RectOffset(28, 28, 24, 24), TextAnchor.UpperCenter);
            UIKit.AddShadow(card, new Color(0f, 0f, 0f, 0.25f), new Vector2(0f, -6f));

            var t = UIKit.Label(card.transform, title, 32, Palette.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Pref(t, -1, 46);
            if (!string.IsNullOrEmpty(subtitle))
            {
                UIKit.Label(card.transform, subtitle, 21, Palette.InkSoft, TextAnchor.MiddleCenter);
            }

            Transform listParent = card.transform;
            if (columns > 1)
            {
                var grid = UIKit.Create("Grid", card.transform);
                var g = grid.AddComponent<GridLayoutGroup>();
                float cell = (width - 56f - (columns - 1) * 10f) / columns;
                g.cellSize = new Vector2(cell, 64f);
                g.spacing = new Vector2(10f, 10f);
                g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                g.constraintCount = columns;
                g.childAlignment = TextAnchor.UpperCenter;
                int rows = Mathf.CeilToInt(choices.Count / (float)columns);
                UIKit.Pref(grid.GetComponent<RectTransform>(), -1, rows * 64f + (rows - 1) * 10f);
                listParent = grid.transform;
            }

            foreach (var choice in choices)
            {
                var c = choice;
                bool dark = c.Color.grayscale < 0.6f;
                string label = string.IsNullOrEmpty(c.Detail) || columns > 1 ? c.Label : c.Label + "\n<size=17>" + c.Detail + "</size>";
                var btn = UIKit.Button(listParent, label, c.Color, dark ? Color.white : Palette.Ink, () =>
                {
                    popup.Close();
                    if (c.OnPick != null) c.OnPick();
                }, 24);
                btn.interactable = c.Enabled;
                if (columns <= 1) UIKit.Pref(btn, -1, string.IsNullOrEmpty(c.Detail) ? 60f : 78f);
            }

            if (onCancel != null)
            {
                var cancel = UIKit.Button(card.transform, "취소", Palette.ButtonNeutral, Palette.InkSoft, () =>
                {
                    popup.Close();
                    onCancel();
                }, 22);
                UIKit.Pref(cancel, -1, 52f);
            }

            popup.StartCoroutine(Tween.Run(0.18f, k => { if (card != null) card.transform.localScale = Vector3.one * Mathf.LerpUnclamped(0.85f, 1f, k); }, Ease.OutBack));
            return popup;
        }

        /// <summary>단순 알림 (확인 버튼 하나).</summary>
        public static Popup Message(Transform parent, string title, string message, string ok = "확인", Action onOk = null)
        {
            return Show(parent, title, message, new List<PopupChoice> { new PopupChoice(ok, onOk, Palette.ButtonPrimary) });
        }

        public void Close()
        {
            if (this != null && gameObject != null) Destroy(gameObject);
        }

        public void Cancel()
        {
            Close();
            if (_onCancel != null) _onCancel();
        }
    }
}
