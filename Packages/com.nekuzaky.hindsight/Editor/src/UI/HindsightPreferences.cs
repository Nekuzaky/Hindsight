using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Nekuzaky.Hindsight.Editor
{
    internal static class HindsightPreferences
    {
        #region Public

        public const string Path = "Preferences/Hindsight";

        [MenuItem("Tools/Hindsight/Preferences...", priority = 20)]
        public static void Open() => SettingsService.OpenUserPreferences(Path);

        [SettingsProvider]
        public static SettingsProvider Create()
        {
            return new SettingsProvider(Path, SettingsScope.User)
            {
                label = "Hindsight",
                activateHandler = (_, root) => Build(root),
                keywords = new HashSet<string>
                {
                    "rewind", "record", "history", "memory", "budget", "timeline", "velocity", "track", "bookmark",
                    "error", "warning", "log", "trail", "ghost", "overlay", "static", "layer", "camera"
                }
            };
        }

        #endregion


        #region Building

        private static void Build(VisualElement root)
        {
            var settings = HindsightSettings.instance;
            root.styleSheets.Add(Glyphs.StyleSheet);
            var page = new ScrollView();
            page.AddToClassList("hs-prefs");
            root.Add(page);

            page.Add(Header());

            var recording = Section(page, "Recording", Glyphs.Record);
            AddToggle(recording, "Record in Play Mode", "Starts a recording every time you enter Play Mode. Turn it off to keep Hindsight installed without any cost.",
                settings.RecordInPlayMode, v => settings.RecordInPlayMode = v);
            AddSlider(recording, "History length (seconds)", "How far back you can rewind. Older frames are dropped as new ones arrive.",
                settings.HistorySeconds, HindsightSettings.MinHistorySeconds, HindsightSettings.MaxHistorySeconds, v => settings.HistorySeconds = v);
            AddInteger(recording, "Memory budget (MB)", "Memory reserved when Play Mode starts. When it is full, the oldest frames go first. Takes effect at the next Play Mode.",
                () => settings.MemoryBudgetMb, v => settings.MemoryBudgetMb = v);
            AddEnum(recording, "Capture rate", "Every frame gives exact rewinds. A fixed rate costs less in very fast scenes.",
                settings.CaptureRate, v => settings.CaptureRate = (CaptureRate)v);
            AddInteger(recording, "Object limit", "Objects beyond this count are not recorded. Takes effect at the next Play Mode.",
                () => settings.MaxObjects, v => settings.MaxObjects = v);

            var scope = Section(page, "What to record", Glyphs.Layers);
            AddToggle(scope, "Rigidbody velocities", "Records linear and angular velocity of 2D and 3D rigidbodies while they move.",
                settings.RecordVelocities, v => settings.RecordVelocities = v);
            AddToggle(scope, "Tracked fields", "Records fields marked with [HindsightTrack] and fields pinned from the Inspector.",
                settings.RecordTrackedFields, v => settings.RecordTrackedFields = v);
            AddToggle(scope, "Ignore static objects", "Static objects cannot move, so recording them is wasted memory.",
                settings.IgnoreStaticObjects, v => settings.IgnoreStaticObjects = v);
            AddToggle(scope, "Ignore cameras", "Keeps cameras where they are when you rewind, so the Game view keeps its current view.",
                settings.IgnoreCameras, v => settings.IgnoreCameras = v);
            Layers(scope, settings);

            var events = Section(page, "Events", Glyphs.Activity);
            AddToggle(events, "Errors and exceptions", "Adds a red marker for every error, exception and failed assertion in the Console.",
                settings.RecordErrors, v => settings.RecordErrors = v);
            AddToggle(events, "Warnings", "Adds an amber marker for every warning.", settings.RecordWarnings, v => settings.RecordWarnings = v);
            AddToggle(events, "Logs", "Adds a grey marker for every Debug.Log. Can be noisy.", settings.RecordLogs, v => settings.RecordLogs = v);
            AddToggle(events, "Editor messages", "Also records messages raised by editor code between two frames, such as inspectors and importers. Off keeps the timeline about the game.",
                settings.RecordEditorMessages, v => settings.RecordEditorMessages = v);
            AddToggle(events, "Objects appearing and disappearing", "Adds a marker on frames where recorded objects appear or are destroyed. Repeats on consecutive frames collapse into one marker. Takes effect at the next Play Mode.",
                settings.MarkObjectLifecycle, v => settings.MarkObjectLifecycle = v);
            AddToggle(events, "Scene loads", "Adds a marker when a scene loads.", settings.MarkSceneLoads, v => settings.MarkSceneLoads = v);
            AddToggle(events, "Collapse repeated messages", "A message repeated on consecutive frames becomes one marker with a count.",
                settings.CollapseRepeats, v => settings.CollapseRepeats = v);

            var view = Section(page, "Rewinding and Scene view", Glyphs.Trail);
            AddToggle(view, "Hide objects not spawned yet", "When rewinding, objects that did not exist at that frame are hidden until you go live again.",
                settings.HideObjectsNotYetSpawned, v => settings.HideObjectsNotYetSpawned = v);
            AddToggle(view, "Motion trail", "Draws the path of the selected objects in the Scene view.",
                settings.ShowMotionTrail, v => settings.ShowMotionTrail = v);
            AddToggle(view, "Ghosts of destroyed objects", "When rewinding, marks where destroyed objects were at that frame.",
                settings.ShowDestroyedGhosts, v => settings.ShowDestroyedGhosts = v);
            AddToggle(view, "Scene view overlay", "Shows the Hindsight overlay with the recording status and frame controls.",
                settings.ShowSceneOverlay, v => settings.ShowSceneOverlay = v);

            Pins(Section(page, "Pinned fields", Glyphs.Pin), settings);
            page.Add(Footer(settings));
        }

        private static VisualElement Header()
        {
            var header = new VisualElement();
            header.AddToClassList("hs-prefs__header");
            header.Add(Glyphs.Icon(Glyphs.Logo, "hs-icon--brand", "hs-icon--title"));
            var titles = new VisualElement();
            var title = new Label("Hindsight");
            title.AddToClassList("hs-title");
            var subtitle = new Label("Rewind debugger. Settings are saved per user in UserSettings/HindsightSettings.asset.");
            subtitle.AddToClassList("hs-prefs__subtitle");
            titles.Add(title);
            titles.Add(subtitle);
            header.Add(titles);
            return header;
        }

        private static VisualElement Section(VisualElement page, string title, string glyph)
        {
            var section = new VisualElement();
            section.AddToClassList("hs-prefs__section");
            var header = new VisualElement();
            header.AddToClassList("hs-section__header");
            header.Add(Glyphs.Icon(glyph, "hs-icon--brand", "hs-icon--small"));
            var label = new Label(title);
            label.AddToClassList("hs-section__title");
            header.Add(label);
            section.Add(header);
            page.Add(section);
            return section;
        }

        #endregion


        #region Fields

        private static void AddToggle(VisualElement parent, string label, string help, bool value, Action<bool> set)
        {
            var field = new Toggle(label) { value = value };
            field.RegisterValueChangedCallback(evt => set(evt.newValue));
            AddField(parent, field, help);
        }

        private static void AddSlider(VisualElement parent, string label, string help, float value, float min, float max, Action<float> set)
        {
            var field = new Slider(label, min, max) { value = value, showInputField = true };
            field.RegisterValueChangedCallback(evt => set(evt.newValue));
            AddField(parent, field, help);
        }

        private static void AddInteger(VisualElement parent, string label, string help, Func<int> get, Action<int> set)
        {
            var field = new IntegerField(label) { value = get(), isDelayed = true };
            field.RegisterValueChangedCallback(evt =>
            {
                set(evt.newValue);
                field.SetValueWithoutNotify(get());
            });
            AddField(parent, field, help);
        }

        private static void AddEnum(VisualElement parent, string label, string help, Enum value, Action<Enum> set)
        {
            var field = new EnumField(label, value);
            field.RegisterValueChangedCallback(evt => set(evt.newValue));
            AddField(parent, field, help);
        }

        private static void Layers(VisualElement parent, HindsightSettings settings)
        {
            var field = new LayerMaskField("Layers", settings.LayerMask);
            field.RegisterValueChangedCallback(evt => settings.LayerMask = evt.newValue);
            AddField(parent, field, "Only objects on these layers are recorded.");
        }

        private static void AddField(VisualElement parent, VisualElement field, string help)
        {
            var row = new VisualElement();
            row.AddToClassList("hs-prefs__field");
            field.AddToClassList("hs-prefs__control");
            field.AddToClassList(BaseField<bool>.alignedFieldUssClassName);
            row.Add(field);
            var hint = new Label(help);
            hint.AddToClassList("hs-prefs__help");
            row.Add(hint);
            parent.Add(row);
        }

        private static void Pins(VisualElement section, HindsightSettings settings)
        {
            var list = new VisualElement();
            section.Add(list);

            void Rebuild()
            {
                list.Clear();
                if (settings.PinCount == 0)
                {
                    var hint = new Label("No pinned field. Right-click a field of one of your scripts in the Inspector and choose Track in Hindsight, or mark it with [HindsightTrack] in code.");
                    hint.AddToClassList("hs-prefs__help");
                    list.Add(hint);
                    return;
                }

                for (var i = 0; i < settings.PinCount; i++)
                {
                    var pin = settings.GetPin(i);
                    var row = new VisualElement();
                    row.AddToClassList("hs-pin");
                    row.Add(Glyphs.Icon(Glyphs.Pin, "hs-icon--brand", "hs-icon--small"));
                    var text = new Label($"{ShortName(pin.TypeName)}.{pin.Path}") { tooltip = pin.TypeName };
                    text.AddToClassList("hs-pin__label");
                    row.Add(text);
                    var remove = new Button(() => { settings.RemovePin(pin.TypeName, pin.Path); Rebuild(); }) { tooltip = "Stop tracking this field" };
                    remove.AddToClassList("hs-icon-button");
                    remove.Add(Glyphs.Icon(Glyphs.Remove));
                    row.Add(remove);
                    list.Add(row);
                }
            }

            Rebuild();
        }

        private static VisualElement Footer(HindsightSettings settings)
        {
            var footer = new VisualElement();
            footer.AddToClassList("hs-prefs__footer");
            var shortcuts = new Button(() => EditorApplication.ExecuteMenuItem("Edit/Shortcuts...")) { text = "Edit shortcuts" };
            shortcuts.AddToClassList("hs-button");
            var reset = new Button(() =>
            {
                if (!EditorUtility.DisplayDialog("Reset Hindsight preferences", "Restore every option to its default value? Pinned fields are kept.", "Reset", "Cancel"))
                    return;
                settings.ResetToDefaults();
                SettingsService.NotifySettingsProviderChanged();
            }) { text = "Reset to defaults" };
            reset.AddToClassList("hs-button");
            footer.Add(shortcuts);
            footer.Add(reset);
            return footer;
        }

        private static string ShortName(string typeName)
        {
            var dot = typeName.LastIndexOf('.');
            return dot < 0 ? typeName : typeName.Substring(dot + 1);
        }

        #endregion
    }
}
