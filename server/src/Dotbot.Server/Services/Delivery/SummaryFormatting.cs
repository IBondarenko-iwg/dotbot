using System.Globalization;

namespace Dotbot.Server.Services.Delivery;

internal static class SummaryFormatting
{
    public static string FormatAttachmentSize(long? sizeBytes)
    {
        if (!sizeBytes.HasValue) return "";
        var b = sizeBytes.Value;
        var inv = CultureInfo.InvariantCulture;
        if (b < 1024) return $" ({b} B)";
        if (b < 1024 * 1024) return $" ({(b / 1024.0).ToString("0.#", inv)} KB)";
        return $" ({(b / (1024.0 * 1024.0)).ToString("0.#", inv)} MB)";
    }
}
