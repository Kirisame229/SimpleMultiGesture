using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using nadena.dev.ndmf.fluent;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using Object = UnityEngine.Object;

[assembly: ExportsPlugin(typeof(SimpleMultiGestureTool.Editor.SimpleMultiGesturePlugin))]
[assembly: ExportsPlugin(typeof(SimpleMultiGestureTool.Editor.SimpleMultiGestureHandLayerRemovalPlugin))]

namespace SimpleMultiGestureTool.Editor
{
    [RunsOnPlatforms(WellKnownPlatforms.VRChatAvatar30)]
    internal sealed class SimpleMultiGestureHandLayerRemovalPlugin
        : Plugin<SimpleMultiGestureHandLayerRemovalPlugin>
    {
        public override string QualifiedName => "me.kirisame.smg.remove-hand-layers";
        public override string DisplayName => "SimpleMultiGesture Hand Layer Removal";

        protected override void Configure()
        {
            InPhase(BuildPhase.Transforming)
                .BeforePlugin("nadena.dev.modular-avatar")
                .WithRequiredExtension(typeof(AnimatorServicesContext), sequence =>
                {
                    sequence.Run(
                        "Disable original FX hand gesture layers",
                        RemoveOriginalHandGestureLayers);
                });
        }

        private static void RemoveOriginalHandGestureLayers(BuildContext context)
        {
            var avatarRoot = context.AvatarRootObject;
            var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null)
            {
                return;
            }

            var configurations = avatarRoot
                .GetComponentsInChildren<SimpleMultiGesture>(true)
                .Where(component =>
                    SimpleMultiGestureHierarchy.IsBuildEnabled(component, descriptor))
                .ToArray();
            if (configurations.Length != 1
                || !configurations[0].removeOriginalHandGestureLayers)
            {
                return;
            }

            var analysis = SimpleMultiGestureHandLayerDetector.Analyze(descriptor);
            if (!analysis.CanRemove)
            {
                SimpleMultiGesturePlugin.Report(
                    ErrorSeverity.NonFatal,
                    SimpleMultiGestureLocalization.Text(analysis.MessageKey));
                return;
            }

            var controllerContext =
                context.Extension<AnimatorServicesContext>().ControllerContext;
            if (!controllerContext.Controllers.TryGetValue(
                    VRCAvatarDescriptor.AnimLayerType.FX,
                    out var fxController)
                || fxController == null)
            {
                SimpleMultiGesturePlugin.Report(
                    ErrorSeverity.NonFatal,
                    SimpleMultiGestureLocalization.Text("handLayersVirtualMismatch"));
                return;
            }

            var targets = fxController.Layers
                .Where(layer =>
                    layer.IsOriginalLayer
                    && (layer.OriginalPhysicalLayerIndex == analysis.LeftLayerIndex
                        || layer.OriginalPhysicalLayerIndex == analysis.RightLayerIndex))
                .ToArray();
            var hasLeft = targets.Any(layer =>
                layer.OriginalPhysicalLayerIndex == analysis.LeftLayerIndex
                && string.Equals(
                    layer.Name,
                    SimpleMultiGestureHandLayerDetector.LeftLayerName,
                    StringComparison.Ordinal));
            var hasRight = targets.Any(layer =>
                layer.OriginalPhysicalLayerIndex == analysis.RightLayerIndex
                && string.Equals(
                    layer.Name,
                    SimpleMultiGestureHandLayerDetector.RightLayerName,
                    StringComparison.Ordinal));
            if (targets.Length != 2 || !hasLeft || !hasRight)
            {
                SimpleMultiGesturePlugin.Report(
                    ErrorSeverity.NonFatal,
                    SimpleMultiGestureLocalization.Text("handLayersVirtualMismatch"));
                return;
            }

            foreach (var layer in targets)
            {
                fxController.RemoveLayer(layer);
            }
        }
    }

    [RunsOnPlatforms(WellKnownPlatforms.VRChatAvatar30)]
    internal sealed class SimpleMultiGesturePlugin : Plugin<SimpleMultiGesturePlugin>
    {
        private const string LayerName = "SimpleMultiGesture FX";
        private const string GestureLeftParameter = "GestureLeft";
        private const string GestureRightParameter = "GestureRight";

        public override string QualifiedName => "me.kirisame.smg";
        public override string DisplayName => "SimpleMultiGesture";

        protected override void Configure()
        {
            InPhase(BuildPhase.Transforming)
                .AfterPlugin("nadena.dev.modular-avatar")
                .WithRequiredExtension(typeof(AnimatorServicesContext), sequence =>
                {
                    sequence.Run("Generate SimpleMultiGesture FX layer", Generate);
                });
        }

        private static void Generate(BuildContext context)
        {
            var avatarRoot = context.AvatarRootObject;
            var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            var allConfigurations =
                avatarRoot.GetComponentsInChildren<SimpleMultiGesture>(true);

            if (descriptor == null)
            {
                CleanupConfigurationObjects(avatarRoot, allConfigurations);
                return;
            }

            var activeConfigurations = allConfigurations
                .Where(component =>
                    SimpleMultiGestureHierarchy.IsBuildEnabled(component, descriptor))
                .ToArray();

            if (activeConfigurations.Length > 1)
            {
                Report(
                    ErrorSeverity.Error,
                    SimpleMultiGestureLocalization.Text("buildMultipleComponents"));
                CleanupConfigurationObjects(avatarRoot, allConfigurations);
                return;
            }

            if (activeConfigurations.Length == 0)
            {
                CleanupConfigurationObjects(avatarRoot, allConfigurations);
                return;
            }

            var configuration = activeConfigurations[0];
            configuration.EnsureValidState();

            if (UsesAnimatorOverrideController(descriptor))
            {
                Report(
                    ErrorSeverity.Error,
                    SimpleMultiGestureLocalization.Text("buildOverrideController"));
                CleanupConfigurationObjects(avatarRoot, allConfigurations);
                return;
            }

            var combinations = CollectEffectiveCombinations(configuration);
            if (!ValidateClips(configuration, combinations, avatarRoot))
            {
                CleanupConfigurationObjects(avatarRoot, allConfigurations);
                return;
            }

            var controllerContext =
                context.Extension<AnimatorServicesContext>().ControllerContext;
            if (!controllerContext.Controllers.TryGetValue(
                    VRCAvatarDescriptor.AnimLayerType.FX,
                    out var fxController))
            {
                fxController = VirtualAnimatorController.Create(
                    controllerContext.CloneContext,
                    "SimpleMultiGesture Generated FX");
                controllerContext.Controllers[VRCAvatarDescriptor.AnimLayerType.FX] =
                    fxController;
            }

            if (fxController == null)
            {
                Report(
                    ErrorSeverity.Error,
                    SimpleMultiGestureLocalization.Text("buildMissingFx"));
                CleanupConfigurationObjects(avatarRoot, allConfigurations);
                return;
            }

            if (fxController.Layers.Any(layer => layer.Name == LayerName))
            {
                Report(
                    ErrorSeverity.Error,
                    SimpleMultiGestureLocalization.Text("buildDuplicateLayer"));
                CleanupConfigurationObjects(avatarRoot, allConfigurations);
                return;
            }

            AddGestureParameters(fxController);
            AddExpressionLayer(
                controllerContext,
                fxController,
                configuration,
                combinations);

            CleanupConfigurationObjects(avatarRoot, allConfigurations);
        }

        private static Dictionary<(int Left, int Right), SimpleMultiGestureCombination>
            CollectEffectiveCombinations(SimpleMultiGesture configuration)
        {
            var result =
                new Dictionary<(int Left, int Right), SimpleMultiGestureCombination>();
            if (configuration.combinations == null)
            {
                return result;
            }

            foreach (var combination in configuration.combinations)
            {
                if (combination == null
                    || combination.leftGesture
                    < SimpleMultiGesture.FirstCombinationLeftGesture
                    || combination.leftGesture > SimpleMultiGesture.LastGesture
                    || combination.rightGesture < 0
                    || combination.rightGesture > SimpleMultiGesture.LastGesture)
                {
                    continue;
                }

                var key = (combination.leftGesture, combination.rightGesture);
                if (combination.registered)
                {
                    result[key] = combination;
                }
                else
                {
                    result.Remove(key);
                }
            }

            return result;
        }

        private static bool ValidateClips(
            SimpleMultiGesture configuration,
            IReadOnlyDictionary<(int Left, int Right), SimpleMultiGestureCombination>
                combinations,
            GameObject avatarRoot)
        {
            var clips = new HashSet<AnimationClip>();
            foreach (var clip in configuration.defaultClips)
            {
                if (clip != null)
                {
                    clips.Add(clip);
                }
            }

            foreach (var combination in combinations.Values)
            {
                if (combination.animationClip != null)
                {
                    clips.Add(combination.animationClip);
                }
            }

            var hasErrors = false;
            foreach (var clip in clips)
            {
                var issues = SimpleMultiGestureClipValidator.Analyze(clip, avatarRoot);
                foreach (var issue in issues)
                {
                    var isError =
                        issue.Severity == SimpleMultiGestureClipIssueSeverity.Error;
                    hasErrors |= isError;
                    var key = isError ? "buildClipError" : "buildClipWarning";
                    Report(
                        isError ? ErrorSeverity.Error : ErrorSeverity.NonFatal,
                        SimpleMultiGestureLocalization.Format(
                            key,
                            clip.name,
                            issue.Message));
                }
            }

            return !hasErrors;
        }

        private static bool UsesAnimatorOverrideController(
            VRCAvatarDescriptor descriptor)
        {
            if (descriptor.baseAnimationLayers == null)
            {
                return false;
            }

            return descriptor.baseAnimationLayers.Any(layer =>
                layer.type == VRCAvatarDescriptor.AnimLayerType.FX
                && !layer.isDefault
                && layer.animatorController is AnimatorOverrideController);
        }

        private static void AddGestureParameters(
            VirtualAnimatorController fxController)
        {
            fxController.Parameters = fxController.Parameters
                .SetItem(GestureLeftParameter, new AnimatorControllerParameter
                {
                    name = GestureLeftParameter,
                    type = AnimatorControllerParameterType.Int,
                    defaultInt = 0
                })
                .SetItem(GestureRightParameter, new AnimatorControllerParameter
                {
                    name = GestureRightParameter,
                    type = AnimatorControllerParameterType.Int,
                    defaultInt = 0
                });
        }

        private static void AddExpressionLayer(
            VirtualControllerContext controllerContext,
            VirtualAnimatorController fxController,
            SimpleMultiGesture configuration,
            IReadOnlyDictionary<(int Left, int Right), SimpleMultiGestureCombination>
                combinations)
        {
            var layer = fxController.AddLayer(new LayerPriority(228),LayerName);
            layer.DefaultWeight = 1f;
            layer.BlendingMode = AnimatorLayerBlendingMode.Override;
            layer.AvatarMask = null;
            layer.IKPass = false;

            var stateMachine = layer.StateMachine;
            stateMachine.Name = LayerName;
            stateMachine.AnyStatePosition = new Vector3(40f, 80f, 0f);
            stateMachine.EntryPosition = new Vector3(40f, 20f, 0f);

            var emptyClip = CreateEmptyClip();
            var sourceClipCache = new Dictionary<AnimationClip, VirtualClip>();
            var defaultStates = new Dictionary<int, VirtualState>();
            var combinationStates =
                new Dictionary<(int Left, int Right), VirtualState>();

            for (var right = 0; right < SimpleMultiGesture.GestureCount; right++)
            {
                var motion = GetMotion(
                    configuration.defaultClips[right],
                    emptyClip,
                    sourceClipCache);
                var state = stateMachine.AddState(
                    DefaultStateName(right),
                    motion,
                    new Vector3(280f + right * 190f, 40f, 0f));
                ConfigureState(state, configuration.writeDefaults);
                defaultStates[right] = state;
            }

            var orderedCombinations = combinations
                .OrderBy(entry => entry.Key.Right)
                .ThenBy(entry => entry.Key.Left)
                .ToArray();
            for (var index = 0; index < orderedCombinations.Length; index++)
            {
                var entry = orderedCombinations[index];
                var motion = GetMotion(
                    entry.Value.animationClip,
                    emptyClip,
                    sourceClipCache);
                var state = stateMachine.AddState(
                    CombinationStateName(entry.Key.Left, entry.Key.Right),
                    motion,
                    new Vector3(
                        280f + entry.Key.Right * 190f,
                        180f + entry.Key.Left * 90f,
                        0f));
                ConfigureState(state, configuration.writeDefaults);
                combinationStates[entry.Key] = state;
            }

            stateMachine.DefaultState = defaultStates[0];

            var transitions = new List<VirtualStateTransition>();
            foreach (var entry in orderedCombinations)
            {
                transitions.Add(CreateTransition(
                    combinationStates[entry.Key],
                    configuration,
                    CreateCondition(
                        GestureLeftParameter,
                        AnimatorConditionMode.Equals,
                        entry.Key.Left),
                    CreateCondition(
                        GestureRightParameter,
                        AnimatorConditionMode.Equals,
                        entry.Key.Right)));
            }

            for (var right = 0; right < SimpleMultiGesture.GestureCount; right++)
            {
                if (right == 0)
                {
                    AddIdleFallbackTransitions(
                        transitions,
                        defaultStates,
                        configuration,
                        combinations);
                    continue;
                }

                transitions.Add(CreateTransition(
                    defaultStates[right],
                    configuration,
                    CreateDefaultFallbackConditions(right, combinations)));
            }

            stateMachine.AnyStateTransitions = transitions.ToImmutableList();
        }

        private static void AddIdleFallbackTransitions(
            ICollection<VirtualStateTransition> transitions,
            IReadOnlyDictionary<int, VirtualState> defaultStates,
            SimpleMultiGesture configuration,
            IReadOnlyDictionary<(int Left, int Right), SimpleMultiGestureCombination>
                combinations)
        {
            for (var left = 0; left < SimpleMultiGesture.GestureCount; left++)
            {
                if (combinations.ContainsKey((left, 0)))
                {
                    continue;
                }

                // With the right hand idle, reuse the default right-hand animation
                // matching the active left gesture.
                transitions.Add(CreateTransition(
                    defaultStates[left],
                    configuration,
                    CreateCondition(
                        GestureLeftParameter,
                        AnimatorConditionMode.Equals,
                        left),
                    CreateCondition(
                        GestureRightParameter,
                        AnimatorConditionMode.Equals,
                        0)));
            }
        }

        private static AnimatorCondition[] CreateDefaultFallbackConditions(
            int right,
            IReadOnlyDictionary<(int Left, int Right), SimpleMultiGestureCombination>
                combinations)
        {
            var conditions = new List<AnimatorCondition>
            {
                CreateCondition(
                    GestureRightParameter,
                    AnimatorConditionMode.Equals,
                    right)
            };

            // Only registered combinations are excluded. Every other Left value falls back
            // to the matching default Right state.
            foreach (var left in combinations.Keys
                         .Where(key => key.Right == right)
                         .Select(key => key.Left)
                         .Distinct()
                         .OrderBy(value => value))
            {
                conditions.Add(CreateCondition(
                    GestureLeftParameter,
                    AnimatorConditionMode.NotEqual,
                    left));
            }

            return conditions.ToArray();
        }

        private static VirtualClip CreateEmptyClip()
        {
            var clip = VirtualClip.Create("SMG Empty");
            clip.FrameRate = 60f;
            clip.Legacy = false;
            clip.WrapMode = WrapMode.Once;

            var settings = clip.Settings;
            settings.loopTime = false;
            clip.Settings = settings;
            return clip;
        }

        private static VirtualClip GetMotion(
            AnimationClip source,
            VirtualClip emptyClip,
            IDictionary<AnimationClip, VirtualClip> sourceClipCache)
        {
            if (source == null)
            {
                return emptyClip;
            }

            if (!sourceClipCache.TryGetValue(source, out var motion))
            {
                // Marker clips are committed as the original read-only asset. This preserves
                // loop settings, object curves, humanoid curves, and Animation Events.
                motion = VirtualClip.FromMarker(source);
                sourceClipCache[source] = motion;
            }

            return motion;
        }

        private static void ConfigureState(VirtualState state, bool writeDefaults)
        {
            state.WriteDefaultValues = writeDefaults;
            state.Speed = 1f;
            state.Mirror = false;
            state.CycleOffset = 0f;
            state.IKOnFeet = false;
            state.SpeedParameter = null;
            state.TimeParameter = null;
            state.CycleOffsetParameter = null;
            state.MirrorParameter = null;
        }

        private static VirtualStateTransition CreateTransition(
            VirtualState destination,
            SimpleMultiGesture configuration,
            params AnimatorCondition[] conditions)
        {
            var transition = VirtualStateTransition.Create();
            transition.SetDestination(destination);
            transition.ExitTime = null;
            transition.HasFixedDuration = true;
            transition.Duration = Mathf.Max(0f, configuration.transitionDuration);
            transition.Offset = Mathf.Clamp01(configuration.transitionOffset);
            transition.InterruptionSource = TransitionInterruptionSource.None;
            transition.OrderedInterruption = true;
            transition.CanTransitionToSelf = false;
            transition.Conditions = conditions.ToImmutableList();
            return transition;
        }

        private static AnimatorCondition CreateCondition(
            string parameter,
            AnimatorConditionMode mode,
            int threshold)
        {
            return new AnimatorCondition
            {
                parameter = parameter,
                mode = mode,
                threshold = threshold
            };
        }

        private static string DefaultStateName(int right)
        {
            return "SMG_R" + right + "_" + SimpleMultiGestureGestureCatalog.Name(right);
        }

        private static string CombinationStateName(int left, int right)
        {
            return "SMG_L" + left + "_"
                   + SimpleMultiGestureGestureCatalog.Name(left)
                   + "_R" + right + "_"
                   + SimpleMultiGestureGestureCatalog.Name(right);
        }

        private static void CleanupConfigurationObjects(
            GameObject avatarRoot,
            IEnumerable<SimpleMultiGesture> configurations)
        {
            var components = configurations
                .Where(component => component != null)
                .ToArray();
            var objects = components
                .Select(component => component.gameObject)
                .Where(gameObject => gameObject != null && gameObject != avatarRoot)
                .Distinct()
                .OrderByDescending(GetHierarchyDepth)
                .ToArray();

            foreach (var gameObject in objects)
            {
                Object.DestroyImmediate(gameObject);
            }

            foreach (var component in components)
            {
                if (component != null && component.gameObject == avatarRoot)
                {
                    Object.DestroyImmediate(component);
                }
            }
        }

        private static int GetHierarchyDepth(GameObject gameObject)
        {
            var depth = 0;
            var current = gameObject.transform;
            while (current.parent != null)
            {
                depth++;
                current = current.parent;
            }

            return depth;
        }

        internal static void Report(ErrorSeverity severity, string message)
        {
            ErrorReport.ReportError(new SimpleMultiGestureBuildMessage(severity, message));
        }
    }

    internal sealed class SimpleMultiGestureBuildMessage : IError
    {
        private readonly string _message;

        internal SimpleMultiGestureBuildMessage(
            ErrorSeverity severity,
            string message)
        {
            Severity = severity;
            _message = message;
        }

        public ErrorSeverity Severity { get; }

        public VisualElement CreateVisualElement(ErrorReport report)
        {
            return new Label(_message)
            {
                style =
                {
                    whiteSpace = WhiteSpace.Normal,
                    marginTop = 4f,
                    marginBottom = 4f
                }
            };
        }

        public string ToMessage()
        {
            return _message;
        }

        public void AddReference(ObjectReference obj)
        {
        }
    }
}
