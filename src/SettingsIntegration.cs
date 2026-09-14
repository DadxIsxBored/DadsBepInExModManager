using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Valheim.SettingsGui;

namespace DadsBepInExModManager
{
    [HarmonyPatch(typeof(Settings), "InitializeTabs")]
    internal static class SettingsInitializeTabsPatch
    {
        private static void Prefix(Settings __instance)
        {
            ModSettingsTab.Install(__instance);
        }
    }

    [HarmonyPatch(typeof(Settings), "ActiveTabChanged")]
    internal static class SettingsActiveTabPatch
    {
        private static void Prefix(Settings __instance, int index)
        {
            ModSettingsTab.InputCaptured = __instance.SettingsTabs != null &&
                                           index >= 0 && index < __instance.SettingsTabs.Count &&
                                           __instance.SettingsTabs[index] is ModSettingsTab;
        }
    }

    [HarmonyPatch(typeof(Player), "TakeInput")]
    internal static class PlayerInputPatch
    {
        private static void Postfix(ref bool __result)
        {
            if (ModSettingsTab.InputCaptured)
            {
                __result = false;
            }
        }
    }

    internal sealed class ModSettingsTab : MonoBehaviour, ISettingsTab
    {
        internal static bool InputCaptured;

        private Button _buttonTemplate;
        private Toggle _toggleTemplate;
        private Slider _sliderTemplate;
        private TMP_Text _textTemplate;
        private RectTransform _root;
        private RectTransform _content;
        private TMP_Text _pluginName;
        private TMP_Text _pageText;
        private readonly List<PluginInfo> _plugins = new List<PluginInfo>();
        private readonly Dictionary<ConfigEntryBase, object> _pending = new Dictionary<ConfigEntryBase, object>();
        private readonly List<GameObject> _generated = new List<GameObject>();
        private int _pluginIndex;

#pragma warning disable 67
        public event Action<string, int> SharedSettingChanged;
#pragma warning restore 67

        internal static void Install(Settings settings)
        {
            TabHandler handler = settings.GetComponentInChildren<TabHandler>(true);
            if (handler == null || handler.m_tabs == null || handler.m_tabs.Count == 0 ||
                handler.m_tabs.Any(t => t.m_page != null && t.m_page.GetComponent<ModSettingsTab>() != null))
            {
                return;
            }

            TabHandler.Tab source = handler.m_tabs[0];
            GameplaySettings gameplay = source.m_page.GetComponent<GameplaySettings>();
            if (gameplay == null)
            {
                return;
            }

            Button tabButton = Instantiate(source.m_button, source.m_button.transform.parent, false);
            tabButton.name = "ModsTabButton";
            SetText(tabButton.gameObject, "Mods");

            RectTransform page = Instantiate(source.m_page, source.m_page.parent, false);
            page.name = "ModsSettingsPage";
            page.gameObject.SetActive(false);

            foreach (MonoBehaviour component in page.GetComponents<MonoBehaviour>())
            {
                if (component is ISettingsTab)
                {
                    DestroyImmediate(component);
                }
            }
            for (int i = page.childCount - 1; i >= 0; --i)
            {
                DestroyImmediate(page.GetChild(i).gameObject);
            }

            ModSettingsTab tab = page.gameObject.AddComponent<ModSettingsTab>();
            tab._buttonTemplate = settings.m_okButton;
            tab._toggleTemplate = gameplay.m_toggleRun;
            tab._sliderTemplate = gameplay.m_autoBackups;
            tab._textTemplate = gameplay.m_autoBackupsText;

            TabHandler.Tab entry = new TabHandler.Tab
            {
                m_button = tabButton,
                m_page = page,
                m_default = false,
                m_onClick = new UnityEvent()
            };
            handler.m_tabs.Add(entry);
        }

        public void Initialize()
        {
            BuildShell();
            RefreshPlugins();
        }

        public void Terminate()
        {
            InputCaptured = false;
        }

        public void OnTabOpen(Button backButton, Button okButton)
        {
            InputCaptured = true;
            RefreshPlugins(false);
            EventSystem.current?.SetSelectedGameObject(_pluginName != null ? _pluginName.gameObject : gameObject);
        }

        public void OnOkAsync(OkActionCompletedHandler completed)
        {
            HashSet<ConfigFile> files = new HashSet<ConfigFile>();
            foreach (KeyValuePair<ConfigEntryBase, object> change in _pending)
            {
                change.Key.BoxedValue = change.Value;
                files.Add(change.Key.ConfigFile);
            }
            foreach (ConfigFile file in files)
            {
                file.Save();
            }
            _pending.Clear();
            InputCaptured = false;
            completed?.Invoke();
        }

        public void OnBack()
        {
            _pending.Clear();
            InputCaptured = false;
        }

        public void OnSharedSettingChanged(string setting, int value)
        {
        }

        private void OnDisable()
        {
            InputCaptured = false;
        }

        private void BuildShell()
        {
            Image blocker = gameObject.AddComponent<Image>();
            blocker.color = new Color(0f, 0f, 0f, 0.001f);
            blocker.raycastTarget = true;

            _root = NewRect("DadsModSettingsRoot", transform);
            Stretch(_root, 22f, 22f, 22f, 16f);

            Button previous = NewButton(_root, "<", new Vector2(64f, 58f));
            Place(previous.GetComponent<RectTransform>(), 0f, 0f, 64f, 58f);
            previous.onClick.AddListener(() => ChangePlugin(-1));

            _pluginName = NewText(_root, "No configurable plugins loaded", 30f, TextAlignmentOptions.Center);
            Place(_pluginName.rectTransform, 76f, 0f, -152f, 58f, true);

            Button next = NewButton(_root, ">", new Vector2(64f, 58f));
            AnchorRight(next.GetComponent<RectTransform>(), 0f, 0f, 64f, 58f);
            next.onClick.AddListener(() => ChangePlugin(1));

            _pageText = NewText(_root, string.Empty, 20f, TextAlignmentOptions.Center);
            AnchorRight(_pageText.rectTransform, 70f, 0f, 110f, 58f);

            RectTransform viewport = NewRect("Viewport", _root);
            Place(viewport, 0f, 72f, 0f, 0f, true, true);
            Image viewportImage = viewport.gameObject.AddComponent<Image>();
            viewportImage.color = new Color(0f, 0f, 0f, 0.08f);
            viewportImage.raycastTarget = true;
            viewport.gameObject.AddComponent<RectMask2D>();

            _content = NewRect("Content", viewport);
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.offsetMin = Vector2.zero;
            _content.offsetMax = Vector2.zero;

            ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = _content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 34f;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = true;
        }

        private void RefreshPlugins(bool rebuild = true)
        {
            string selectedGuid = _plugins.Count > 0 ? _plugins[_pluginIndex].Metadata.GUID : null;
            _plugins.Clear();
            _plugins.AddRange(Chainloader.PluginInfos.Values
                .Where(p => p.Instance != null && p.Instance.Config != null && p.Instance.Config.Keys.Count > 0)
                .OrderBy(p => p.Metadata.Name, StringComparer.OrdinalIgnoreCase));

            if (_plugins.Count == 0)
            {
                _pluginIndex = 0;
            }
            else
            {
                int retained = selectedGuid == null ? -1 : _plugins.FindIndex(p => p.Metadata.GUID == selectedGuid);
                _pluginIndex = retained >= 0 ? retained : Mathf.Clamp(_pluginIndex, 0, _plugins.Count - 1);
            }
            if (rebuild)
            {
                RebuildEntries();
            }
        }

        private void ChangePlugin(int delta)
        {
            if (_plugins.Count == 0) return;
            _pluginIndex = (_pluginIndex + delta + _plugins.Count) % _plugins.Count;
            RebuildEntries();
        }

        private void RebuildEntries()
        {
            foreach (GameObject item in _generated)
            {
                Destroy(item);
            }
            _generated.Clear();

            if (_plugins.Count == 0)
            {
                _pluginName.text = "No configurable plugins loaded";
                _pageText.text = string.Empty;
                _content.sizeDelta = Vector2.zero;
                return;
            }

            PluginInfo plugin = _plugins[_pluginIndex];
            _pluginName.text = plugin.Metadata.Name + "  " + plugin.Metadata.Version;
            _pageText.text = (_pluginIndex + 1).ToString(CultureInfo.InvariantCulture) + " / " + _plugins.Count.ToString(CultureInfo.InvariantCulture);

            List<ConfigEntryBase> entries = plugin.Instance.Config.Keys
                .Select(key => plugin.Instance.Config[key])
                .OrderBy(entry => entry.Definition.Section, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Definition.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            float y = 0f;
            string section = null;
            foreach (ConfigEntryBase entry in entries)
            {
                if (!string.Equals(section, entry.Definition.Section, StringComparison.Ordinal))
                {
                    section = entry.Definition.Section;
                    TMP_Text heading = NewText(_content, section, 27f, TextAlignmentOptions.BottomLeft);
                    Place(heading.rectTransform, 18f, y, -18f, 46f, true);
                    _generated.Add(heading.gameObject);
                    y += 50f;
                }
                BuildEntry(entry, y);
                y += 74f;
            }
            _content.sizeDelta = new Vector2(0f, y + 12f);
            _content.anchoredPosition = Vector2.zero;
        }

        private void BuildEntry(ConfigEntryBase entry, float y)
        {
            GameObject row = new GameObject("Setting_" + entry.Definition.Key, typeof(RectTransform), typeof(Image));
            row.transform.SetParent(_content, false);
            RectTransform rowRect = row.GetComponent<RectTransform>();
            Place(rowRect, 8f, y, -8f, 68f, true);
            Image rowImage = row.GetComponent<Image>();
            rowImage.color = new Color(0f, 0f, 0f, 0.14f);
            rowImage.raycastTarget = false;
            _generated.Add(row);

            string description = entry.Description?.Description;
            TMP_Text label = NewText(rowRect, entry.Definition.Key, 22f, TextAlignmentOptions.Left);
            label.text = string.IsNullOrWhiteSpace(description)
                ? entry.Definition.Key
                : entry.Definition.Key + "\n<size=70%><color=#C8C8C8>" + EscapeRichText(description) + "</color></size>";
            label.enableWordWrapping = true;
            Place(label.rectTransform, 16f, 3f, -520f, 62f, true);

            Type type = entry.SettingType;
            object current = GetPending(entry);
            if (type == typeof(bool))
            {
                Toggle toggle = Instantiate(_toggleTemplate, rowRect, false);
                CleanLocalization(toggle.gameObject);
                toggle.name = "Value";
                RectTransform rect = toggle.GetComponent<RectTransform>();
                AnchorRight(rect, 38f, 13f, 42f, 42f);
                toggle.onValueChanged = new Toggle.ToggleEvent();
                toggle.SetIsOnWithoutNotify((bool)current);
                toggle.onValueChanged.AddListener(value => SetPending(entry, value));
                return;
            }

            IList choices = GetChoices(entry);
            if (type.IsEnum || choices != null)
            {
                List<object> values = type.IsEnum
                    ? Enum.GetValues(type).Cast<object>().ToList()
                    : choices.Cast<object>().ToList();
                BuildChoice(rowRect, entry, current, values);
                return;
            }

            if (IsNumber(type) && TryGetRange(entry, out double minimum, out double maximum))
            {
                Slider slider = Instantiate(_sliderTemplate, rowRect, false);
                CleanLocalization(slider.gameObject);
                RectTransform sliderRect = slider.GetComponent<RectTransform>();
                AnchorRight(sliderRect, 150f, 17f, 330f, 34f);
                slider.onValueChanged = new Slider.SliderEvent();
                slider.minValue = (float)minimum;
                slider.maxValue = (float)maximum;
                slider.wholeNumbers = IsInteger(type);
                slider.SetValueWithoutNotify(Convert.ToSingle(current, CultureInfo.InvariantCulture));
                TMP_Text valueText = NewText(rowRect, FormatValue(current), 21f, TextAlignmentOptions.Center);
                AnchorRight(valueText.rectTransform, 20f, 8f, 112f, 52f);
                slider.onValueChanged.AddListener(value =>
                {
                    object converted = ConvertNumber(value, type);
                    valueText.text = FormatValue(converted);
                    SetPending(entry, converted);
                });
                return;
            }

            TMP_InputField input = NewInput(rowRect, FormatValue(current));
            AnchorRight(input.GetComponent<RectTransform>(), 20f, 8f, 460f, 52f);
            input.onEndEdit.AddListener(raw =>
            {
                if (TryConvert(raw, type, out object converted))
                {
                    SetPending(entry, converted);
                }
                else
                {
                    input.SetTextWithoutNotify(FormatValue(GetPending(entry)));
                }
            });
        }

        private void BuildChoice(RectTransform parent, ConfigEntryBase entry, object current, List<object> values)
        {
            int selected = Math.Max(0, values.FindIndex(v => Equals(v, current)));
            TMP_Text valueText = NewText(parent, FormatValue(values[selected]), 22f, TextAlignmentOptions.Center);
            AnchorRight(valueText.rectTransform, 82f, 8f, 316f, 52f);

            Button left = NewButton(parent, "<", new Vector2(54f, 52f));
            AnchorRight(left.GetComponent<RectTransform>(), 410f, 8f, 54f, 52f);
            Button right = NewButton(parent, ">", new Vector2(54f, 52f));
            AnchorRight(right.GetComponent<RectTransform>(), 20f, 8f, 54f, 52f);

            Action<int> change = direction =>
            {
                selected = (selected + direction + values.Count) % values.Count;
                object value = values[selected];
                valueText.text = FormatValue(value);
                SetPending(entry, value);
            };
            left.onClick.AddListener(() => change(-1));
            right.onClick.AddListener(() => change(1));
        }

        private object GetPending(ConfigEntryBase entry)
        {
            return _pending.TryGetValue(entry, out object value) ? value : entry.BoxedValue;
        }

        private void SetPending(ConfigEntryBase entry, object value)
        {
            _pending[entry] = value;
        }

        private static IList GetChoices(ConfigEntryBase entry)
        {
            object acceptable = entry.Description?.AcceptableValues;
            if (acceptable == null) return null;
            var property = acceptable.GetType().GetProperty("AcceptableValues");
            return property?.GetValue(acceptable, null) as IList;
        }

        private static bool TryGetRange(ConfigEntryBase entry, out double minimum, out double maximum)
        {
            minimum = maximum = 0d;
            object acceptable = entry.Description?.AcceptableValues;
            if (acceptable == null) return false;
            Type type = acceptable.GetType();
            var min = type.GetProperty("MinValue");
            var max = type.GetProperty("MaxValue");
            if (min == null || max == null) return false;
            minimum = Convert.ToDouble(min.GetValue(acceptable, null), CultureInfo.InvariantCulture);
            maximum = Convert.ToDouble(max.GetValue(acceptable, null), CultureInfo.InvariantCulture);
            return maximum > minimum;
        }

        private static bool TryConvert(string raw, Type type, out object value)
        {
            try
            {
                value = TomlTypeConverter.ConvertToValue(raw, type);
                return value != null;
            }
            catch
            {
                value = null;
                return false;
            }
        }

        private static bool IsNumber(Type type)
        {
            TypeCode code = Type.GetTypeCode(type);
            return code >= TypeCode.SByte && code <= TypeCode.Decimal;
        }

        private static bool IsInteger(Type type)
        {
            TypeCode code = Type.GetTypeCode(type);
            return code >= TypeCode.SByte && code <= TypeCode.UInt64;
        }

        private static object ConvertNumber(float value, Type type)
        {
            if (IsInteger(type)) value = Mathf.Round(value);
            return Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
        }

        private static string FormatValue(object value)
        {
            if (value == null) return string.Empty;
            if (value is IFormattable formattable) return formattable.ToString(null, CultureInfo.InvariantCulture);
            return value.ToString();
        }

        private Button NewButton(Transform parent, string text, Vector2 size)
        {
            Button button = Instantiate(_buttonTemplate, parent, false);
            button.name = "Button_" + text;
            button.onClick = new Button.ButtonClickedEvent();
            RectTransform rect = button.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = size;
            CleanLocalization(button.gameObject);
            SetText(button.gameObject, text);
            return button;
        }

        private TMP_InputField NewInput(Transform parent, string value)
        {
            Button source = Instantiate(_buttonTemplate, parent, false);
            source.name = "TextValue";
            RectTransform rect = source.GetComponent<RectTransform>();
            Button.ButtonClickedEvent empty = new Button.ButtonClickedEvent();
            source.onClick = empty;
            DestroyImmediate(source);
            CleanLocalization(rect.gameObject);
            TMP_Text text = rect.GetComponentInChildren<TMP_Text>(true);
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.margin = new Vector4(14f, 0f, 14f, 0f);
            text.enableWordWrapping = false;
            text.text = value;
            TMP_InputField input = rect.gameObject.AddComponent<TMP_InputField>();
            input.textComponent = text;
            input.targetGraphic = rect.GetComponent<Image>();
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.text = value;
            return input;
        }

        private TMP_Text NewText(Transform parent, string value, float size, TextAlignmentOptions alignment)
        {
            TMP_Text text = Instantiate(_textTemplate, parent, false);
            text.name = "Text";
            CleanLocalization(text.gameObject);
            text.text = value;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        private static void Stretch(RectTransform rect, float left, float top, float right, float bottom)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        private static void Place(RectTransform rect, float left, float top, float rightOrWidth, float height, bool stretchX = false, bool stretchY = false)
        {
            rect.pivot = new Vector2(0f, 1f);
            rect.anchorMin = new Vector2(0f, stretchY ? 0f : 1f);
            rect.anchorMax = new Vector2(stretchX ? 1f : 0f, 1f);
            rect.offsetMin = new Vector2(left, stretchY ? 0f : -(top + height));
            rect.offsetMax = new Vector2(stretchX ? rightOrWidth : left + rightOrWidth, -top);
        }

        private static void AnchorRight(RectTransform rect, float right, float top, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-right, -top);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static void CleanLocalization(GameObject root)
        {
            foreach (Component component in root.GetComponentsInChildren<Component>(true))
            {
                if (component != null && component.GetType().Name == "Localize")
                {
                    DestroyImmediate(component);
                }
            }
        }

        private static void SetText(GameObject root, string value)
        {
            CleanLocalization(root);
            TMP_Text text = root.GetComponentInChildren<TMP_Text>(true);
            if (text != null) text.text = value;
        }

        private static string EscapeRichText(string text)
        {
            return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }
    }
}
