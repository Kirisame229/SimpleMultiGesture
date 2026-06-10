using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace SimpleMultiGestureTool.Editor
{
    [CustomEditor(typeof(SimpleMultiGesture))]
    internal sealed class SimpleMultiGestureEditor : UnityEditor.Editor
    {
        private const float GestureLabelWidth = 126f;
        private const float CombinationNameWidth = 150f;
        private const float DeleteButtonWidth = 54f;
        private const float GestureSelectorSpacing = 5f;
        private const float MajorSectionSpacing = 20f;
        private const float TargetAvatarSpacing = 10f;
        private const string AdvancedFoldoutKeyPrefix = "me.kirisame.smg.advanced.";

        private SerializedProperty _defaultClips;
        private SerializedProperty _combinations;
        private SerializedProperty _writeDefaults;
        private SerializedProperty _transitionDuration;
        private SerializedProperty _transitionOffset;

        private int _selectedLeft = SimpleMultiGesture.FirstCombinationLeftGesture;
        private int _selectedRight;
        private bool _showAdvanced;

        private GUIStyle _miniIssueStyle;

        private void OnEnable()
        {
            _defaultClips = serializedObject.FindProperty(nameof(SimpleMultiGesture.defaultClips));
            _combinations = serializedObject.FindProperty(nameof(SimpleMultiGesture.combinations));
            _writeDefaults = serializedObject.FindProperty(nameof(SimpleMultiGesture.writeDefaults));
            _transitionDuration =
                serializedObject.FindProperty(nameof(SimpleMultiGesture.transitionDuration));
            _transitionOffset =
                serializedObject.FindProperty(nameof(SimpleMultiGesture.transitionOffset));
            _showAdvanced = EditorPrefs.GetBool(AdvancedFoldoutKey(), false);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var component = (SimpleMultiGesture)target;
            var descriptor = SimpleMultiGestureHierarchy.FindAvatarDescriptor(component);
            if (descriptor == null)
            {
                EditorGUILayout.HelpBox(
                    SimpleMultiGestureLocalization.Text("invalidAvatarDescriptor"),
                    MessageType.Warning);
                serializedObject.ApplyModifiedProperties();
                return;
            }

            EnsureStyles();

            EditorGUILayout.Space(2f);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField(
                    SimpleMultiGestureLocalization.Label("targetAvatar"),
                    descriptor,
                    typeof(VRCAvatarDescriptor),
                    true);
            }

            if (CountActiveConfigurations(descriptor) > 1)
            {
                EditorGUILayout.HelpBox(
                    SimpleMultiGestureLocalization.Text("duplicateComponentInspector"),
                    MessageType.Error);
            }

            EditorGUILayout.Space(TargetAvatarSpacing);
            DrawDefaultGestures(descriptor.gameObject);

            EditorGUILayout.Space(MajorSectionSpacing);
            DrawCombinations(descriptor.gameObject);

            EditorGUILayout.Space(MajorSectionSpacing);
            DrawAdvancedOptions();

            EditorGUILayout.Space(MajorSectionSpacing);
            DrawLanguage();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawDefaultGestures(GameObject avatarRoot)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(
                    SimpleMultiGestureLocalization.Text("defaultGestures"),
                    EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(
                        SimpleMultiGestureLocalization.Text("clearDefaultGestures"),
                        EditorStyles.miniButton))
                {
                    ClearDefaultGestures();
                }
            }

            for (var gesture = 0; gesture < SimpleMultiGesture.GestureCount; gesture++)
            {
                var clipProperty = _defaultClips.GetArrayElementAtIndex(gesture);
                var content = new GUIContent(
                    SimpleMultiGestureGestureCatalog.Name(gesture),
                    SimpleMultiGestureGestureCatalog.Name(gesture));

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(content, GUILayout.Width(GestureLabelWidth));
                    EditorGUILayout.PropertyField(clipProperty, GUIContent.none);
                }

                DrawClipIssues(clipProperty.objectReferenceValue as AnimationClip, avatarRoot);
            }
        }

        private void DrawCombinations(GameObject avatarRoot)
        {
            EditorGUILayout.LabelField(
                SimpleMultiGestureLocalization.Text("combinations"),
                EditorStyles.boldLabel);

            DrawGestureSelector(
                SimpleMultiGestureLocalization.Text("gestureLeft"),
                SimpleMultiGesture.FirstCombinationLeftGesture,
                SimpleMultiGesture.LastGesture,
                ref _selectedLeft);
            EditorGUILayout.Space(GestureSelectorSpacing);
            DrawGestureSelector(
                SimpleMultiGestureLocalization.Text("gestureRight"),
                0,
                SimpleMultiGesture.LastGesture,
                ref _selectedRight);

            var component = (SimpleMultiGesture)target;
            var isRegistered = FindLastRegisteredCombinationIndex(
                component,
                _selectedLeft,
                _selectedRight) >= 0;

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(
                    SimpleMultiGestureLocalization.Text("selectedCombination")
                    + ": "
                    + CombinationName(_selectedLeft, _selectedRight));
                using (new EditorGUI.DisabledScope(isRegistered))
                {
                    if (GUILayout.Button(
                            SimpleMultiGestureLocalization.Text("registerCombination"),
                            GUILayout.ExpandWidth(false)))
                    {
                        RegisterCombination(_selectedLeft, _selectedRight);
                    }
                }
            }

            EditorGUILayout.Space(MajorSectionSpacing);
            EditorGUILayout.LabelField(
                SimpleMultiGestureLocalization.Format(
                    "registeredCombinations",
                    CountRegisteredCombinations(component)),
                EditorStyles.boldLabel);

            DrawRegisteredCombinationList(component, avatarRoot);
        }

        private static void DrawGestureSelector(
            string label,
            int firstGesture,
            int lastGesture,
            ref int selectedGesture)
        {
            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);

            var names = Enumerable
                .Range(firstGesture, lastGesture - firstGesture + 1)
                .Select(SimpleMultiGestureGestureCatalog.Name)
                .ToArray();
            var selectedIndex = Mathf.Clamp(
                selectedGesture - firstGesture,
                0,
                names.Length - 1);
            selectedIndex = GUILayout.SelectionGrid(selectedIndex, names, 4);
            selectedGesture = selectedIndex + firstGesture;
        }

        private void DrawRegisteredCombinationList(
            SimpleMultiGesture component,
            GameObject avatarRoot)
        {
            var registeredIndices = Enumerable
                .Range(0, component.combinations.Count)
                .Where(index =>
                    component.combinations[index] != null
                    && component.combinations[index].registered)
                .OrderBy(index => component.combinations[index].leftGesture)
                .ThenBy(index => component.combinations[index].rightGesture)
                .ToArray();

            foreach (var index in registeredIndices)
            {
                var combination = component.combinations[index];
                var entry = _combinations.GetArrayElementAtIndex(index);
                var clipProperty = entry.FindPropertyRelative(
                    nameof(SimpleMultiGestureCombination.animationClip));

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        var isSelected =
                            combination.leftGesture == _selectedLeft
                            && combination.rightGesture == _selectedRight;
                        var previousBackground = GUI.backgroundColor;
                        if (isSelected)
                        {
                            GUI.backgroundColor = new Color(0.55f, 0.8f, 1f);
                        }

                        var selectClicked = GUILayout.Button(
                            CombinationName(
                                combination.leftGesture,
                                combination.rightGesture),
                            EditorStyles.miniButton,
                            GUILayout.Width(CombinationNameWidth));
                        GUI.backgroundColor = previousBackground;
                        if (selectClicked)
                        {
                            _selectedLeft = combination.leftGesture;
                            _selectedRight = combination.rightGesture;
                            GUI.FocusControl(null);
                        }

                        EditorGUILayout.PropertyField(
                            clipProperty,
                            GUIContent.none,
                            GUILayout.MinWidth(80f));

                        if (GUILayout.Button(
                                SimpleMultiGestureLocalization.Text("deleteCombination"),
                                EditorStyles.miniButton,
                                GUILayout.Width(DeleteButtonWidth)))
                        {
                            DeleteCombination(
                                combination.leftGesture,
                                combination.rightGesture);
                            GUIUtility.ExitGUI();
                        }
                    }

                    DrawClipIssues(
                        clipProperty.objectReferenceValue as AnimationClip,
                        avatarRoot,
                        0f);
                }
            }

            if (registeredIndices.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    SimpleMultiGestureLocalization.Text("noRegisteredCombinations"),
                    MessageType.Info);
            }
        }

        private void DrawAdvancedOptions()
        {
            var previous = _showAdvanced;
            _showAdvanced = EditorGUILayout.Foldout(
                _showAdvanced,
                SimpleMultiGestureLocalization.Text("advancedOptions"),
                true);
            if (previous != _showAdvanced)
            {
                EditorPrefs.SetBool(AdvancedFoldoutKey(), _showAdvanced);
            }

            if (!_showAdvanced)
            {
                return;
            }

            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(
                _writeDefaults,
                SimpleMultiGestureLocalization.Label("writeDefaults"));

            _transitionDuration.floatValue = Mathf.Max(
                0f,
                EditorGUILayout.FloatField(
                    SimpleMultiGestureLocalization.Text("transitionDuration"),
                    _transitionDuration.floatValue));

            _transitionOffset.floatValue = EditorGUILayout.Slider(
                SimpleMultiGestureLocalization.Text("transitionOffset"),
                _transitionOffset.floatValue,
                0f,
                1f);
            EditorGUI.indentLevel--;
        }

        private void DrawLanguage()
        {
            var language = SimpleMultiGestureLocalization.CurrentLanguage;
            var labels = new[]
            {
                SimpleMultiGestureLocalization.Text(language, "languageJapanese"),
                SimpleMultiGestureLocalization.Text(language, "languageKorean"),
                SimpleMultiGestureLocalization.Text(language, "languageEnglish")
            };

            var selected = EditorGUILayout.Popup(
                SimpleMultiGestureLocalization.Text(language, "language"),
                (int)language,
                labels);
            if (selected == (int)language)
            {
                return;
            }

            SimpleMultiGestureLocalization.CurrentLanguage =
                (SimpleMultiGestureLanguage)selected;
            InternalEditorUtility.RepaintAllViews();
        }

        private void DrawClipIssues(
            AnimationClip clip,
            GameObject avatarRoot,
            float leftPadding = GestureLabelWidth)
        {
            if (clip == null)
            {
                return;
            }

            var issues = SimpleMultiGestureClipValidator.Analyze(clip, avatarRoot);
            foreach (var issue in issues)
            {
                var iconName = issue.Severity == SimpleMultiGestureClipIssueSeverity.Error
                    ? "console.erroricon.sml"
                    : "console.warnicon.sml";
                var icon = EditorGUIUtility.IconContent(iconName);

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(leftPadding);
                    GUILayout.Label(icon, GUILayout.Width(16f), GUILayout.Height(16f));

                    var previousColor = _miniIssueStyle.normal.textColor;
                    _miniIssueStyle.normal.textColor =
                        issue.Severity == SimpleMultiGestureClipIssueSeverity.Error
                            ? new Color(1f, 0.45f, 0.45f)
                            : new Color(1f, 0.75f, 0.25f);
                    GUILayout.Label(issue.Message, _miniIssueStyle);
                    _miniIssueStyle.normal.textColor = previousColor;
                }
            }
        }

        private void RegisterCombination(int left, int right)
        {
            serializedObject.ApplyModifiedProperties();

            var component = (SimpleMultiGesture)target;
            Undo.RecordObject(
                component,
                SimpleMultiGestureLocalization.Text("editCombinationUndo"));
            component.combinations.Add(new SimpleMultiGestureCombination
            {
                leftGesture = left,
                rightGesture = right,
                registered = true,
                animationClip = null
            });
            EditorUtility.SetDirty(component);
            serializedObject.Update();
        }

        private void DeleteCombination(int left, int right)
        {
            serializedObject.ApplyModifiedProperties();

            var component = (SimpleMultiGesture)target;
            Undo.RecordObject(
                component,
                SimpleMultiGestureLocalization.Text("deleteCombinationUndo"));
            component.combinations.RemoveAll(
                combination => combination != null
                               && combination.leftGesture == left
                               && combination.rightGesture == right);
            EditorUtility.SetDirty(component);
            serializedObject.Update();
        }

        private void ClearDefaultGestures()
        {
            serializedObject.ApplyModifiedProperties();

            var component = (SimpleMultiGesture)target;
            Undo.RecordObject(
                component,
                SimpleMultiGestureLocalization.Text("clearDefaultGesturesUndo"));
            for (var index = 0; index < component.defaultClips.Length; index++)
            {
                component.defaultClips[index] = null;
            }

            EditorUtility.SetDirty(component);
            serializedObject.Update();
        }

        private static int FindLastRegisteredCombinationIndex(
            SimpleMultiGesture component,
            int left,
            int right)
        {
            var result = -1;
            if (component.combinations == null)
            {
                return result;
            }

            for (var index = 0; index < component.combinations.Count; index++)
            {
                var combination = component.combinations[index];
                if (combination == null
                    || combination.leftGesture != left
                    || combination.rightGesture != right)
                {
                    continue;
                }

                result = combination.registered ? index : -1;
            }

            return result;
        }

        private static int CountRegisteredCombinations(SimpleMultiGesture component)
        {
            return component.combinations.Count(combination =>
                combination != null && combination.registered);
        }

        private static string CombinationName(int left, int right)
        {
            return SimpleMultiGestureGestureCatalog.Name(left)
                   + " + "
                   + SimpleMultiGestureGestureCatalog.Name(right);
        }

        private static int CountActiveConfigurations(VRCAvatarDescriptor descriptor)
        {
            return descriptor
                .GetComponentsInChildren<SimpleMultiGesture>(true)
                .Count(component =>
                    SimpleMultiGestureHierarchy.IsBuildEnabled(component, descriptor));
        }

        private void EnsureStyles()
        {
            if (_miniIssueStyle == null)
            {
                _miniIssueStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    wordWrap = true
                };
            }
        }

        private string AdvancedFoldoutKey()
        {
            var id = target != null
                ? GlobalObjectId.GetGlobalObjectIdSlow(target).ToString()
                : "unknown";
            return AdvancedFoldoutKeyPrefix + id;
        }
    }
}
