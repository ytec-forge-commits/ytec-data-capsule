using System.Globalization;
using System.Windows;

namespace Ytec.WindowsBackup.App;

internal static class UiLanguage
{
    private const string JapaneseCode = "ja";
    private const string EnglishCode = "en";

    public static string Code { get; private set; } = EnglishCode;

    public static bool IsJapanese =>
        string.Equals(Code, JapaneseCode, StringComparison.Ordinal);

    public static CultureInfo Culture { get; private set; } =
        CultureInfo.GetCultureInfo("en-US");

    public static void Initialize(IEnumerable<string> arguments)
    {
        var requested = arguments.FirstOrDefault(argument =>
            argument.StartsWith("--lang=", StringComparison.OrdinalIgnoreCase));
        var code = requested is null
            ? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
            : requested.Substring("--lang=".Length);
        SetLanguage(
            string.Equals(code, JapaneseCode, StringComparison.OrdinalIgnoreCase)
                ? JapaneseCode
                : EnglishCode);
    }

    public static void Toggle() =>
        SetLanguage(IsJapanese ? EnglishCode : JapaneseCode);

    public static string Text(string key) =>
        System.Windows.Application.Current.TryFindResource(key) as string ?? key;

    public static string Format(string key, params object[] arguments) =>
        string.Format(Culture, Text(key), arguments);

    private static void SetLanguage(string code)
    {
        Code = code;
        Culture = CultureInfo.GetCultureInfo(
            IsJapanese ? "ja-JP" : "en-US");
        Thread.CurrentThread.CurrentUICulture = Culture;

        var dictionaries = System.Windows.Application.Current.Resources.MergedDictionaries;
        var current = dictionaries.FirstOrDefault(dictionary =>
            dictionary.Source?.OriginalString.StartsWith(
                "Localization/Strings.",
                StringComparison.OrdinalIgnoreCase) == true);
        var replacement = new ResourceDictionary
        {
            Source = new Uri(
                $"Localization/Strings.{Code}.xaml",
                UriKind.Relative),
        };
        if (current is null)
        {
            dictionaries.Insert(0, replacement);
            return;
        }

        dictionaries[dictionaries.IndexOf(current)] = replacement;
    }
}
