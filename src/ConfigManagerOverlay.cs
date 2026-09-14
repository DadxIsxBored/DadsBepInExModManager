using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DadsBepInExModManager
{
    internal sealed class ConfigManagerOverlay : MonoBehaviour
    {
        private const int WindowId = 774193;
        private static ConfigManagerOverlay _instance;

        private readonly List<PluginInfo> _plugins = new List<PluginInfo>();
        private readonly Dictionary<ConfigEntryBase, object> _originalValues = new Dictionary<ConfigEntryBase, object>();
        private readonly Dictionary<ConfigEntryBase, string> _textValues = new Dictionary<ConfigEntryBase, string>();
        private readonly HashSet<ConfigFile> _changedFiles = new HashSet<ConfigFile>();

        private GameObject _inputBlocker;
        private GameObject _previousSelection;
        private CursorLockMode _previousCursorLock;
        private bool _previousCursorVisible;
        private bool _wasGamePaused;
        private float _previousTimeScale = 1f;
        private int _blockUntilFrame = -1;
        private int _selectedPlugin;
        private string _pluginSearch = string.Empty;
        private string _settingSearch = string.Empty;
        private Vector2 _pluginScroll;
        private Vector2 _settingScroll;
        private Rect _windowRect;

        private GUIStyle _windowStyle;
        private GUIStyle _headerStyle;
        private GUIStyle _sectionStyle;
        private GUIStyle _descriptionStyle;
        private GUIStyle _selectedButtonStyle;
        private GUIStyle _panelStyle;
        private Texture2D _screenTexture;
        private Texture2D _windowTexture;
        private Texture2D _panelTexture;
        private Texture2D _selectedTexture;

        internal bool IsOpen { get; private set; }
        internal static bool BlocksInput => _instance != null && (_instance.IsOpen || Time.frameCount <= _instance._blockUntilFrame);

        private void Awake()
        {
            _instance = this;
            CreateInputBlocker();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F1))
            {
                if (IsOpen) Close(true);
                else Open();
                return;
            }

            if (IsOpen && Input.GetKeyDown(KeyCode.Escape))
            {
                Close(true);
                return;
            }

            if (IsOpen)
            {
                EnforceModalState();
                if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null)
                {
                    EventSystem.current.SetSelectedGameObject(null);
                }
            }
            else if (_inputBlocker != null && _inputBlocker.activeSelf && Time.frameCount > _blockUntilFrame)
            {
                _inputBlocker.SetActive(false);
            }
        }

        private void LateUpdate()
        {
            if (IsOpen) EnforceModalState();
        }

        private void OnGUI()
        {
            if (!IsOpen) return;
            EnforceModalState();
            EnsureStyles();
            GUI.depth = -10000;
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), _screenTexture, ScaleMode.StretchToFill);
            _windowRect = new Rect(Screen.width * 0.04f, Screen.height * 0.04f, Screen.width * 0.92f, Screen.height * 0.92f);
            GUI.ModalWindow(WindowId, _windowRect, DrawWindow, GUIContent.none, _windowStyle);
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Dads BepInEx Mod Manager", _headerStyle, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("Reload", GUILayout.Width(100f), GUILayout.Height(36f))) ReloadAll();
            if (GUILayout.Button("Save & Close", GUILayout.Width(140f), GUILayout.Height(36f))) Close(true);
            if (GUILayout.Button("Cancel", GUILayout.Width(100f), GUILayout.Height(36f))) Close(false);
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();
            DrawPluginColumn();
            GUILayout.Space(10f);
            DrawSettingsColumn();
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private void DrawPluginColumn()
        {
            GUILayout.BeginVertical(_panelStyle, GUILayout.Width(310f), GUILayout.ExpandHeight(true));
            GUILayout.Label("Loaded mods", _sectionStyle);
            GUILayout.Label("Search");
            _pluginSearch = GUILayout.TextField(_pluginSearch ?? string.Empty, GUILayout.Height(30f));
            GUILayout.Space(5f);
            _pluginScroll = GUILayout.BeginScrollView(_pluginScroll, GUILayout.ExpandHeight(true));

            List<PluginInfo> visible = _plugins.Where(plugin => Matches(plugin.Metadata.Name, _pluginSearch) || Matches(plugin.Metadata.GUID, _pluginSearch)).ToList();
            foreach (PluginInfo plugin in visible)
            {
                int index = _plugins.IndexOf(plugin);
                GUIStyle style = index == _selectedPlugin ? _selectedButtonStyle : GUI.skin.button;
                if (GUILayout.Button(plugin.Metadata.Name + "\n" + plugin.Metadata.Version, style, GUILayout.Height(52f)))
                {
                    _selectedPlugin = index;
                    _settingScroll = Vector2.zero;
                    _settingSearch = string.Empty;
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void DrawSettingsColumn()
        {
            GUILayout.BeginVertical(_panelStyle, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (_plugins.Count == 0)
            {
                GUILayout.Label("No loaded plugins expose BepInEx configuration entries.", _sectionStyle);
                GUILayout.EndVertical();
                return;
            }

            _selectedPlugin = Mathf.Clamp(_selectedPlugin, 0, _plugins.Count - 1);
            PluginInfo plugin = _plugins[_selectedPlugin];
            GUILayout.Label(plugin.Metadata.Name + "  " + plugin.Metadata.Version, _sectionStyle);
            GUILayout.Label(plugin.Metadata.GUID, _descriptionStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Search settings", GUILayout.Width(120f));
            _settingSearch = GUILayout.TextField(_settingSearch ?? string.Empty, GUILayout.Height(30f));
            GUILayout.EndHorizontal();
            GUILayout.Space(5f);

            List<ConfigEntryBase> entries = GetEntries(plugin)
                .Where(entry => Matches(entry.Definition.Section, _settingSearch) ||
                                Matches(entry.Definition.Key, _settingSearch) ||
                                Matches(entry.Description?.Description, _settingSearch))
                .ToList();

            _settingScroll = GUILayout.BeginScrollView(_settingScroll, GUILayout.ExpandHeight(true));
            string section = null;
            foreach (ConfigEntryBase entry in entries)
            {
                if (!string.Equals(section, entry.Definition.Section, StringComparison.Ordinal))
                {
                    section = entry.Definition.Section;
                    GUILayout.Space(6f);
                    GUILayout.Label(section, _sectionStyle);
                }
                DrawEntry(entry);
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void DrawEntry(ConfigEntryBase entry)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label(entry.Definition.Key, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("Reset", GUILayout.Width(72f))) SetValue(entry, entry.DefaultValue);
            GUILayout.EndHorizontal();

            if (!string.IsNullOrWhiteSpace(entry.Description?.Description))
            {
                GUILayout.Label(entry.Description.Description, _descriptionStyle);
            }

            Type type = entry.SettingType;
            object current = entry.BoxedValue;
            if (type == typeof(bool))
            {
                bool value = (bool)current;
                bool next = GUILayout.Toggle(value, value ? "Enabled" : "Disabled", GUILayout.Height(28f));
                if (next != value) SetValue(entry, next);
            }
            else if (type.IsEnum || GetChoices(entry) != null)
            {
                DrawChoice(entry, current);
            }
            else if (IsNumber(type) && TryGetRange(entry, out double minimum, out double maximum))
            {
                GUILayout.BeginHorizontal();
                float value = Convert.ToSingle(current, CultureInfo.InvariantCulture);
                float next = GUILayout.HorizontalSlider(value, (float)minimum, (float)maximum, GUILayout.ExpandWidth(true));
                if (IsInteger(type)) next = Mathf.Round(next);
                GUILayout.Label(FormatValue(ConvertNumber(next, type)), GUILayout.Width(110f));
                GUILayout.EndHorizontal();
                object converted = ConvertNumber(next, type);
                if (!Equals(converted, current)) SetValue(entry, converted);
            }
            else
            {
                if (!_textValues.TryGetValue(entry, out string buffer)) buffer = FormatValue(current);
                string next = GUILayout.TextField(buffer ?? string.Empty, GUILayout.Height(28f));
                _textValues[entry] = next;
                if (!string.Equals(next, buffer, StringComparison.Ordinal) && TryConvert(next, type, out object converted))
                {
                    SetValue(entry, converted);
                }
            }
            GUILayout.EndVertical();
        }

        private void DrawChoice(ConfigEntryBase entry, object current)
        {
            IList acceptable = GetChoices(entry);
            List<object> values = entry.SettingType.IsEnum
                ? Enum.GetValues(entry.SettingType).Cast<object>().ToList()
                : acceptable.Cast<object>().ToList();
            if (values.Count == 0)
            {
                GUILayout.Label(FormatValue(current));
                return;
            }

            int selected = Math.Max(0, values.FindIndex(value => Equals(value, current)));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("<", GUILayout.Width(46f), GUILayout.Height(30f)))
            {
                selected = (selected - 1 + values.Count) % values.Count;
                SetValue(entry, values[selected]);
            }
            GUILayout.Label(FormatValue(values[selected]), GUI.skin.textField, GUILayout.ExpandWidth(true), GUILayout.Height(30f));
            if (GUILayout.Button(">", GUILayout.Width(46f), GUILayout.Height(30f)))
            {
                selected = (selected + 1) % values.Count;
                SetValue(entry, values[selected]);
            }
            GUILayout.EndHorizontal();
        }

        private void Open()
        {
            RefreshPlugins();
            _originalValues.Clear();
            _textValues.Clear();
            _changedFiles.Clear();
            foreach (PluginInfo plugin in _plugins)
            {
                foreach (ConfigEntryBase entry in GetEntries(plugin))
                {
                    _originalValues[entry] = entry.BoxedValue;
                    _textValues[entry] = FormatValue(entry.BoxedValue);
                }
            }

            _previousTimeScale = Time.timeScale;
            _previousCursorLock = Cursor.lockState;
            _previousCursorVisible = Cursor.visible;
            _previousSelection = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            _wasGamePaused = Game.instance != null && Game.IsPaused();
            if (Game.instance != null && !_wasGamePaused) Game.Pause();
            Time.timeScale = 0f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            _inputBlocker.SetActive(true);
            IsOpen = true;
        }

        private void Close(bool save)
        {
            if (!IsOpen) return;
            if (save)
            {
                foreach (ConfigFile file in _changedFiles) file.Save();
            }
            else
            {
                foreach (KeyValuePair<ConfigEntryBase, object> original in _originalValues)
                {
                    original.Key.BoxedValue = original.Value;
                }
            }

            IsOpen = false;
            _blockUntilFrame = Time.frameCount + 1;
            if (Game.instance != null && !_wasGamePaused) Game.Unpause();
            Time.timeScale = _previousTimeScale;
            Cursor.lockState = _previousCursorLock;
            Cursor.visible = _previousCursorVisible;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(_previousSelection);
            _originalValues.Clear();
            _textValues.Clear();
            _changedFiles.Clear();
        }

        private void ReloadAll()
        {
            foreach (PluginInfo plugin in _plugins) plugin.Instance.Config.Reload();
            RefreshPlugins();
            _originalValues.Clear();
            _textValues.Clear();
            _changedFiles.Clear();
            foreach (PluginInfo plugin in _plugins)
            {
                foreach (ConfigEntryBase entry in GetEntries(plugin))
                {
                    _originalValues[entry] = entry.BoxedValue;
                    _textValues[entry] = FormatValue(entry.BoxedValue);
                }
            }
        }

        private void RefreshPlugins()
        {
            string selected = _plugins.Count > 0 && _selectedPlugin < _plugins.Count ? _plugins[_selectedPlugin].Metadata.GUID : null;
            _plugins.Clear();
            _plugins.AddRange(Chainloader.PluginInfos.Values
                .Where(plugin => plugin.Instance != null && plugin.Instance.Config != null && plugin.Instance.Config.Keys.Count > 0)
                .OrderBy(plugin => plugin.Metadata.Name, StringComparer.OrdinalIgnoreCase));
            int retained = selected == null ? -1 : _plugins.FindIndex(plugin => plugin.Metadata.GUID == selected);
            _selectedPlugin = retained >= 0 ? retained : Mathf.Clamp(_selectedPlugin, 0, Math.Max(0, _plugins.Count - 1));
        }

        private static IEnumerable<ConfigEntryBase> GetEntries(PluginInfo plugin)
        {
            return plugin.Instance.Config.Keys.Select(definition => plugin.Instance.Config[definition])
                .OrderBy(entry => entry.Definition.Section, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Definition.Key, StringComparer.OrdinalIgnoreCase);
        }

        private void SetValue(ConfigEntryBase entry, object value)
        {
            try
            {
                if (!Equals(entry.BoxedValue, value))
                {
                    entry.BoxedValue = value;
                    _changedFiles.Add(entry.ConfigFile);
                    _textValues[entry] = FormatValue(entry.BoxedValue);
                }
            }
            catch (Exception exception)
            {
                Plugin.Log?.LogWarning("Could not set " + entry.Definition.Section + "." + entry.Definition.Key + ": " + exception.Message);
            }
        }

        private void CreateInputBlocker()
        {
            _inputBlocker = new GameObject("DadsConfigManagerInputBlocker", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            DontDestroyOnLoad(_inputBlocker);
            Canvas canvas = _inputBlocker.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 32760;
            CanvasScaler scaler = _inputBlocker.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            GameObject blocker = new GameObject("RaycastBlocker", typeof(RectTransform), typeof(Image));
            blocker.transform.SetParent(_inputBlocker.transform, false);
            RectTransform rect = blocker.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            Image image = blocker.GetComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0.001f);
            image.raycastTarget = true;
            _inputBlocker.SetActive(false);
        }

        private void EnsureStyles()
        {
            if (_windowStyle != null) return;
            _screenTexture = MakeTexture(new Color(0f, 0f, 0f, 0.72f));
            _windowTexture = MakeTexture(new Color(0.08f, 0.09f, 0.11f, 0.98f));
            _panelTexture = MakeTexture(new Color(0.13f, 0.14f, 0.17f, 1f));
            _selectedTexture = MakeTexture(new Color(0.55f, 0.12f, 0.05f, 1f));

            _windowStyle = new GUIStyle(GUI.skin.window) { padding = new RectOffset(16, 16, 16, 16) };
            _windowStyle.normal.background = _windowTexture;
            _panelStyle = new GUIStyle(GUI.skin.box) { padding = new RectOffset(10, 10, 10, 10) };
            _panelStyle.normal.background = _panelTexture;
            _headerStyle = new GUIStyle(GUI.skin.label) { fontSize = 27, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            _sectionStyle = new GUIStyle(GUI.skin.label) { fontSize = 19, fontStyle = FontStyle.Bold };
            _sectionStyle.normal.textColor = new Color(1f, 0.62f, 0.18f);
            _descriptionStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
            _descriptionStyle.normal.textColor = new Color(0.78f, 0.8f, 0.84f);
            _selectedButtonStyle = new GUIStyle(GUI.skin.button);
            _selectedButtonStyle.normal.background = _selectedTexture;
            _selectedButtonStyle.hover.background = _selectedTexture;
            _selectedButtonStyle.active.background = _selectedTexture;
        }

        private static Texture2D MakeTexture(Color color)
        {
            Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        private static IList GetChoices(ConfigEntryBase entry)
        {
            object acceptable = entry.Description?.AcceptableValues;
            return acceptable?.GetType().GetProperty("AcceptableValues")?.GetValue(acceptable, null) as IList;
        }

        private static bool TryGetRange(ConfigEntryBase entry, out double minimum, out double maximum)
        {
            minimum = maximum = 0d;
            object acceptable = entry.Description?.AcceptableValues;
            if (acceptable == null) return false;
            var min = acceptable.GetType().GetProperty("MinValue");
            var max = acceptable.GetType().GetProperty("MaxValue");
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

        private static bool Matches(string value, string search)
        {
            return string.IsNullOrWhiteSpace(search) || (!string.IsNullOrEmpty(value) && value.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        internal void Shutdown()
        {
            if (IsOpen) Close(false);
            if (_inputBlocker != null) Destroy(_inputBlocker);
            foreach (Texture2D texture in new[] { _screenTexture, _windowTexture, _panelTexture, _selectedTexture })
            {
                if (texture != null) Destroy(texture);
            }
            if (_instance == this) _instance = null;
        }

        internal static void EnforceIfOpen()
        {
            if (_instance != null && _instance.IsOpen) _instance.EnforceModalState();
        }

        private void EnforceModalState()
        {
            Time.timeScale = 0f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    [HarmonyPatch(typeof(Player), "TakeInput")]
    internal static class PlayerInputBlockPatch
    {
        private static void Postfix(ref bool __result)
        {
            if (ConfigManagerOverlay.BlocksInput) __result = false;
        }
    }

    [HarmonyPatch(typeof(Menu), "Update")]
    internal static class MenuInputBlockPatch
    {
        private static bool Prefix()
        {
            return !ConfigManagerOverlay.BlocksInput;
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "Update")]
    internal static class InventoryInputBlockPatch
    {
        private static bool Prefix()
        {
            return !ConfigManagerOverlay.BlocksInput;
        }
    }

    [HarmonyPatch(typeof(Game), "IsPaused")]
    internal static class GamePausedStatePatch
    {
        private static void Postfix(ref bool __result)
        {
            if (ConfigManagerOverlay.BlocksInput) __result = true;
        }
    }

    [HarmonyPatch(typeof(Game), "UpdatePause")]
    internal static class GamePauseEnforcementPatch
    {
        private static void Postfix()
        {
            ConfigManagerOverlay.EnforceIfOpen();
        }
    }

    [HarmonyPatch(typeof(Player), "SetControls")]
    internal static class PlayerControlsBlockPatch
    {
        private static void Prefix(object[] __args)
        {
            if (!ConfigManagerOverlay.BlocksInput) return;
            if (__args.Length > 0) __args[0] = Vector3.zero;
            for (int i = 1; i < __args.Length; ++i)
            {
                if (__args[i] is bool) __args[i] = false;
            }
        }
    }

    [HarmonyPatch(typeof(Player), "SetMouseLook")]
    internal static class PlayerMouseLookBlockPatch
    {
        private static void Prefix(ref Vector2 __0)
        {
            if (ConfigManagerOverlay.BlocksInput) __0 = Vector2.zero;
        }
    }

    [HarmonyPatch(typeof(GameCamera), "UpdateCamera")]
    internal static class GameCameraInputBlockPatch
    {
        private static bool Prefix()
        {
            return !ConfigManagerOverlay.BlocksInput;
        }
    }

    [HarmonyPatch(typeof(GameCamera), "UpdateMouseCapture")]
    internal static class MouseCaptureBlockPatch
    {
        private static bool Prefix()
        {
            if (!ConfigManagerOverlay.BlocksInput) return true;
            ConfigManagerOverlay.EnforceIfOpen();
            return false;
        }
    }

    [HarmonyPatch(typeof(GameCamera), "LateUpdate")]
    internal static class CameraLateModalStatePatch
    {
        private static void Postfix()
        {
            ConfigManagerOverlay.EnforceIfOpen();
        }
    }
}
