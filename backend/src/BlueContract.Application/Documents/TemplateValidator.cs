using System.Text.RegularExpressions;
using BlueContract.Domain.Documents;

namespace BlueContract.Application.Documents;

public sealed record TemplateIssue(string Tag, string Message);

public sealed record TemplateValidationResult(bool IsValid, IReadOnlyList<string> Tags, IReadOnlyList<TemplateIssue> Errors);

/// <summary>Checks every tag of an uploaded template against the field catalog of its kind (spec §10: "báo placeholder không hợp lệ").</summary>
public static partial class TemplateValidator
{
    public static readonly IReadOnlySet<string> Filters = new HashSet<string>(StringComparer.Ordinal)
    {
        "money", "words", "date", "datetext", "datetime", "number", "percent", "upper", "lower",
    };

    public static TemplateValidationResult Validate(TemplateKind kind, IReadOnlyList<string> tags)
    {
        var schema = TemplateFieldCatalog.For(kind);
        var globals = schema.Fields.Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
        var collections = schema.Collections.ToDictionary(c => c.Name, c => c.Fields.Select(f => f.Path).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);
        var errors = new List<TemplateIssue>();
        var stack = new Stack<string>();

        foreach (var raw in tags)
        {
            var tag = raw.Trim();
            if (tag.Length == 0)
            {
                errors.Add(new(raw, "Thẻ rỗng {{ }}."));
                continue;
            }

            if (Anchor().IsMatch(tag))
            {
                continue;
            }

            if (tag.StartsWith("#if ", StringComparison.Ordinal))
            {
                var path = tag[4..].Trim().TrimStart('!');
                if (!IsKnownPath(path, stack, globals, collections))
                {
                    errors.Add(new(raw, $"Điều kiện tham chiếu trường không tồn tại: {path}."));
                }

                stack.Push("if");
                continue;
            }

            if (tag.StartsWith('#'))
            {
                var name = tag[1..].Trim();
                if (!collections.ContainsKey(name))
                {
                    errors.Add(new(raw, $"Danh sách lặp không tồn tại: {name}. Danh sách hợp lệ: {string.Join(", ", collections.Keys)}."));
                }
                else if (stack.Contains(name))
                {
                    errors.Add(new(raw, $"Không được lặp lồng chính danh sách {name}."));
                }

                stack.Push(name);
                continue;
            }

            if (tag.StartsWith('/'))
            {
                var name = tag[1..].Trim();
                if (stack.Count == 0 || stack.Peek() != name)
                {
                    errors.Add(new(raw, stack.Count == 0
                        ? $"Thẻ đóng {{{{/{name}}}}} không có thẻ mở tương ứng."
                        : $"Thẻ đóng {{{{/{name}}}}} không khớp, đang chờ {{{{/{stack.Peek()}}}}}."));
                }
                else
                {
                    stack.Pop();
                }

                continue;
            }

            var pipe = tag.IndexOf('|', StringComparison.Ordinal);
            var valuePath = pipe < 0 ? tag : tag[..pipe].Trim();
            var filter = pipe < 0 ? null : tag[(pipe + 1)..].Trim().ToLowerInvariant();
            if (filter is not null && !Filters.Contains(filter))
            {
                errors.Add(new(raw, $"Bộ lọc không hợp lệ: {filter}. Bộ lọc hợp lệ: {string.Join(", ", Filters)}."));
            }

            if (valuePath == "@index")
            {
                if (!stack.Any(collections.ContainsKey))
                {
                    errors.Add(new(raw, "{{@index}} chỉ dùng bên trong vòng lặp."));
                }

                continue;
            }

            if (!IsKnownPath(valuePath, stack, globals, collections))
            {
                errors.Add(new(raw, $"Thẻ dữ liệu không hợp lệ: {valuePath}."));
            }
        }

        foreach (var open in stack)
        {
            errors.Add(new($"#{open}", $"Thiếu thẻ đóng {{{{/{open}}}}}."));
        }

        return new TemplateValidationResult(errors.Count == 0, tags, errors);
    }

    private static bool IsKnownPath(string path, Stack<string> stack, HashSet<string> globals, Dictionary<string, HashSet<string>> collections) =>
        globals.Contains(path) || stack.Any(s => collections.TryGetValue(s, out var fields) && fields.Contains(path));

    [GeneratedRegex("^SIG_[A-Z0-9_]+$")]
    private static partial Regex Anchor();
}
