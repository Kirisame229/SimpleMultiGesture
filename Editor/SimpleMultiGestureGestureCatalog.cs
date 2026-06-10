namespace SimpleMultiGestureTool.Editor
{
    internal static class SimpleMultiGestureGestureCatalog
    {
        private static readonly string[] Names =
        {
            "Idle",
            "Fist",
            "Open",
            "Point",
            "Peace",
            "RockNRoll",
            "Gun",
            "Thumbs up"
        };

        internal static string Name(int gesture)
        {
            return gesture >= 0 && gesture < Names.Length
                ? Names[gesture]
                : gesture.ToString();
        }
    }
}
