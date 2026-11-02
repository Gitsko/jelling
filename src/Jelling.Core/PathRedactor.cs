using System.Text.RegularExpressions;

namespace Jelling.Core;

/// <summary>
/// Replaces folders in text, also written with forward slashes as in file URIs, but not where the folder
/// name only starts a longer one, as C:\Users\bob does C:\Users\bobby. Case does not matter, as on Windows.
/// </summary>
public sealed class PathRedactor
{
    private readonly Regex? _pattern;
    private readonly string _placeholder;

    /// <param name="folders">Spellings of the same folder, e.g. its long and its 8.3 short form; empty ones are skipped.</param>
    public PathRedactor(IEnumerable<string> folders, string placeholder)
    {
        _placeholder = placeholder;
        var forms = folders
            .Where(folder => folder.Length > 0)
            .SelectMany(folder => new[] { folder, folder.Replace('\\', '/') })
            .Distinct(StringComparer.OrdinalIgnoreCase)

            // Longest first: C:\Users\bob would otherwise take the start of the short form C:\Users\BOB~1
            .OrderByDescending(form => form.Length)
            .Select(Regex.Escape)
            .ToList();
        if (forms.Count > 0)
        {
            _pattern = new Regex($@"(?:{string.Join('|', forms)})(?![\w.-])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
    }

    public string Redact(string text) => _pattern?.Replace(text, _placeholder) ?? text;
}
