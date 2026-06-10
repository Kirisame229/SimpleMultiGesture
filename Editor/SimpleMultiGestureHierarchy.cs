using VRC.SDK3.Avatars.Components;

namespace SimpleMultiGestureTool.Editor
{
    internal static class SimpleMultiGestureHierarchy
    {
        internal static VRCAvatarDescriptor FindAvatarDescriptor(SimpleMultiGesture component)
        {
            return component != null
                ? component.GetComponentInParent<VRCAvatarDescriptor>(true)
                : null;
        }

        internal static bool BelongsToAvatar(
            SimpleMultiGesture component,
            VRCAvatarDescriptor descriptor)
        {
            return component != null
                   && descriptor != null
                   && FindAvatarDescriptor(component) == descriptor;
        }

        internal static bool IsBuildEnabled(
            SimpleMultiGesture component,
            VRCAvatarDescriptor descriptor)
        {
            if (!BelongsToAvatar(component, descriptor) || !component.enabled)
            {
                return false;
            }

            var current = component.transform;
            while (current != null)
            {
                if (!current.gameObject.activeSelf || current.CompareTag("EditorOnly"))
                {
                    return false;
                }

                if (current == descriptor.transform)
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }
    }
}
