using System.Globalization;

namespace MapDecals;

/// <summary>Pure validation shared by UI and persistence; no engine dependencies.</summary>
internal static class DecalRules
{
    internal const int PageSize = 6;
    internal static string MenuPosition(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "left" => "left",
        "right" => "right",
        _ => "center"
    };
    internal static int PageCount(int count) => Math.Max(1, (count + PageSize - 1) / PageSize);
    internal static bool IsVisible(bool permitted, bool forced, bool preference) => permitted && (forced || preference);
    internal static bool TryNumber(string text, float min, float max, out float value) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        && float.IsFinite(value) && value >= min && value <= max;
    internal static string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture);
    internal static string Triple(float x, float y, float z) => $"{Number(x)} {Number(y)} {Number(z)}";
    internal static bool ValidName(string name) => !string.IsNullOrWhiteSpace(name) && name.Length is >= 3 and <= 64
        && !name.Any(char.IsControl);
}
