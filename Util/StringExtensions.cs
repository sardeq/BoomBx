public static class StringExtensions
{
    public static string TrimTo(this string input, int maxLength)
    {
        if (string.IsNullOrEmpty(input)) return input;
        return input.Length <= maxLength ? input : input[..maxLength] + "...";
    }
}