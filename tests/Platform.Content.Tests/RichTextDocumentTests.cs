using System.Text.Json;
using Platform.Content.Features.Posts;
using Platform.Content.Tests.Support;
using Platform.Helpers.Validation;

namespace Platform.Content.Tests;

public sealed class RichTextDocumentTests
{
    private static string[] Problems(string json)
    {
        var errors = new ValidationErrors();
        RichTextDocument.Validate(TestData.Doc(json), "bodyDoc", errors);
        return errors.Any ? errors.ToDictionary()["bodyDoc"] : [];
    }

    private const string Rich = """
        {"type":"doc","content":[
          {"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"Dream Bigger"}]},
          {"type":"paragraph","content":[
            {"type":"text","text":"Create. "},
            {"type":"text","marks":[{"type":"bold"},{"type":"italic"}],"text":"Inspire."},
            {"type":"hardBreak"},
            {"type":"text","marks":[{"type":"link","attrs":{"href":"https://example.com/a?b=1","target":"_blank","rel":"noopener noreferrer nofollow","class":null,"title":null}}],"text":"Learn more"}]},
          {"type":"bulletList","content":[
            {"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"One"}]}]},
            {"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"Two"}]},
              {"type":"orderedList","attrs":{"start":1,"type":null},"content":[{"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"Nested"}]}]}]}]}]},
          {"type":"blockquote","content":[{"type":"paragraph","content":[{"type":"text","text":"Quoted"}]}]},
          {"type":"paragraph"}
        ]}
        """;

    [Fact]
    public void ADocumentMadeOfTheAllowedPieces_IsValid() => Assert.Empty(Problems(Rich));

    [Fact]
    public void AMissingDocument_IsValid_BecauseAPostMayHaveNoTextYet()
    {
        var errors = new ValidationErrors();

        RichTextDocument.Validate(null, "bodyDoc", errors);
        RichTextDocument.Validate(TestData.Doc("null"), "bodyDoc", errors);

        Assert.False(errors.Any);
    }

    [Theory]
    [InlineData("""[]""")]
    [InlineData("""{"type":"paragraph"}""")]
    [InlineData("""{"type":"doc","content":[{"type":"table"}]}""")]
    [InlineData("""{"type":"doc","content":[{"type":"image","attrs":{"src":"https://example.com/a.png"}}]}""")]
    [InlineData("""{"type":"doc","content":[{"type":"paragraph","content":[{"type":"paragraph"}]}]}""")]
    [InlineData("""{"type":"doc","content":[{"type":"text","text":"loose text"}]}""")]
    [InlineData("""{"type":"doc","content":[{"type":"bulletList","content":[{"type":"paragraph"}]}]}""")]
    [InlineData("""{"type":"doc","extra":1}""")]
    [InlineData("""{"type":"doc","content":{"type":"paragraph"}}""")]
    [InlineData("""{"type":"doc","html":"<script>alert(1)</script>"}""")]
    [InlineData("""{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":""}]}]}""")]
    [InlineData("""{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"x","content":[]}]}]}""")]
    [InlineData("""{"type":"doc","content":[{"type":"paragraph","text":"x"}]}""")]
    [InlineData("""{"type":"doc","content":[{"type":"paragraph","content":[{"type":"hardBreak","content":[{"type":"text","text":"x"}]}]}]}""")]
    public void ADocumentWithAnythingElse_IsRefusedNotCleanedUp(string json) => Assert.NotEmpty(Problems(json));

    [Theory]
    [InlineData("""{"type":"doc","content":[{"type":"heading","content":[{"type":"text","text":"x"}]}]}""")]
    [InlineData("""{"type":"doc","content":[{"type":"heading","attrs":{"level":4},"content":[{"type":"text","text":"x"}]}]}""")]
    [InlineData("""{"type":"doc","content":[{"type":"heading","attrs":{"level":1,"id":"x"},"content":[{"type":"text","text":"x"}]}]}""")]
    [InlineData("""{"type":"doc","content":[{"type":"paragraph","attrs":{"textAlign":"center"}}]}""")]
    [InlineData("""{"type":"doc","content":[{"type":"orderedList","attrs":{"start":0},"content":[]}]}""")]
    public void TheSettingsOfAPart_AreChecked(string json) => Assert.NotEmpty(Problems(json));

    [Theory]
    [InlineData("""{"type":"link","attrs":{"href":"javascript:alert(1)"}}""")]
    [InlineData("""{"type":"link","attrs":{"href":"data:text/html;base64,PHNjcmlwdD4="}}""")]
    [InlineData("""{"type":"link","attrs":{"href":"//evil.example.com"}}""")]
    [InlineData("""{"type":"link","attrs":{"href":"https://exa mple.com"}}""")]
    [InlineData("""{"type":"link","attrs":{"href":"/relative"}}""")]
    [InlineData("""{"type":"link","attrs":{"href":"ftp://example.com"}}""")]
    [InlineData("""{"type":"link","attrs":{}}""")]
    [InlineData("""{"type":"link"}""")]
    [InlineData("""{"type":"link","attrs":{"href":"https://example.com","onclick":"x"}}""")]
    [InlineData("""{"type":"link","attrs":{"href":"https://example.com","target":"_top"}}""")]
    [InlineData("""{"type":"link","attrs":{"href":"https://example.com","class":"evil"}}""")]
    [InlineData("""{"type":"link","attrs":{"href":"https://example.com","title":"a title"}}""")]
    [InlineData("""{"type":"code"}""")]
    [InlineData("""{"type":"bold","attrs":{"x":1}}""")]
    [InlineData("""{"type":"bold","extra":true}""")]
    public void AMark_IsOneOfTheAllowedAndALinkIsASafeAddress(string mark)
    {
        var json = $$"""{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","marks":[{{mark}}],"text":"x"}]}]}""";

        Assert.NotEmpty(Problems(json));
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://example.com/path?x=1#y")]
    [InlineData("mailto:someone@example.com")]
    public void ALink_ToAWebOrMailAddress_IsFine(string href)
    {
        var json = $$$"""{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","marks":[{"type":"link","attrs":{"href":"{{{href}}}"}}],"text":"x"}]}]}""";

        Assert.Empty(Problems(json));
    }

    [Fact]
    public void ThesameMarkTwiceOnOnePieceOfText_IsRefused()
    {
        const string json = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","marks":[{"type":"bold"},{"type":"bold"}],"text":"x"}]}]}""";

        Assert.NotEmpty(Problems(json));
    }

    [Fact]
    public void ADocumentNestedTooDeeply_IsRefused()
    {
        var open = string.Concat(Enumerable.Repeat("""{"type":"blockquote","content":[""", RichTextDocument.MaxDepth + 2));
        var close = string.Concat(Enumerable.Repeat("]}", RichTextDocument.MaxDepth + 2));
        var json = $$"""{"type":"doc","content":[{{open}}{"type":"paragraph"}{{close}}]}""";

        Assert.NotEmpty(Problems(json));
    }

    [Fact]
    public void ADocumentWithTooManyParts_IsRefused()
    {
        var paragraphs = string.Join(",", Enumerable.Repeat("""{"type":"paragraph"}""", RichTextDocument.MaxNodes + 1));

        Assert.NotEmpty(Problems($$"""{"type":"doc","content":[{{paragraphs}}]}"""));
    }

    [Fact]
    public void ADocumentWithTooMuchText_IsRefused()
    {
        var text = new string('a', RichTextDocument.MaxTextLength + 1);
        var chunks = string.Join(",", Enumerable.Range(0, 3).Select(_ => $$"""{"type":"paragraph","content":[{"type":"text","text":"{{text[..70_000]}}"}]}"""));
        var tooMuch = $$"""{"type":"doc","content":[{{chunks}}]}""";

        Assert.NotEmpty(Problems(tooMuch));
    }

    [Fact]
    public void ADocumentOverAMegabyte_IsRefusedBeforeItIsWalked()
    {
        var spaces = new string(' ', RichTextDocument.MaxBytes);

        Assert.Equal(["The content is too large."], Problems($$"""{"type":"doc"{{spaces}} }"""));
    }

    // --- Plain text ---------------------------------------------------------------------------

    [Fact]
    public void ThePlainText_SeparatesBlocksWithABlankLine_AndShowsListsAndBreaks()
    {
        var text = RichTextDocument.ToPlainText(TestData.Doc(Rich));

        Assert.Equal(
            "Dream Bigger\n\nCreate. Inspire.\nLearn more\n\n• One\n• Two\n  1. Nested\n\nQuoted",
            text);
    }

    [Fact]
    public void ANumberedList_StartsWhereItSaysAndCountsUp()
    {
        const string json = """
            {"type":"doc","content":[{"type":"orderedList","attrs":{"start":3},"content":[
              {"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"c"}]}]},
              {"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"d"}]}]}]}]}
            """;

        Assert.Equal("3. c\n4. d", RichTextDocument.ToPlainText(TestData.Doc(json)));
    }

    [Fact]
    public void ADocumentWithNoContent_HasNoText()
    {
        Assert.Equal(string.Empty, RichTextDocument.ToPlainText(TestData.Doc("""{"type":"doc"}""")));
        Assert.Equal(string.Empty, RichTextDocument.ToPlainText(TestData.Doc("""{"type":"doc","content":[{"type":"paragraph"}]}""")));
    }

    [Theory]
    [InlineData("short", "short")]
    [InlineData("  two   spaces\nand a\n\nbreak ", "two spaces and a break")]
    public void Excerpt_OfShortText_IsTheTextOnOneLine(string text, string expected) =>
        Assert.Equal(expected, RichTextDocument.Excerpt(text));

    [Fact]
    public void Excerpt_OfLongText_IsCutAtAWordAndEndsInAnEllipsis()
    {
        var text = string.Join(" ", Enumerable.Repeat("word", 100));

        var excerpt = RichTextDocument.Excerpt(text);

        Assert.True(excerpt.Length <= RichTextDocument.ExcerptLength);
        Assert.EndsWith("…", excerpt);
        Assert.DoesNotContain("wor…", excerpt);
        Assert.StartsWith("word word", excerpt);
    }

    [Theory]
    [InlineData("https://example.com", true)]
    [InlineData("mailto:a@b.co", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSafeLink_AllowsOnlyWebAndMail(string? href, bool expected) => Assert.Equal(expected, RichTextDocument.IsSafeLink(href));

    [Fact]
    public void TheKindsOfPart_AreTheOnesTheEditorAndTheRendererKnow()
    {
        Assert.Equal(
            ["blockquote", "bulletList", "doc", "hardBreak", "heading", "listItem", "orderedList", "paragraph", "text"],
            RichTextDocument.NodeTypes.Order(StringComparer.Ordinal));
        Assert.Equal(["bold", "italic", "underline", "link"], RichTextDocument.MarkTypes);
    }

    [Fact]
    public void AValidDocument_ParsesBackFromTheTextTheDatabaseKeeps()
    {
        var stored = TestData.Doc(Rich).GetRawText();

        var again = PostMapper.Json(stored);

        Assert.Equal(JsonValueKind.Object, again.ValueKind);
        Assert.Equal(RichTextDocument.ToPlainText(TestData.Doc(Rich)), RichTextDocument.ToPlainText(again));
    }
}
