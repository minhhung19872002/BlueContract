using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using BlueProcure.Domain.Common;

namespace BlueProcure.Infrastructure.Documents;

/// <summary>Formats placeholder values with Vietnamese conventions (spec §0.4, §10).</summary>
public static class TemplateValueFormatter
{
    public static readonly IReadOnlySet<string> Filters = new HashSet<string>(StringComparer.Ordinal)
    {
        "money", "words", "date", "datetext", "datetime", "number", "percent", "upper", "lower",
    };

    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");
    private static readonly TimeZoneInfo VietnamZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");

    public static string Format(JsonNode? value, string? filter)
    {
        if (value is null)
        {
            return string.Empty;
        }

        return filter switch
        {
            "money" => TryDecimal(value, out var m) ? Math.Round(m, 0, MidpointRounding.AwayFromZero).ToString("#,##0", Vi) : Text(value),
            "words" => TryDecimal(value, out var w) ? VietnameseNumberReader.ReadMoney(w) : Text(value),
            "number" => TryDecimal(value, out var n) ? n.ToString("#,##0.###", Vi) : Text(value),
            "percent" => TryDecimal(value, out var p) ? p.ToString("0.##", Vi) + "%" : Text(value),
            "date" => TryDate(value, out var d) ? d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : Text(value),
            "datetime" => TryDate(value, out var dt) ? dt.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture) : Text(value),
            "datetext" => TryDate(value, out var dtx) ? $"ngày {dtx.Day:00} tháng {dtx.Month:00} năm {dtx.Year}" : Text(value),
            "upper" => Text(value).ToUpper(Vi),
            "lower" => Text(value).ToLower(Vi),
            _ => Default(value),
        };
    }

    /// <summary>Truthiness for {{#if}}: missing, null, false, 0, "" and empty arrays are false.</summary>
    public static bool IsTruthy(JsonNode? value) => value switch
    {
        null => false,
        JsonArray a => a.Count > 0,
        JsonObject => true,
        JsonValue v when v.TryGetValue<bool>(out var b) => b,
        JsonValue v when TryDecimal(v, out var d) => d != 0,
        _ => !string.IsNullOrWhiteSpace(Text(value)),
    };

    private static string Default(JsonNode value) => value switch
    {
        JsonValue v when v.TryGetValue<bool>(out var b) => b ? "Có" : "Không",
        JsonValue v when v.GetValueKind() == JsonValueKind.Number && TryDecimal(v, out var d) =>
            d == decimal.Truncate(d) ? d.ToString("#,##0", Vi) : d.ToString("#,##0.###", Vi),
        _ => Text(value),
    };

    private static string Text(JsonNode value) =>
        value is JsonValue v && v.TryGetValue<string>(out var s) ? s : value.ToJsonString();

    private static bool TryDecimal(JsonNode node, out decimal result)
    {
        result = 0;
        if (node is not JsonValue v)
        {
            return false;
        }

        if (v.TryGetValue(out decimal d))
        {
            result = d;
            return true;
        }

        if (v.TryGetValue(out double dbl))
        {
            result = (decimal)dbl;
            return true;
        }

        if (v.TryGetValue(out long l))
        {
            result = l;
            return true;
        }

        return v.TryGetValue(out string? s) && decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out result);
    }

    private static bool TryDate(JsonNode node, out DateTime result)
    {
        result = default;
        if (node is not JsonValue v || !v.TryGetValue(out string? s) || string.IsNullOrWhiteSpace(s))
        {
            return false;
        }

        // Date-only values are business dates; timestamps are converted to Vietnam time.
        if (DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnly))
        {
            result = dateOnly.ToDateTime(TimeOnly.MinValue);
            return true;
        }

        if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
        {
            result = TimeZoneInfo.ConvertTime(dto, VietnamZone).DateTime;
            return true;
        }

        return false;
    }
}
