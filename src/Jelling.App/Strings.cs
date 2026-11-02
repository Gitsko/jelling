using Microsoft.Windows.ApplicationModel.Resources;

namespace Jelling.App;

/// <summary>The localized strings from Strings/*/Resources.resw.</summary>
internal static class Strings
{
    private static readonly ResourceLoader Loader = new();

    public static string Get(string name) => Loader.GetString(name);
}
