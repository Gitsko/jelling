namespace Jelling.Core;

/// <summary>
/// Release versions as the app shows and compares them: three parts, written like the Git tags ("v1.2.0").
/// </summary>
public static class ReleaseVersion
{
    /// <summary>The version <paramref name="tag"/> names, if that is a later release than <paramref name="current"/>.</summary>
    /// <returns>Null otherwise, and for anything but a plain three-part version such as "v1.1.0-beta".</returns>
    public static Version? Newer(string? tag, Version current) =>
        tag is not null && Parse(tag) is { } release && release > Normalize(current) ? release : null;

    public static string Format(Version version) => $"v{Normalize(version).ToString(3)}";

    // Tags have up to three parts; a fourth would be dropped from the version shown, so it is no release
    private static Version? Parse(string tag) =>
        Version.TryParse(tag.TrimStart('v', 'V'), out var version) && version.Revision < 0 ? Normalize(version) : null;

    // Assemblies have four parts (1.0.0.0), tags three, whose missing part counts as -1; pad both to three
    private static Version Normalize(Version version) =>
        new(version.Major, version.Minor, Math.Max(version.Build, 0));
}
