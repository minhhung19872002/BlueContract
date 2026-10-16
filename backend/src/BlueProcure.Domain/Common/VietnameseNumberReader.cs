using System.Globalization;
using System.Text;

namespace BlueProcure.Domain.Common;

/// <summary>
/// Reads numbers as Vietnamese words (đọc số thành chữ) following administrative-document conventions:
/// "linh" for a zero tens digit, "mốt"/"tư"/"lăm" in compound positions, "không trăm" for inner zero hundreds,
/// and groups nghìn / triệu / tỷ (tỷ repeats for larger magnitudes: "nghìn tỷ", "triệu tỷ").
/// Example: 1_205_030_000 → "Một tỷ hai trăm linh năm triệu không trăm ba mươi nghìn đồng".
/// </summary>
public static class VietnameseNumberReader
{
    private static readonly string[] Digits = ["không", "một", "hai", "ba", "bốn", "năm", "sáu", "bảy", "tám", "chín"];
    private static readonly string[] GroupNames = ["", "nghìn", "triệu"];

    /// <summary>Money in VND, capitalised, ending with "đồng" (e.g. "Hai triệu đồng"). Decimals are rounded to whole đồng.</summary>
    public static string ReadMoney(decimal amount)
    {
        var rounded = Math.Round(amount, 0, MidpointRounding.AwayFromZero);
        var words = Read(rounded);
        return Capitalize(words) + " đồng";
    }

    /// <summary>Integer part as words, lower case. Negative numbers are prefixed with "âm".</summary>
    public static string Read(decimal number)
    {
        var value = decimal.Truncate(number);
        if (value == 0)
        {
            return "không";
        }

        var negative = value < 0;
        var digits = Math.Abs(value).ToString("0", CultureInfo.InvariantCulture);
        var text = ReadDigits(digits);
        return negative ? "âm " + text : text;
    }

    private static string ReadDigits(string digits)
    {
        // Split into 9-digit "tỷ" blocks from the right: each block above the lowest is followed by "tỷ".
        var blocks = new List<string>();
        for (var end = digits.Length; end > 0; end -= 9)
        {
            var start = Math.Max(0, end - 9);
            blocks.Insert(0, digits[start..end]);
        }

        var parts = new List<string>();
        for (var i = 0; i < blocks.Count; i++)
        {
            var block = long.Parse(blocks[i], CultureInfo.InvariantCulture);
            var tyCount = blocks.Count - 1 - i;
            var isFirstNonZero = parts.Count == 0;
            if (block == 0)
            {
                continue;
            }

            var blockText = ReadBelowBillion(block, isFirstNonZero);
            var suffix = string.Join(' ', Enumerable.Repeat("tỷ", tyCount));
            parts.Add(string.IsNullOrEmpty(suffix) ? blockText : blockText + " " + suffix);
        }

        return string.Join(' ', parts);
    }

    /// <summary>0 &lt; n &lt; 1,000,000,000 split into hundreds groups (đơn vị, nghìn, triệu).</summary>
    private static string ReadBelowBillion(long n, bool isLeading)
    {
        var groups = new[] { (int)(n / 1_000_000 % 1000), (int)(n / 1000 % 1000), (int)(n % 1000) };
        var sb = new StringBuilder();
        var started = false;
        for (var g = 0; g < 3; g++)
        {
            var value = groups[g];
            if (value == 0)
            {
                continue;
            }

            // Inner groups (not the very first spoken group) read their hundreds even when zero: "không trăm ba mươi".
            var full = started || !isLeading;
            if (sb.Length > 0)
            {
                sb.Append(' ');
            }

            sb.Append(ReadHundreds(value, full));
            var name = GroupNames[2 - g];
            if (name.Length > 0)
            {
                sb.Append(' ').Append(name);
            }

            started = true;
        }

        return sb.ToString();
    }

    private static string ReadHundreds(int n, bool full)
    {
        var hundreds = n / 100;
        var tens = n / 10 % 10;
        var units = n % 10;
        var parts = new List<string>();

        if (hundreds > 0 || full)
        {
            parts.Add(Digits[hundreds] + " trăm");
        }

        if (tens == 0)
        {
            if (units > 0 && (hundreds > 0 || full))
            {
                parts.Add("linh");
            }
        }
        else if (tens == 1)
        {
            parts.Add("mười");
        }
        else
        {
            parts.Add(Digits[tens] + " mươi");
        }

        if (units > 0)
        {
            parts.Add(units switch
            {
                1 when tens > 1 => "mốt",
                4 when tens > 1 => "tư",
                5 when tens > 0 => "lăm",
                _ => Digits[units],
            });
        }

        return string.Join(' ', parts);
    }

    private static string Capitalize(string text) =>
        text.Length == 0 ? text : char.ToUpper(text[0], CultureInfo.GetCultureInfo("vi-VN")) + text[1..];
}
