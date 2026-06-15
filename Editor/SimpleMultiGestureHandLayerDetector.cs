using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace SimpleMultiGestureTool.Editor
{
    internal sealed class SimpleMultiGestureHandLayerAnalysis
    {
        internal SimpleMultiGestureHandLayerAnalysis(
            bool canRemove,
            string messageKey,
            int leftLayerIndex = -1,
            int rightLayerIndex = -1)
        {
            CanRemove = canRemove;
            MessageKey = messageKey;
            LeftLayerIndex = leftLayerIndex;
            RightLayerIndex = rightLayerIndex;
        }

        internal bool CanRemove { get; }
        internal string MessageKey { get; }
        internal int LeftLayerIndex { get; }
        internal int RightLayerIndex { get; }
    }

    internal static class SimpleMultiGestureHandLayerDetector
    {
        internal const string LeftLayerName = "Left Hand";
        internal const string RightLayerName = "Right Hand";

        internal static SimpleMultiGestureHandLayerAnalysis Analyze(
            VRCAvatarDescriptor descriptor)
        {
            var controller = GetOriginalFxController(descriptor);
            if (controller == null)
            {
                return Failure("handLayersNoCustomFx");
            }

            var layers = controller.layers;
            var leftMatches = FindLayerIndices(layers, LeftLayerName);
            var rightMatches = FindLayerIndices(layers, RightLayerName);
            if (leftMatches.Length == 0 || rightMatches.Length == 0)
            {
                return Failure("handLayersMissing");
            }

            if (leftMatches.Length != 1 || rightMatches.Length != 1)
            {
                return Failure("handLayersDuplicate");
            }

            var leftIndex = leftMatches[0];
            var rightIndex = rightMatches[0];
            if (leftIndex == 0 || rightIndex == 0)
            {
                return Failure("handLayersBaseLayer");
            }

            if (IsSyncedOrReferenced(layers, leftIndex)
                || IsSyncedOrReferenced(layers, rightIndex))
            {
                return Failure("handLayersSynced");
            }

            if (!UsesParameter(layers[leftIndex], "GestureLeft")
                || !UsesParameter(layers[rightIndex], "GestureRight"))
            {
                return Failure("handLayersParameterMismatch");
            }

            return new SimpleMultiGestureHandLayerAnalysis(
                true,
                "handLayersDetected",
                leftIndex,
                rightIndex);
        }

        private static AnimatorController GetOriginalFxController(
            VRCAvatarDescriptor descriptor)
        {
            if (descriptor == null || descriptor.baseAnimationLayers == null)
            {
                return null;
            }

            foreach (var layer in descriptor.baseAnimationLayers)
            {
                if (layer.type != VRCAvatarDescriptor.AnimLayerType.FX
                    || layer.isDefault)
                {
                    continue;
                }

                return layer.animatorController as AnimatorController;
            }

            return null;
        }

        private static int[] FindLayerIndices(
            IReadOnlyList<AnimatorControllerLayer> layers,
            string name)
        {
            return layers
                .Select((layer, index) => (layer, index))
                .Where(entry => string.Equals(
                    entry.layer.name,
                    name,
                    StringComparison.Ordinal))
                .Select(entry => entry.index)
                .ToArray();
        }

        private static bool IsSyncedOrReferenced(
            IReadOnlyList<AnimatorControllerLayer> layers,
            int targetIndex)
        {
            if (layers[targetIndex].syncedLayerIndex >= 0)
            {
                return true;
            }

            return layers
                .Select((layer, index) => (layer, index))
                .Any(entry =>
                    entry.index != targetIndex
                    && entry.layer.syncedLayerIndex == targetIndex);
        }

        private static bool UsesParameter(
            AnimatorControllerLayer layer,
            string parameter)
        {
            return layer.stateMachine != null
                   && UsesParameter(layer.stateMachine, parameter);
        }

        private static bool UsesParameter(
            AnimatorStateMachine stateMachine,
            string parameter)
        {
            if (TransitionsUseParameter(stateMachine.anyStateTransitions, parameter)
                || TransitionsUseParameter(stateMachine.entryTransitions, parameter))
            {
                return true;
            }

            foreach (var childState in stateMachine.states)
            {
                var state = childState.state;
                if (state == null)
                {
                    continue;
                }

                if (TransitionsUseParameter(state.transitions, parameter)
                    || StateUsesParameter(state, parameter)
                    || MotionUsesParameter(state.motion, parameter))
                {
                    return true;
                }
            }

            foreach (var childStateMachine in stateMachine.stateMachines)
            {
                if (childStateMachine.stateMachine != null
                    && UsesParameter(childStateMachine.stateMachine, parameter))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TransitionsUseParameter<T>(
            IEnumerable<T> transitions,
            string parameter)
            where T : AnimatorTransitionBase
        {
            return transitions.Any(transition =>
                transition != null
                && transition.conditions.Any(condition =>
                    string.Equals(
                        condition.parameter,
                        parameter,
                        StringComparison.Ordinal)));
        }

        private static bool StateUsesParameter(
            AnimatorState state,
            string parameter)
        {
            return state.speedParameterActive
                       && string.Equals(
                           state.speedParameter,
                           parameter,
                           StringComparison.Ordinal)
                   || state.mirrorParameterActive
                       && string.Equals(
                           state.mirrorParameter,
                           parameter,
                           StringComparison.Ordinal)
                   || state.cycleOffsetParameterActive
                       && string.Equals(
                           state.cycleOffsetParameter,
                           parameter,
                           StringComparison.Ordinal)
                   || state.timeParameterActive
                       && string.Equals(
                           state.timeParameter,
                           parameter,
                           StringComparison.Ordinal);
        }

        private static bool MotionUsesParameter(Motion motion, string parameter)
        {
            if (!(motion is BlendTree blendTree))
            {
                return false;
            }

            if (string.Equals(
                    blendTree.blendParameter,
                    parameter,
                    StringComparison.Ordinal)
                || string.Equals(
                    blendTree.blendParameterY,
                    parameter,
                    StringComparison.Ordinal))
            {
                return true;
            }

            return blendTree.children.Any(child =>
                string.Equals(
                    child.directBlendParameter,
                    parameter,
                    StringComparison.Ordinal)
                || MotionUsesParameter(child.motion, parameter));
        }

        private static SimpleMultiGestureHandLayerAnalysis Failure(string messageKey)
        {
            return new SimpleMultiGestureHandLayerAnalysis(false, messageKey);
        }
    }
}
