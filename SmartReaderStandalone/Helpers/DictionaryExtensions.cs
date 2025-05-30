namespace SmartReaderStandalone.Helpers
{
    public static class DictionaryExtensions
    {
        public static Dictionary<string, object?> AlsoIf(this Dictionary<string, object?> dict, bool condition, Action<Dictionary<string, object?>> action)
        {
            if (condition)
                action(dict);

            return dict;
        }
    }
}
