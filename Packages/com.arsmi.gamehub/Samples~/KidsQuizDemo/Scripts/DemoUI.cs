using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArsmiGames.Demo
{
    /// <summary>
    /// TextMeshPro-based uGUI builders for the demo.
    ///
    /// Everything is anchored or stretched — nothing is positioned by fixed pixels — so
    /// the layout survives any aspect ratio the platform gives it (a phone in portrait,
    /// a desktop at 21:9, the fullscreen button). The previous version pinned sizes and
    /// got clipped the moment the frame was not the size it expected.
    /// </summary>
    public static class DemoUI
    {
        public static readonly Color Bg = new Color32(0x0B, 0x0A, 0x0F, 0xFF);
        public static readonly Color Panel = new Color32(0x16, 0x15, 0x1C, 0xFF);
        public static readonly Color PanelSoft = new Color32(0x1E, 0x1C, 0x26, 0xFF);
        public static readonly Color Line = new Color32(0xFF, 0xFF, 0xFF, 0x14);
        public static readonly Color Accent = new Color32(0xCC, 0x78, 0x5C, 0xFF);
        public static readonly Color AccentSoft = new Color32(0xCC, 0x78, 0x5C, 0x2A);
        public static readonly Color Text = new Color32(0xEC, 0xEC, 0xF1, 0xFF);
        public static readonly Color Muted = new Color32(0x9A, 0x99, 0xA8, 0xFF);
        public static readonly Color Faint = new Color32(0x6A, 0x69, 0x78, 0xFF);
        public static readonly Color Good = new Color32(0x5C, 0xC4, 0x7D, 0xFF);
        public static readonly Color Bad = new Color32(0xE6, 0x5B, 0x5B, 0xFF);
        public static readonly Color Gold = new Color32(0xE8, 0xB3, 0x39, 0xFF);

        public static GameObject Node(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        /// <summary>Fills the parent, inset by `pad` on every side.</summary>
        public static RectTransform Fill(GameObject go, float pad = 0f)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(pad, pad);
            rt.offsetMax = new Vector2(-pad, -pad);
            return rt;
        }

        /// <summary>Anchors to a fractional slice of the parent, so it scales with it.</summary>
        public static RectTransform Anchor(GameObject go, Vector2 min, Vector2 max, Vector4 inset = default)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = new Vector2(inset.x, inset.y);
            rt.offsetMax = new Vector2(-inset.z, -inset.w);
            return rt;
        }

        public static Image Box(GameObject go, Color color)
        {
            var image = go.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>A card: background + a vertical stack that grows with its content.</summary>
        public static RectTransform Card(Transform parent, out VerticalLayoutGroup layout, int pad = 18, int spacing = 10)
        {
            var go = Node("Card", parent);
            Box(go, Panel);

            layout = go.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(pad, pad, pad, pad);
            layout.spacing = spacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            return go.GetComponent<RectTransform>();
        }

        public static TextMeshProUGUI Label(Transform parent, string content, float size, Color color,
                                            TextAlignmentOptions align = TextAlignmentOptions.TopLeft,
                                            FontStyles style = FontStyles.Normal)
        {
            var go = Node("Text", parent);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.text = content;
            text.fontSize = size;
            text.color = color;
            text.alignment = align;
            text.fontStyle = style;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>A heading with a small accent rule under it.</summary>
        public static void Section(Transform parent, string title)
        {
            var label = Label(parent, title.ToUpperInvariant(), 15, Accent, TextAlignmentOptions.Left, FontStyles.Bold);
            label.characterSpacing = 6f;
            Height(label.gameObject, 24);
        }

        public static LayoutElement Height(GameObject go, float height)
        {
            var element = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            element.minHeight = height;
            element.preferredHeight = height;
            return element;
        }

        public static Button Btn(Transform parent, string caption, System.Action onClick,
                                 Color? fill = null, Color? textColor = null, float height = 52, float fontSize = 18)
        {
            var go = Node("Button", parent);
            var image = Box(go, fill ?? PanelSoft);
            image.raycastTarget = true;

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;

            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.18f, 1.18f, 1.18f, 1f);
            colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(1f, 1f, 1f, 0.35f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            button.onClick.AddListener(() => onClick?.Invoke());

            Height(go, height);

            var label = Label(go.transform, caption, fontSize, textColor ?? Text,
                              TextAlignmentOptions.Center, FontStyles.Bold);
            Fill(label.gameObject, 10f);

            return button;
        }

        public static HorizontalLayoutGroup Row(Transform parent, float height = 52, int spacing = 10)
        {
            var go = Node("Row", parent);
            var layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            Height(go, height);
            return layout;
        }

        /// <summary>
        /// A scroll view that fills its parent. Returns the content transform.
        ///
        /// The viewport is stretched to the parent and the content is anchored to the top
        /// and sized by a fitter — which is what makes a long log scroll instead of being
        /// silently cut off at the bottom of the panel.
        /// </summary>
        public static RectTransform Scroll(Transform parent, out ScrollRect scroll, int pad = 8)
        {
            var viewport = Node("Viewport", parent);
            Box(viewport, new Color(0f, 0f, 0f, 0.28f)).raycastTarget = true;
            Fill(viewport);

            var mask = viewport.AddComponent<RectMask2D>();
            mask.padding = Vector4.zero;

            scroll = viewport.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 32f;

            var content = Node("Content", viewport.transform);
            var rt = content.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(0f, 0f);
            rt.offsetMax = new Vector2(0f, 0f);

            var layout = content.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(pad, pad, pad, pad);
            layout.spacing = 8;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = viewport.GetComponent<RectTransform>();
            scroll.content = rt;
            return rt;
        }

        /// <summary>A rounded-looking pill used for status chips.</summary>
        public static TextMeshProUGUI Chip(Transform parent, string text, Color color)
        {
            var go = Node("Chip", parent);
            Box(go, new Color(color.r, color.g, color.b, 0.14f));
            var label = Label(go.transform, text, 14, color, TextAlignmentOptions.Center, FontStyles.Bold);
            Fill(label.gameObject, 8f);
            return label;
        }
    }
}
