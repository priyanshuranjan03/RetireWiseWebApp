using Markdig;
using Microsoft.AspNetCore.Html;

namespace RetireWiseWebApp.Helpers;

public static class MarkdownHelper
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    public static HtmlString ToHtml(this string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return new HtmlString(string.Empty);

        var html = Markdown.ToHtml(markdown, Pipeline);
        return new HtmlString(html);
    }

    public static string ToHtmlString(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return string.Empty;

        return Markdown.ToHtml(markdown, Pipeline);
    }
}