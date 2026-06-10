using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using Object = UnityEngine.Object;

namespace SimpleMultiGestureTool.Editor
{
    internal static class SimpleMultiGestureSetupMenu
    {
        private const string MenuPath = "GameObject/SimpleMultiGesture/Setup";

        [MenuItem(MenuPath, false, 10)]
        private static void Setup()
        {
            var selectedObjects = Selection.gameObjects;
            var descriptors = new HashSet<VRCAvatarDescriptor>();

            foreach (var selectedObject in selectedObjects)
            {
                var descriptor =
                    selectedObject.GetComponentInParent<VRCAvatarDescriptor>(true);
                if (descriptor == null)
                {
                    Debug.LogWarning(
                        SimpleMultiGestureLocalization.Format(
                            "setupNoDescriptor",
                            selectedObject.name),
                        selectedObject);
                    continue;
                }

                descriptors.Add(descriptor);
            }

            var undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(SimpleMultiGestureLocalization.Text("setupUndo"));
            var createdObjects = new List<Object>();

            foreach (var descriptor in descriptors)
            {
                var existing = descriptor
                    .GetComponentsInChildren<SimpleMultiGesture>(true)
                    .FirstOrDefault(component =>
                        SimpleMultiGestureHierarchy.BelongsToAvatar(component, descriptor));
                if (existing != null)
                {
                    Debug.LogWarning(
                        SimpleMultiGestureLocalization.Format(
                            "setupAlreadyExists",
                            descriptor.name),
                        descriptor);
                    continue;
                }

                var setupObject = new GameObject("SimpleMultiGesture");
                Undo.RegisterCreatedObjectUndo(
                    setupObject,
                    SimpleMultiGestureLocalization.Text("setupUndo"));
                Undo.SetTransformParent(
                    setupObject.transform,
                    descriptor.transform,
                    SimpleMultiGestureLocalization.Text("setupUndo"));
                setupObject.transform.localPosition = Vector3.zero;
                setupObject.transform.localRotation = Quaternion.identity;
                setupObject.transform.localScale = Vector3.one;
                Undo.AddComponent<SimpleMultiGesture>(setupObject);
                createdObjects.Add(setupObject);
            }

            Undo.CollapseUndoOperations(undoGroup);
            if (createdObjects.Count > 0)
            {
                Selection.objects = createdObjects.ToArray();
            }
        }

        [MenuItem(MenuPath, true)]
        private static bool ValidateSetup()
        {
            return Selection.gameObjects.Length > 0;
        }
    }
}
