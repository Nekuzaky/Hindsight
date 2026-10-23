namespace Nekuzaky.Hindsight.Editor
{
    /// <summary>
    /// Marker texts for objects appearing and disappearing, built once, so recording never formats a string.
    /// Identical texts on consecutive frames collapse into one marker.
    /// </summary>
    internal static class LifecycleText
    {
        #region Private and Protected

        private const int Cached = 64;

        private static readonly string[] AppearedTexts = Build("appeared");
        private static readonly string[] DisappearedTexts = Build("disappeared");

        #endregion


        #region Main API

        public static string Appeared(int count) => Pick(AppearedTexts, count);

        public static string Disappeared(int count) => Pick(DisappearedTexts, count);

        #endregion


        #region Tools and Utilities

        private static string Pick(string[] texts, int count)
        {
            return count <= 0 ? texts[0] : texts[count < Cached ? count : Cached];
        }

        private static string[] Build(string verb)
        {
            var texts = new string[Cached + 1];
            texts[0] = $"Objects {verb}";
            texts[1] = $"1 object {verb}";
            for (var i = 2; i < Cached; i++)
                texts[i] = $"{i} objects {verb}";
            texts[Cached] = $"{Cached}+ objects {verb}";
            return texts;
        }

        #endregion
    }
}
