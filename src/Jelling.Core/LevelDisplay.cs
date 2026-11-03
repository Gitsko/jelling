namespace Jelling.Core;

/// <summary>How a pinned device's badge shows its battery level.</summary>
public enum LevelDisplay
{
    /// <summary>The percentage next to the glyph.</summary>
    Number,

    /// <summary>A ring around the glyph, filled in proportion to the level.</summary>
    Ring,

    /// <summary>The glyph alone; it takes the warning color when the level is low.</summary>
    Symbol,
}
