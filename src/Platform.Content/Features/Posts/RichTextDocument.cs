using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Platform.Helpers.Validation;

namespace Platform.Content.Features.Posts;

/// <summary>
/// The editor's document: a ProseMirror JSON object, checked against an allowlist of node and mark types and reduced to plain text.
/// No HTML is stored, parsed or sent anywhere: the document is data, the web page draws it with its own renderer, and what a
/// platform receives is the plain text. A document with anything this class does not know is refused, not cleaned up.
/// </summary>
public static partial class RichTextDocument
{
    public const int MaxDepth = 8;
    public const int MaxNodes = 5000;
    public const int MaxTextLength = 200_000;
    public const int MaxBytes = 1_000_000;
    public const int MaxLinkLength = 2048;
    public const int ExcerptLength = 160;

    // For each kind of node, the kinds it may contain. The web client's editor and renderer must know the same set (a test compares).
    private static readonly Dictionary<string, string[]> Allowed = new(StringComparer.Ordinal)
    {
        ["doc"] = ["paragraph", "heading", "bulletList", "orderedList", "blockquote"],
        ["paragraph"] = ["text", "hardBreak"],
        ["heading"] = ["text", "hardBreak"],
        ["bulletList"] = ["listItem"],
        ["orderedList"] = ["listItem"],
        ["listItem"] = ["paragraph", "bulletList", "orderedList"],
        ["blockquote"] = ["paragraph", "heading", "bulletList", "orderedList", "blockquote"],
        ["hardBreak"] = [],
        ["text"] = []
    };

    /// <summary>The marks a piece of text may carry.</summary>
    public static readonly IReadOnlyList<string> MarkTypes = ["bold", "italic", "underline", "link"];

    /// <summary>Every kind of node, root included.</summary>
    public static IReadOnlyCollection<string> NodeTypes => Allowed.Keys;

    private sealed class Counter
    {
        public int Nodes;
        public int TextLength;
    }

    /// <summary>
    /// Adds one field error under <paramref name="field"/> when the document is not valid. A missing document (null) is valid: a
    /// post may have no text yet; whether it needs some is the caller's rule.
    /// </summary>
    public static void Validate(JsonElement? document, string field, ValidationErrors errors)
    {
        if (document is null || document.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return;
        }

        var raw = document.Value.GetRawText();
        if (Encoding.UTF8.GetByteCount(raw) > MaxBytes)
        {
            errors.Add(field, "The content is too large.");
            return;
        }

        var problem = VisitNode(document.Value, null, 0, new Counter());
        if (problem is not null)
        {
            errors.Add(field, problem);
        }
    }

    private static string? VisitNode(JsonElement node, string? parent, int depth, Counter counter)
    {
        if (node.ValueKind != JsonValueKind.Object)
        {
            return "Every part of the content must be an object.";
        }

        if (depth > MaxDepth)
        {
            return "The content is nested too deeply.";
        }

        if (++counter.Nodes > MaxNodes)
        {
            return "The content has too many parts.";
        }

        string? type = null;
        string? text = null;
        JsonElement content = default, attrs = default, marks = default;
        var hasContent = false;
        var hasAttrs = false;
        var hasMarks = false;
        var hasText = false;
        foreach (var property in node.EnumerateObject())
        {
            switch (property.Name)
            {
                case "type":
                    if (property.Value.ValueKind != JsonValueKind.String)
                    {
                        return "A part of the content has no kind.";
                    }

                    type = property.Value.GetString();
                    break;
                case "content":
                    content = property.Value;
                    hasContent = true;
                    break;
                case "attrs":
                    attrs = property.Value;
                    hasAttrs = true;
                    break;
                case "marks":
                    marks = property.Value;
                    hasMarks = true;
                    break;
                case "text":
                    if (property.Value.ValueKind != JsonValueKind.String)
                    {
                        return "The text of a part of the content must be text.";
                    }

                    text = property.Value.GetString();
                    hasText = true;
                    break;
                default:
                    return "The content has a part this editor does not use.";
            }
        }

        if (type is null || !Allowed.TryGetValue(type, out var childTypes))
        {
            return "The content has a kind of part this editor does not use.";
        }

        if (parent is null ? type != "doc" : !Allowed[parent].Contains(type))
        {
            return $"A {type} cannot go there.";
        }

        if (type == "text")
        {
            if (string.IsNullOrEmpty(text) || hasContent || hasAttrs)
            {
                return "A piece of text must have text and nothing else but marks.";
            }

            counter.TextLength += text.Length;
            return counter.TextLength > MaxTextLength ? "The text is too long." : hasMarks ? ValidateMarks(marks) : null;
        }

        if (hasText || hasMarks)
        {
            return "Only a piece of text has text or marks.";
        }

        var attrsProblem = ValidateAttrs(type, hasAttrs ? attrs : null);
        if (attrsProblem is not null)
        {
            return attrsProblem;
        }

        if (!hasContent)
        {
            // An empty paragraph or heading is fine; a list or a document with nothing in it is not meaningful but harmless.
            return null;
        }

        if (content.ValueKind != JsonValueKind.Array)
        {
            return "The content of a part must be a list.";
        }

        if (childTypes.Length == 0 && content.GetArrayLength() > 0)
        {
            return $"A {type} cannot contain anything.";
        }

        foreach (var child in content.EnumerateArray())
        {
            var problem = VisitNode(child, type, depth + 1, counter);
            if (problem is not null)
            {
                return problem;
            }
        }

        return null;
    }

    private static string? ValidateAttrs(string type, JsonElement? attrs)
    {
        if (attrs is null || attrs.Value.ValueKind == JsonValueKind.Null)
        {
            return type == "heading" ? "A heading needs a level." : null;
        }

        if (attrs.Value.ValueKind != JsonValueKind.Object)
        {
            return "The settings of a part must be an object.";
        }

        switch (type)
        {
            case "heading":
                foreach (var property in attrs.Value.EnumerateObject())
                {
                    if (property.Name != "level" || !property.Value.TryGetInt32(out var level) || level is < 1 or > 3)
                    {
                        return "A heading has a level from 1 to 3 and nothing else.";
                    }
                }

                return attrs.Value.TryGetProperty("level", out _) ? null : "A heading needs a level.";
            case "orderedList":
                foreach (var property in attrs.Value.EnumerateObject())
                {
                    var ok = property.Name switch
                    {
                        "start" => property.Value.ValueKind == JsonValueKind.Null
                                   || (property.Value.TryGetInt32(out var start) && start is >= 1 and <= 9999),
                        "type" => property.Value.ValueKind == JsonValueKind.Null
                                  || (property.Value.ValueKind == JsonValueKind.String && property.Value.GetString()!.Length <= 1),
                        _ => false
                    };
                    if (!ok)
                    {
                        return "A numbered list has a start number and nothing else.";
                    }
                }

                return null;
            default:
                return attrs.Value.EnumerateObject().Any() ? $"A {type} has no settings." : null;
        }
    }

    private static string? ValidateMarks(JsonElement marks)
    {
        if (marks.ValueKind != JsonValueKind.Array)
        {
            return "The marks of a piece of text must be a list.";
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var mark in marks.EnumerateArray())
        {
            if (mark.ValueKind != JsonValueKind.Object)
            {
                return "Every mark must be an object.";
            }

            string? type = null;
            JsonElement? attrs = null;
            foreach (var property in mark.EnumerateObject())
            {
                switch (property.Name)
                {
                    case "type" when property.Value.ValueKind == JsonValueKind.String:
                        type = property.Value.GetString();
                        break;
                    case "attrs":
                        attrs = property.Value;
                        break;
                    default:
                        return "A mark has a kind and settings and nothing else.";
                }
            }

            if (type is null || !MarkTypes.Contains(type))
            {
                return "The content has a kind of mark this editor does not use.";
            }

            if (!seen.Add(type))
            {
                return "A piece of text cannot have the same mark twice.";
            }

            var problem = type == "link" ? ValidateLink(attrs) : NoSettings(attrs);
            if (problem is not null)
            {
                return problem;
            }
        }

        return null;
    }

    private static string? NoSettings(JsonElement? attrs) =>
        attrs is null || attrs.Value.ValueKind == JsonValueKind.Null || (attrs.Value.ValueKind == JsonValueKind.Object && !attrs.Value.EnumerateObject().Any())
            ? null
            : "That mark has no settings.";

    private static string? ValidateLink(JsonElement? attrs)
    {
        if (attrs is null || attrs.Value.ValueKind != JsonValueKind.Object)
        {
            return "A link needs an address.";
        }

        string? href = null;
        foreach (var property in attrs.Value.EnumerateObject())
        {
            switch (property.Name)
            {
                case "href" when property.Value.ValueKind == JsonValueKind.String:
                    href = property.Value.GetString();
                    break;
                case "target" when property.Value.ValueKind == JsonValueKind.Null
                                   || (property.Value.ValueKind == JsonValueKind.String && property.Value.GetString() == "_blank"):
                    break;
                case "rel" when property.Value.ValueKind == JsonValueKind.Null
                                || (property.Value.ValueKind == JsonValueKind.String && property.Value.GetString()!.Length <= 100):
                    break;
                case "class" when property.Value.ValueKind == JsonValueKind.Null:
                    break;
                // Tiptap 3 writes a link's title on every link, null, and the editor has no way to set one. Only null passes.
                case "title" when property.Value.ValueKind == JsonValueKind.Null:
                    break;
                default:
                    return "A link has an address and nothing else that this editor uses.";
            }
        }

        return IsSafeLink(href) ? null : "A link must be a web address (http or https) or an email address (mailto).";
    }

    /// <summary>Whether an address may be linked: http, https or mailto, no spaces or control characters, a sane length.</summary>
    public static bool IsSafeLink(string? href)
    {
        if (string.IsNullOrEmpty(href) || href.Length > MaxLinkLength || href.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
        {
            return false;
        }

        return Uri.TryCreate(href, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "mailto";
    }

    /// <summary>
    /// The plain text of a valid document: blocks separated by a blank line, list items one per line (with their bullet or
    /// number), a line break as a line break. Marks and headings are not shown; what a platform receives has no formatting.
    /// </summary>
    public static string ToPlainText(JsonElement document)
    {
        var blocks = new List<string>();
        if (document.ValueKind == JsonValueKind.Object && document.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var block in content.EnumerateArray())
            {
                AppendBlock(block, blocks);
            }
        }

        return string.Join("\n\n", blocks.Where(block => block.Trim().Length > 0)).Trim();
    }

    private static void AppendBlock(JsonElement node, List<string> blocks)
    {
        switch (TypeOf(node))
        {
            case "paragraph" or "heading":
                blocks.Add(Inline(node));
                break;
            case "blockquote":
                foreach (var child in Children(node))
                {
                    AppendBlock(child, blocks);
                }

                break;
            case "bulletList" or "orderedList":
                blocks.Add(string.Join("\n", ListLines(node, string.Empty)));
                break;
        }
    }

    private static IEnumerable<string> ListLines(JsonElement list, string indent)
    {
        var ordered = TypeOf(list) == "orderedList";
        var number = 1;
        if (ordered && list.TryGetProperty("attrs", out var attrs) && attrs.ValueKind == JsonValueKind.Object
            && attrs.TryGetProperty("start", out var start) && start.TryGetInt32(out var first))
        {
            number = first;
        }

        foreach (var item in Children(list))
        {
            var marker = ordered ? $"{number++}." : "•";
            var first_ = true;
            foreach (var part in Children(item))
            {
                if (TypeOf(part) is "bulletList" or "orderedList")
                {
                    foreach (var line in ListLines(part, indent + "  "))
                    {
                        yield return line;
                    }
                }
                else if (first_)
                {
                    first_ = false;
                    yield return $"{indent}{marker} {Inline(part)}";
                }
                else
                {
                    yield return $"{indent}  {Inline(part)}";
                }
            }
        }
    }

    private static string Inline(JsonElement node)
    {
        var text = new StringBuilder();
        foreach (var child in Children(node))
        {
            switch (TypeOf(child))
            {
                case "text" when child.TryGetProperty("text", out var value) && value.ValueKind == JsonValueKind.String:
                    text.Append(value.GetString());
                    break;
                case "hardBreak":
                    text.Append('\n');
                    break;
            }
        }

        return text.ToString();
    }

    private static string? TypeOf(JsonElement node) =>
        node.ValueKind == JsonValueKind.Object && node.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String ? type.GetString() : null;

    private static IEnumerable<JsonElement> Children(JsonElement node) =>
        node.ValueKind == JsonValueKind.Object && node.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array
            ? content.EnumerateArray()
            : [];

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>The first words of the plain text on one line, for the list: whitespace collapsed, cut at a word boundary, ending in an ellipsis when cut.</summary>
    public static string Excerpt(string plainText, int length = ExcerptLength)
    {
        var collapsed = Whitespace().Replace(plainText, " ").Trim();
        if (collapsed.Length <= length)
        {
            return collapsed;
        }

        var cut = collapsed[..(length - 1)];
        var space = cut.LastIndexOf(' ');
        return (space > length / 2 ? cut[..space] : cut).TrimEnd() + "…";
    }
}
