using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SimpleMultiGestureTool.Editor
{
    internal enum SimpleMultiGestureClipIssueSeverity
    {
        Warning,
        Error
    }

    internal sealed class SimpleMultiGestureClipIssue
    {
        internal SimpleMultiGestureClipIssueSeverity Severity { get; }
        internal string Key { get; }
        internal object[] Arguments { get; }

        internal SimpleMultiGestureClipIssue(
            SimpleMultiGestureClipIssueSeverity severity,
            string key,
            params object[] arguments)
        {
            Severity = severity;
            Key = key;
            Arguments = arguments ?? Array.Empty<object>();
        }

        internal string Message => SimpleMultiGestureLocalization.Format(Key, Arguments);
    }

    internal static class SimpleMultiGestureClipValidator
    {
        private const string BlendShapePrefix = "blendShape.";
        private static readonly Dictionary<(int Clip, int Avatar), List<SimpleMultiGestureClipIssue>>
            Cache =
                new Dictionary<(int Clip, int Avatar), List<SimpleMultiGestureClipIssue>>();

        static SimpleMultiGestureClipValidator()
        {
            EditorApplication.projectChanged += ClearCache;
            EditorApplication.hierarchyChanged += ClearCache;
        }

        internal static List<SimpleMultiGestureClipIssue> Analyze(
            AnimationClip clip,
            GameObject avatarRoot)
        {
            if (clip == null)
            {
                return new List<SimpleMultiGestureClipIssue>();
            }

            var cacheKey = (
                clip.GetInstanceID(),
                avatarRoot != null ? avatarRoot.GetInstanceID() : 0);
            if (Cache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }

            var issues = new List<SimpleMultiGestureClipIssue>();
            if (clip.legacy)
            {
                issues.Add(new SimpleMultiGestureClipIssue(
                    SimpleMultiGestureClipIssueSeverity.Error,
                    "clipIssueLegacy"));
            }

            if (AnimationUtility.GetAnimationEvents(clip).Length > 0)
            {
                issues.Add(new SimpleMultiGestureClipIssue(
                    SimpleMultiGestureClipIssueSeverity.Warning,
                    "clipIssueAnimationEvent"));
            }

            var issueKeys = new HashSet<string>();
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                AnalyzeBinding(binding, avatarRoot, issues, issueKeys);
            }

            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                AnalyzeBinding(binding, avatarRoot, issues, issueKeys);
            }

            Cache[cacheKey] = issues;
            return issues;
        }

        private static void ClearCache()
        {
            Cache.Clear();
        }

        private static void AnalyzeBinding(
            EditorCurveBinding binding,
            GameObject avatarRoot,
            ICollection<SimpleMultiGestureClipIssue> issues,
            ISet<string> issueKeys)
        {
            if (avatarRoot == null)
            {
                return;
            }

            var target = FindTarget(avatarRoot.transform, binding.path);
            if (target == null)
            {
                AddOnce(
                    issues,
                    issueKeys,
                    "path:" + binding.path,
                    SimpleMultiGestureClipIssueSeverity.Warning,
                    "clipIssueMissingPath",
                    string.IsNullOrEmpty(binding.path) ? "/" : binding.path);
                return;
            }

            if (!HasRequiredComponent(target, binding.type))
            {
                AddOnce(
                    issues,
                    issueKeys,
                    "component:" + binding.path + ":" + binding.type,
                    SimpleMultiGestureClipIssueSeverity.Warning,
                    "clipIssueMissingComponent",
                    string.IsNullOrEmpty(binding.path) ? "/" : binding.path,
                    binding.type != null ? binding.type.Name : "Unknown");
                return;
            }

            if (IsBlendShapeBinding(binding))
            {
                var renderer = target.GetComponent<SkinnedMeshRenderer>();
                var blendShape = binding.propertyName.Substring(BlendShapePrefix.Length);
                if (renderer?.sharedMesh == null
                    || renderer.sharedMesh.GetBlendShapeIndex(blendShape) < 0)
                {
                    AddOnce(
                        issues,
                        issueKeys,
                        "blendshape:" + binding.path + ":" + blendShape,
                        SimpleMultiGestureClipIssueSeverity.Warning,
                        "clipIssueMissingBlendShape",
                        string.IsNullOrEmpty(binding.path) ? "/" : binding.path,
                        blendShape);
                }

                return;
            }

            if (IsAllowedBinding(binding))
            {
                return;
            }

            AddOnce(
                issues,
                issueKeys,
                "binding:" + binding.path + ":" + binding.propertyName + ":" + binding.type,
                SimpleMultiGestureClipIssueSeverity.Warning,
                "clipIssueUnsupportedBinding",
                string.IsNullOrEmpty(binding.path) ? "/" : binding.path,
                binding.propertyName);
        }

        private static Transform FindTarget(Transform avatarRoot, string path)
        {
            return string.IsNullOrEmpty(path) ? avatarRoot : avatarRoot.Find(path);
        }

        private static bool HasRequiredComponent(Transform target, Type bindingType)
        {
            if (bindingType == null
                || bindingType == typeof(GameObject)
                || bindingType == typeof(Transform))
            {
                return true;
            }

            return target.GetComponent(bindingType) != null;
        }

        private static bool IsAllowedBinding(EditorCurveBinding binding)
        {
            if (binding.type == typeof(GameObject))
            {
                return binding.propertyName == "m_IsActive";
            }

            if (binding.type == typeof(Transform))
            {
                return IsTransformPositionOrRotation(binding.propertyName);
            }

            // Animator bindings include humanoid muscles and root motion curves.
            if (binding.type == typeof(Animator))
            {
                return true;
            }

            return false;
        }

        private static bool IsTransformPositionOrRotation(string propertyName)
        {
            return propertyName.StartsWith("m_LocalPosition.", StringComparison.Ordinal)
                   || propertyName.StartsWith("m_LocalRotation.", StringComparison.Ordinal)
                   || propertyName.StartsWith("localEulerAngles", StringComparison.Ordinal)
                   || propertyName.StartsWith("m_LocalEulerAnglesHint.", StringComparison.Ordinal);
        }

        private static bool IsBlendShapeBinding(EditorCurveBinding binding)
        {
            return binding.type == typeof(SkinnedMeshRenderer)
                   && binding.propertyName.StartsWith(BlendShapePrefix, StringComparison.Ordinal);
        }

        private static void AddOnce(
            ICollection<SimpleMultiGestureClipIssue> issues,
            ISet<string> issueKeys,
            string issueKey,
            SimpleMultiGestureClipIssueSeverity severity,
            string localizationKey,
            params object[] arguments)
        {
            if (!issueKeys.Add(issueKey))
            {
                return;
            }

            issues.Add(new SimpleMultiGestureClipIssue(severity, localizationKey, arguments));
        }
    }
}
