using DDjourneys.Core.Documents;

namespace DDjourneys.Tracking.Tests;

/// <summary>The reader behind the About page: the README is shown as it is written.</summary>
public sealed class MarkdownDocumentTests
{
	private static T Single<T>(string markdown)
		where T : MarkdownBlock =>
		Assert.IsType<T>(Assert.Single(MarkdownDocument.Parse(markdown)));

	[Theory]
	[InlineData("# One", 1, "One")]
	[InlineData("## Two words", 2, "Two words")]
	[InlineData("###### Six", 6, "Six")]
	[InlineData("## Closed ##", 2, "Closed")]
	[InlineData("# C#", 1, "C#")]
	public void A_heading_has_its_level_and_text(string line, int level, string text)
	{
		var heading = Single<MarkdownHeading>(line);

		Assert.Equal(level, heading.Level);
		Assert.Equal(text, string.Concat(heading.Content.Select(item => item.Text)));
	}

	[Theory]
	[InlineData("#NoSpace")]
	[InlineData("####### Seven")]
	public void A_hash_without_the_space_is_a_paragraph(string line) =>
		Single<MarkdownParagraph>(line);

	[Fact]
	public void Lines_of_one_paragraph_are_joined_with_a_space()
	{
		var paragraph = Single<MarkdownParagraph>("first line\nsecond line");

		Assert.Equal("first line second line", Assert.Single(paragraph.Content).Text);
	}

	[Fact]
	public void Two_trailing_spaces_make_a_line_break()
	{
		var paragraph = Single<MarkdownParagraph>("first  \nsecond");

		Assert.Equal("first\nsecond", Assert.Single(paragraph.Content).Text);
	}

	[Fact]
	public void A_blank_line_separates_paragraphs()
	{
		IReadOnlyList<MarkdownBlock> blocks = MarkdownDocument.Parse("one\n\n\ntwo");

		Assert.Equal(2, blocks.Count);
		Assert.All(blocks, block => Assert.IsType<MarkdownParagraph>(block));
	}

	[Fact]
	public void Emphasis_code_and_links_become_styled_runs()
	{
		IReadOnlyList<MarkdownInline> runs =
			MarkdownDocument.ParseInlines("a **bold** and *slanted* word, `code`, and [a link](https://example.org/x).");

		Assert.Contains(runs, run => run is { Text: "bold", Style: InlineStyle.Bold });
		Assert.Contains(runs, run => run is { Text: "slanted", Style: InlineStyle.Italic });
		Assert.Contains(runs, run => run is { Text: "code", Style: InlineStyle.Code });
		Assert.Contains(runs, run => run is { Text: "a link", Style: InlineStyle.Link, Url: "https://example.org/x" });
		Assert.Equal(
			"a bold and slanted word, code, and a link.",
			string.Concat(runs.Select(run => run.Text)));
	}

	[Fact]
	public void Emphasis_inside_a_link_keeps_both_styles()
	{
		MarkdownInline run = Assert.Single(MarkdownDocument.ParseInlines("[**GitHub**](https://github.com)"));

		Assert.Equal(InlineStyle.Bold | InlineStyle.Link, run.Style);
		Assert.Equal("https://github.com", run.Url);
	}

	[Fact]
	public void A_link_title_is_not_part_of_the_address()
	{
		MarkdownInline run = Assert.Single(MarkdownDocument.ParseInlines("[text](https://example.org \"Title\")"));

		Assert.Equal("https://example.org", run.Url);
	}

	[Theory]
	[InlineData("see https://example.org/page.", "https://example.org/page")]
	[InlineData("see https://example.org/a, then", "https://example.org/a")]
	[InlineData("see <https://example.org/a> now", "https://example.org/a")]
	public void A_web_address_is_a_link_without_the_punctuation_after_it(string text, string address)
	{
		MarkdownInline link = Assert.Single(MarkdownDocument.ParseInlines(text), run => run.Style.HasFlag(InlineStyle.Link));

		Assert.Equal(address, link.Url);
		Assert.Equal(address, link.Text);
	}

	[Theory]
	[InlineData("snake_case_name")]
	[InlineData("2 * 3 * 4")]
	[InlineData("a * b")]
	public void A_mark_that_is_not_emphasis_stays_as_it_is(string text)
	{
		MarkdownInline run = Assert.Single(MarkdownDocument.ParseInlines(text));

		Assert.Equal(text, run.Text);
		Assert.Equal(InlineStyle.None, run.Style);
	}

	[Fact]
	public void An_escaped_mark_is_plain_text() =>
		Assert.Equal("*not slanted*", Assert.Single(MarkdownDocument.ParseInlines("\\*not slanted\\*")).Text);

	[Fact]
	public void Code_is_shown_as_it_is()
	{
		MarkdownInline run = Assert.Single(MarkdownDocument.ParseInlines("`a * b _ c`"));

		Assert.Equal(InlineStyle.Code, run.Style);
		Assert.Equal("a * b _ c", run.Text);
	}

	[Fact]
	public void A_fence_keeps_its_lines_and_language()
	{
		var code = Single<MarkdownCode>("```kotlin\nval a = 1\n\n# not a heading\n```");

		Assert.Equal("kotlin", code.Language);
		Assert.Equal("val a = 1\n\n# not a heading", code.Text);
	}

	[Fact]
	public void An_unclosed_fence_runs_to_the_end()
	{
		var code = Single<MarkdownCode>("```\nline one\nline two");

		Assert.Equal("line one\nline two", code.Text);
	}

	[Fact]
	public void A_list_has_its_items_and_a_wrapped_item_is_joined()
	{
		var list = Single<MarkdownList>("- one\n- two\n  continued\n- three");

		Assert.False(list.Ordered);
		Assert.Equal(3, list.Items.Count);
		Assert.Equal("two continued", string.Concat(list.Items[1].Select(item => item.Text)));
	}

	[Fact]
	public void An_ordered_list_keeps_its_first_number()
	{
		var list = Single<MarkdownList>("3. three\n4. four");

		Assert.True(list.Ordered);
		Assert.Equal(3, list.Start);
		Assert.Equal(2, list.Items.Count);
	}

	[Fact]
	public void A_rule_is_not_a_list()
	{
		IReadOnlyList<MarkdownBlock> blocks = MarkdownDocument.Parse("above\n\n---\n\nbelow");

		Assert.IsType<MarkdownRule>(blocks[1]);
	}

	[Fact]
	public void A_quotation_holds_blocks()
	{
		var quote = Single<MarkdownQuote>("> first\n>\n> second");

		Assert.Equal(2, quote.Blocks.Count);
	}

	[Fact]
	public void A_comment_is_not_shown()
	{
		IReadOnlyList<MarkdownBlock> blocks = MarkdownDocument.Parse("before\n\n<!-- for the author\nonly -->\n\nafter");

		Assert.Equal("before\n\nafter", MarkdownDocument.PlainText(blocks));
	}

	[Fact]
	public void Windows_line_endings_and_a_byte_order_mark_are_ignored()
	{
		IReadOnlyList<MarkdownBlock> blocks = MarkdownDocument.Parse("﻿# Title\r\n\r\ntext\r\nmore");

		Assert.Equal("Title\n\ntext more", MarkdownDocument.PlainText(blocks));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   \n\n  ")]
	public void Nothing_is_an_empty_document(string? text) =>
		Assert.Empty(MarkdownDocument.Parse(text));

	[Fact]
	public void An_image_is_shown_as_its_description()
	{
		MarkdownInline run = Assert.Single(MarkdownDocument.ParseInlines("![the map](map.png)"));

		Assert.Equal("the map", run.Text);
		Assert.Equal(InlineStyle.None, run.Style);
	}
}

/// <summary>The README is the About page: it has to read well in this reader.</summary>
public sealed class ReadmeTests
{
	private static string? ReadmePath()
	{
		string? directory = AppContext.BaseDirectory;

		while (directory is not null)
		{
			if (File.Exists(Path.Combine(directory, "DDjourneys.slnx")))
			{
				string path = Path.Combine(directory, "README.md");

				return File.Exists(path) ? path : null;
			}

			directory = Path.GetDirectoryName(directory);
		}

		return null;
	}

	private static IReadOnlyList<MarkdownBlock> Read()
	{
		string path = ReadmePath() ?? throw new FileNotFoundException("README.md next to DDjourneys.slnx");

		return MarkdownDocument.Parse(File.ReadAllText(path));
	}

	[Fact]
	public void It_starts_with_the_name_of_the_app()
	{
		var title = Assert.IsType<MarkdownHeading>(Read()[0]);

		Assert.Equal(1, title.Level);
		Assert.Equal("DDjourneys", string.Concat(title.Content.Select(item => item.Text)));
	}

	[Fact]
	public void It_is_prose_with_headings_only()
	{
		IReadOnlyList<MarkdownBlock> blocks = Read();

		Assert.DoesNotContain(blocks, block => block is MarkdownList or MarkdownQuote or MarkdownRule);
		Assert.Contains(blocks, block => block is MarkdownHeading { Level: 2 });
		Assert.True(blocks.OfType<MarkdownParagraph>().Count() >= 10);
	}

	[Fact]
	public void It_has_no_emphasis_outside_code()
	{
		foreach (MarkdownParagraph paragraph in Read().OfType<MarkdownParagraph>())
		{
			Assert.DoesNotContain(
				paragraph.Content,
				run => run.Style.HasFlag(InlineStyle.Bold) || run.Style.HasFlag(InlineStyle.Italic));
		}
	}

	[Fact]
	public void Every_link_leaves_the_app_through_the_web()
	{
		foreach (MarkdownInline run in Read().OfType<MarkdownParagraph>().SelectMany(item => item.Content).Where(item => item.Url is not null))
		{
			Assert.StartsWith("https://", run.Url, StringComparison.Ordinal);
		}
	}
}
