namespace VmTips.Web.Data;

public static class TeamFlags
{
    private static readonly Dictionary<string, string> FlagCodesByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Argentina"] = "ar",
        ["Australien"] = "au",
        ["Belgien"] = "be",
        ["Bosnien och Hercegovina"] = "ba",
        ["Brasilien"] = "br",
        ["Brazil"] = "br",
        ["Chile"] = "cl",
        ["Colombia"] = "co",
        ["Costa Rica"] = "cr",
        ["Danmark"] = "dk",
        ["Ecuador"] = "ec",
        ["Egypten"] = "eg",
        ["Egypt"] = "eg",
        ["England"] = "gb-eng",
        ["Frankrike"] = "fr",
        ["France"] = "fr",
        ["Ghana"] = "gh",
        ["Irland"] = "ie",
        ["Italien"] = "it",
        ["Japan"] = "jp",
        ["Kanada"] = "ca",
        ["Kroatien"] = "hr",
        ["Croatia"] = "hr",
        ["Marocko"] = "ma",
        ["Mexiko"] = "mx",
        ["Mexico"] = "mx",
        ["Nederländerna"] = "nl",
        ["Norge"] = "no",
        ["Paraguay"] = "py",
        ["Polen"] = "pl",
        ["Portugal"] = "pt",
        ["Qatar"] = "qa",
        ["Schweiz"] = "ch",
        ["Serbien"] = "rs",
        ["South Africa"] = "za",
        ["Spanien"] = "es",
        ["Spain"] = "es",
        ["Sverige"] = "se",
        ["Sydkorea"] = "kr",
        ["Tjeckien"] = "cz",
        ["Tyskland"] = "de",
        ["Germany"] = "de",
        ["Uruguay"] = "uy",
        ["USA"] = "us"
    };

    private static readonly Dictionary<string, string> FlagsByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Argentina"] = "🇦🇷",
        ["Australien"] = "🇦🇺",
        ["Belgien"] = "🇧🇪",
        ["Bosnien och Hercegovina"] = "🇧🇦",
        ["Brasilien"] = "🇧🇷",
        ["Brazil"] = "🇧🇷",
        ["Chile"] = "🇨🇱",
        ["Colombia"] = "🇨🇴",
        ["Costa Rica"] = "🇨🇷",
        ["Danmark"] = "🇩🇰",
        ["Ecuador"] = "🇪🇨",
        ["Egypten"] = "🇪🇬",
        ["Egypt"] = "🇪🇬",
        ["England"] = "🏴",
        ["Frankrike"] = "🇫🇷",
        ["France"] = "🇫🇷",
        ["Ghana"] = "🇬🇭",
        ["Irland"] = "🇮🇪",
        ["Italien"] = "🇮🇹",
        ["Japan"] = "🇯🇵",
        ["Kanada"] = "🇨🇦",
        ["Kroatien"] = "🇭🇷",
        ["Croatia"] = "🇭🇷",
        ["Marocko"] = "🇲🇦",
        ["Mexiko"] = "🇲🇽",
        ["Mexico"] = "🇲🇽",
        ["Nederländerna"] = "🇳🇱",
        ["Norge"] = "🇳🇴",
        ["Paraguay"] = "🇵🇾",
        ["Polen"] = "🇵🇱",
        ["Portugal"] = "🇵🇹",
        ["Qatar"] = "🇶🇦",
        ["Schweiz"] = "🇨🇭",
        ["Serbien"] = "🇷🇸",
        ["South Africa"] = "🇿🇦",
        ["Spanien"] = "🇪🇸",
        ["Spain"] = "🇪🇸",
        ["Sverige"] = "🇸🇪",
        ["Sydkorea"] = "🇰🇷",
        ["Tjeckien"] = "🇨🇿",
        ["Tyskland"] = "🇩🇪",
        ["Germany"] = "🇩🇪",
        ["Uruguay"] = "🇺🇾",
        ["USA"] = "🇺🇸"
    };

    public static string GetFlag(string teamName)
    {
        return FlagsByName.TryGetValue(teamName.Trim(), out var flag)
            ? flag
            : string.Empty;
    }

    public static string GetFlagCode(string teamName)
    {
        return FlagCodesByName.TryGetValue(teamName.Trim(), out var code)
            ? code
            : string.Empty;
    }
}
