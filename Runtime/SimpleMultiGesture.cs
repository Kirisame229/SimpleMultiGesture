using System;
using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase;

namespace SimpleMultiGestureTool
{
    [Serializable]
    public sealed class SimpleMultiGestureCombination
    {
        public int leftGesture = 1;
        public int rightGesture;
        public bool registered = true;
        public AnimationClip animationClip;
    }

    [DisallowMultipleComponent]
    [AddComponentMenu("SimpleMultiGesture/SimpleMultiGesture")]
    public sealed class SimpleMultiGesture : MonoBehaviour, IEditorOnly
    {
        public const int GestureCount = 8;
        public const int FirstCombinationLeftGesture = 1;
        public const int LastGesture = 7;
        public const int DefaultLayerPriority = 229;
        public const float DefaultTransitionDuration = 0.1f;
        public const float DefaultTransitionOffset = 0f;

        public AnimationClip[] defaultClips = new AnimationClip[GestureCount];
        public List<SimpleMultiGestureCombination> combinations =
            new List<SimpleMultiGestureCombination>();

        public bool writeDefaults = true;
        public int layerPriority;
        public float transitionDuration = DefaultTransitionDuration;
        public float transitionOffset = DefaultTransitionOffset;

        private void Reset()
        {
            EnsureValidState();
        }

        private void OnValidate()
        {
            EnsureValidState();
        }

        public void EnsureValidState()
        {
            if (defaultClips == null || defaultClips.Length != GestureCount)
            {
                var resized = new AnimationClip[GestureCount];
                if (defaultClips != null)
                {
                    Array.Copy(defaultClips, resized, Mathf.Min(defaultClips.Length, resized.Length));
                }

                defaultClips = resized;
            }

            if (combinations == null)
            {
                combinations = new List<SimpleMultiGestureCombination>();
            }

            transitionDuration = Mathf.Max(0f, transitionDuration);
            transitionOffset = Mathf.Clamp01(transitionOffset);
        }
    }
}
