using System.Globalization;
using System.Windows;

namespace MuMiClick;

internal static class LocalizationService
{
    private const string ResourcePrefix = "/MuMiClick;component/Resources/Strings.";
    public static string CurrentLanguage { get; private set; } = "en-US";

    public static string Resolve(string? language)
    {
        if (string.Equals(language, "ko-KR", StringComparison.OrdinalIgnoreCase)) return "ko-KR";
        if (string.Equals(language, "en-US", StringComparison.OrdinalIgnoreCase)) return "en-US";
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("ko", StringComparison.OrdinalIgnoreCase)
            ? "ko-KR"
            : "en-US";
    }

    public static void Apply(string? language)
    {
        CurrentLanguage = Resolve(language);
        var dictionaries = System.Windows.Application.Current.Resources.MergedDictionaries;
        var previous = dictionaries.FirstOrDefault(x => x.Source?.OriginalString.Contains("Resources/Strings.", StringComparison.OrdinalIgnoreCase) == true);
        if (previous is not null) dictionaries.Remove(previous);
        dictionaries.Add(new ResourceDictionary
        {
            Source = new Uri($"{ResourcePrefix}{CurrentLanguage}.xaml", UriKind.Relative)
        });
    }

    public static string T(string key) => System.Windows.Application.Current.TryFindResource(key)?.ToString() ?? key;
    public static string F(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, T(key), args);
}
