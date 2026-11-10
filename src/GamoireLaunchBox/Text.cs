namespace GamoireLaunchBox
{
    public static class Text
    {
        public static string Clip(string text, int max)
        {
            if (text.Length <= max) return text;
            var cut = char.IsHighSurrogate(text[max - 1]) ? max - 1 : max;
            return text.Substring(0, cut);
        }
    }
}
