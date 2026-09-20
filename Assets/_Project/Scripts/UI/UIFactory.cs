using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Mirro.UI
{
    /// <summary>
    /// 코드로 uGUI를 구성하기 위한 헬퍼. 둥근 사각형/원 스프라이트는 런타임에 절차적으로
    /// 생성하므로 별도 이미지 에셋이 필요 없고, 9-slice라 어떤 크기에서도 모서리가 일정하다.
    /// </summary>
    public static class UIFactory
    {
        private const int CornerTexRadius = 64;

        private static Font _font;
        private static Sprite _roundedSprite;
        private static Sprite _circleSprite;

        /// <summary>한글 표시를 위해 OS 폰트(맑은 고딕)를 우선 사용하고, 없으면 내장 폰트로 폴백한다.</summary>
        public static Font GetFont()
        {
            if (_font != null) return _font;
            _font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "Segoe UI", "Arial" }, 48);
            if (_font == null)
                _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return _font;
        }

        public static Sprite RoundedSprite
        {
            get
            {
                if (_roundedSprite != null) return _roundedSprite;

                int r = CornerTexRadius;
                int size = r * 2 + 4;
                var tex = NewTexture(size);
                var pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float cx = x + 0.5f;
                        float cy = y + 0.5f;
                        float dx = Mathf.Max(r - cx, 0f, cx - (size - r));
                        float dy = Mathf.Max(r - cy, 0f, cy - (size - r));
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float a = Mathf.Clamp01(r - d + 0.5f);
                        pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                    }
                }
                tex.SetPixels32(pixels);
                tex.Apply(false, true);

                _roundedSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                    SpriteMeshType.FullRect, new Vector4(r, r, r, r));
                _roundedSprite.hideFlags = HideFlags.HideAndDontSave;
                return _roundedSprite;
            }
        }

        public static Sprite CircleSprite
        {
            get
            {
                if (_circleSprite != null) return _circleSprite;

                const int size = 256;
                float radius = size * 0.5f;
                var tex = NewTexture(size);
                var pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dx = x + 0.5f - radius;
                        float dy = y + 0.5f - radius;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float a = Mathf.Clamp01(radius - d + 0.5f);
                        pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                    }
                }
                tex.SetPixels32(pixels);
                tex.Apply(false, true);

                _circleSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
                _circleSprite.hideFlags = HideFlags.HideAndDontSave;
                return _circleSprite;
            }
        }

        private static Texture2D NewTexture(int size)
        {
            return new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        public static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>부모 중심 기준의 고정 크기 박스로 배치한다.</summary>
        public static void SetBox(RectTransform rt, Vector2 anchoredPosition, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPosition;
            rt.sizeDelta = size;
        }

        public static Image AddRounded(RectTransform rt, Color color, float cornerRadius)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = RoundedSprite;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = CornerTexRadius / Mathf.Max(1f, cornerRadius);
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        public static Image AddCircle(RectTransform rt, Color color)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = CircleSprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        public static Image AddSolid(RectTransform rt, Color color)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        public static Text AddLabel(Transform parent, string name, string content, int fontSize, Color color,
            FontStyle style = FontStyle.Normal)
        {
            var rt = NewRect(name, parent);
            Stretch(rt);
            var text = rt.gameObject.AddComponent<Text>();
            text.font = GetFont();
            text.text = content;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        public class ButtonParts
        {
            public RectTransform rect;
            public Image image;
            public Text label;
            public Button button;

            public void SetInteractable(bool interactable, Color enabledColor, Color enabledText)
            {
                button.interactable = interactable;
                image.color = interactable ? enabledColor : new Color(0.30f, 0.32f, 0.34f);
                label.color = interactable ? enabledText : new Color(0.55f, 0.58f, 0.60f);
            }
        }

        /// <summary>둥근 배경 + 가운데 글자 + 호버 확대가 붙은 버튼.</summary>
        public static ButtonParts AddTextButton(Transform parent, string name, string text, Vector2 position, Vector2 size,
            Color color, Color textColor, int fontSize, UnityAction onClick, float cornerRadius = 40f)
        {
            var rt = NewRect(name, parent);
            SetBox(rt, position, size);
            var image = AddRounded(rt, color, cornerRadius);
            var button = MakeButton(rt, image, onClick, 1.05f);
            var label = AddLabel(rt, "Label", text, fontSize, textColor, FontStyle.Bold);
            return new ButtonParts { rect = rt, image = image, label = label, button = button };
        }

        /// <summary>한 줄 입력창(둥근 배경). 레거시 InputField를 코드로 조립한다.</summary>
        public static InputField AddInputField(RectTransform rt, string defaultText, string placeholder, int fontSize,
            Color background, Color textColor)
        {
            var bg = AddRounded(rt, background, 36f);
            bg.raycastTarget = true;

            var text = AddLabel(rt, "Text", string.Empty, fontSize, textColor);
            text.alignment = TextAnchor.MiddleLeft;
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.rectTransform.offsetMin = new Vector2(28f, 6f);
            text.rectTransform.offsetMax = new Vector2(-28f, -6f);

            var hint = AddLabel(rt, "Placeholder", placeholder, fontSize,
                new Color(textColor.r, textColor.g, textColor.b, 0.4f), FontStyle.Italic);
            hint.alignment = TextAnchor.MiddleLeft;
            hint.rectTransform.offsetMin = new Vector2(28f, 6f);
            hint.rectTransform.offsetMax = new Vector2(-28f, -6f);

            var input = rt.gameObject.AddComponent<InputField>();
            input.targetGraphic = bg;
            input.textComponent = text;
            input.placeholder = hint;
            input.lineType = InputField.LineType.SingleLine;
            input.characterLimit = 45;
            input.text = defaultText;
            return input;
        }

        /// <summary>targetGraphic이 클릭을 받는다(raycastTarget 활성화). hoverScale이 1보다 크면 호버 확대 효과를 붙인다.</summary>
        public static Button MakeButton(RectTransform rt, Graphic targetGraphic, UnityAction onClick, float hoverScale = 1.06f)
        {
            targetGraphic.raycastTarget = true;
            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = targetGraphic;
            button.transition = Selectable.Transition.None;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(onClick);
            if (hoverScale > 1f)
                rt.gameObject.AddComponent<UIHoverScale>().hoverScale = hoverScale;
            return button;
        }

        public static Color ContrastText(Color background)
        {
            float luminance = 0.299f * background.r + 0.587f * background.g + 0.114f * background.b;
            return luminance > 0.6f ? new Color(0.10f, 0.12f, 0.14f) : Color.white;
        }

        public static void EnsureEventSystem()
        {
            if (Object.FindAnyObjectByType<EventSystem>() != null) return;

            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            var module = go.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();
        }
    }
}
