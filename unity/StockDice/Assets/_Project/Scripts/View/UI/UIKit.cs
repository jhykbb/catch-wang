using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace StockDice.View
{
    /// <summary>uGUI 를 코드로 조립하는 도우미. 둥근 모서리 스프라이트도 런타임에 만든다.</summary>
    public static class UIKit
    {
        private static Sprite _rounded;
        private static Sprite _roundedSmall;
        private static Sprite _circle;

        /// <summary>둥근 사각형 (9-slice).</summary>
        public static Sprite Rounded
        {
            get { if (_rounded == null) _rounded = MakeRounded(96, 28); return _rounded; }
        }

        public static Sprite RoundedSmall
        {
            get { if (_roundedSmall == null) _roundedSmall = MakeRounded(48, 12); return _roundedSmall; }
        }

        public static Sprite Circle
        {
            get { if (_circle == null) _circle = MakeRounded(64, 32); return _circle; }
        }

        private static Sprite MakeRounded(int size, int radius)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "SD_Rounded" + radius,
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float px = x + 0.5f, py = y + 0.5f;
                    float cx = Mathf.Clamp(px, radius, size - radius);
                    float cy = Mathf.Clamp(py, radius, size - radius);
                    float dist = Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
                    float a = Mathf.Clamp01(radius - dist + 0.5f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            float border = Mathf.Min(radius, size / 2 - 1);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                new Vector4(border, border, border, border));
        }

        public static GameObject Create(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        public static RectTransform Rt(Component c)
        {
            return (RectTransform)c.transform;
        }

        // ---------------- 배치 ----------------

        /// <summary>앵커 영역 + 오프셋으로 배치.</summary>
        public static RectTransform Place(Component c, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var rt = Rt(c);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            return rt;
        }

        /// <summary>한 점 앵커 + 피벗 + 위치 + 크기로 배치.</summary>
        public static RectTransform PlaceAt(Component c, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var rt = Rt(c);
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        public static RectTransform Fill(Component c, float left = 0, float top = 0, float right = 0, float bottom = 0)
        {
            return Place(c, Vector2.zero, Vector2.one, new Vector2(left, bottom), new Vector2(-right, -top));
        }

        // ---------------- 위젯 ----------------

        public static Image Panel(Transform parent, string name, Color color, Sprite sprite = null)
        {
            var go = Create(name, parent);
            var img = go.AddComponent<Image>();
            img.sprite = sprite ?? Rounded;
            img.type = Image.Type.Sliced;
            img.color = color;
            img.raycastTarget = true;
            return img;
        }

        public static Image Rect(Transform parent, string name, Color color)
        {
            var go = Create(name, parent);
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        public static Text Label(Transform parent, string text, int size, Color color, TextAnchor anchor = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal)
        {
            var go = Create("Text", parent);
            var t = go.AddComponent<Text>();
            t.font = FontProvider.Get();
            t.fontSize = size;
            t.color = color;
            t.alignment = anchor;
            t.fontStyle = style;
            t.text = text;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = true;
            t.raycastTarget = false;
            return t;
        }

        public static Shadow AddShadow(Graphic g, Color color, Vector2 distance)
        {
            var s = g.gameObject.AddComponent<Shadow>();
            s.effectColor = color;
            s.effectDistance = distance;
            return s;
        }

        public static Button Button(Transform parent, string label, Color bg, Color fg, UnityAction onClick, int fontSize = 24)
        {
            var img = Panel(parent, "Button_" + label, bg);
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var cb = btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(0.93f, 0.93f, 0.93f, 1f);
            cb.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            cb.selectedColor = Color.white;
            cb.disabledColor = new Color(0.75f, 0.75f, 0.75f, 0.45f);
            cb.fadeDuration = 0.08f;
            btn.colors = cb;
            var nav = btn.navigation;
            nav.mode = Navigation.Mode.None;
            btn.navigation = nav;
            var text = Label(img.transform, label, fontSize, fg, TextAnchor.MiddleCenter, FontStyle.Bold);
            Fill(text, 6, 2, 6, 2);
            if (onClick != null) btn.onClick.AddListener(onClick);
            img.gameObject.AddComponent<ButtonFx>();
            return btn;
        }

        public static Text ButtonLabel(Button b)
        {
            return b.GetComponentInChildren<Text>();
        }

        public static void SetButton(Button b, string label, bool interactable)
        {
            if (b == null) return;
            var t = ButtonLabel(b);
            if (t != null && label != null) t.text = label;
            b.interactable = interactable;
        }

        public static InputField Input(Transform parent, string value, int fontSize, InputField.ContentType type = InputField.ContentType.Standard, int charLimit = 12)
        {
            var bg = Panel(parent, "Input", new Color(0.95f, 0.96f, 0.97f, 1f), RoundedSmall);
            var input = bg.gameObject.AddComponent<InputField>();
            var text = Label(bg.transform, "", fontSize, Palette.Ink);
            text.supportRichText = false;
            Fill(text, 12, 4, 12, 4);
            var ph = Label(bg.transform, "입력", fontSize, Palette.InkSoft, TextAnchor.MiddleLeft, FontStyle.Italic);
            Fill(ph, 12, 4, 12, 4);
            input.textComponent = text;
            input.placeholder = ph;
            input.contentType = type;
            input.characterLimit = charLimit;
            input.text = value;
            return input;
        }

        public static VerticalLayoutGroup VStack(GameObject go, float spacing, RectOffset padding = null, TextAnchor align = TextAnchor.UpperLeft)
        {
            var v = go.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = padding ?? new RectOffset(0, 0, 0, 0);
            v.childAlignment = align;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            return v;
        }

        public static HorizontalLayoutGroup HStack(GameObject go, float spacing, RectOffset padding = null, TextAnchor align = TextAnchor.MiddleLeft)
        {
            var h = go.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.padding = padding ?? new RectOffset(0, 0, 0, 0);
            h.childAlignment = align;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            return h;
        }

        public static LayoutElement Pref(Component c, float width = -1, float height = -1, float flexWidth = -1, float flexHeight = -1)
        {
            var le = c.GetComponent<LayoutElement>();
            if (le == null) le = c.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.preferredHeight = height;
            if (width >= 0) le.minWidth = width;
            if (height >= 0) le.minHeight = height;
            le.flexibleWidth = flexWidth;
            le.flexibleHeight = flexHeight;
            return le;
        }

        public static string Colored(string text, Color c)
        {
            return "<color=#" + ColorUtility.ToHtmlStringRGB(c) + ">" + text + "</color>";
        }
    }

    /// <summary>버튼에 마우스를 올리면 살짝 커지고, 누르면 살짝 작아진다.</summary>
    public sealed class ButtonFx : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        private float _target = 1f;
        private float _current = 1f;
        private Selectable _selectable;

        private void Awake()
        {
            _selectable = GetComponent<Selectable>();
        }

        private bool Interactable { get { return _selectable == null || _selectable.IsInteractable(); } }

        public void OnPointerEnter(PointerEventData e) { if (Interactable) _target = 1.05f; }
        public void OnPointerExit(PointerEventData e) { _target = 1f; }
        public void OnPointerDown(PointerEventData e) { if (Interactable) _target = 0.95f; }
        public void OnPointerUp(PointerEventData e) { _target = Interactable ? 1.05f : 1f; }

        private void OnDisable()
        {
            _target = _current = 1f;
            transform.localScale = Vector3.one;
        }

        private void Update()
        {
            if (!Interactable) _target = 1f;
            if (Mathf.Abs(_current - _target) < 0.001f) return;
            _current = Mathf.Lerp(_current, _target, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 18f));
            transform.localScale = Vector3.one * _current;
        }
    }

    /// <summary>숫자가 부드럽게 올라가거나 내려가는 텍스트.</summary>
    public sealed class CountUpText : MonoBehaviour
    {
        public Text Target;
        public Func<long, string> Format = v => Core.Fmt.Won(v);
        private double _shown;
        private long _goal;
        private bool _init;

        public void Set(long value, bool instant = false)
        {
            _goal = value;
            if (!_init || instant)
            {
                _init = true;
                _shown = value;
                if (Target != null) Target.text = Format(value);
            }
        }

        private void Update()
        {
            if (Target == null) return;
            if (System.Math.Abs(_shown - _goal) < 0.5) { _shown = _goal; return; }
            _shown += (_goal - _shown) * (1.0 - Mathf.Exp(-Time.unscaledDeltaTime * 6f));
            if (System.Math.Abs(_shown - _goal) < 1) _shown = _goal;
            Target.text = Format((long)System.Math.Round(_shown));
        }
    }
}
