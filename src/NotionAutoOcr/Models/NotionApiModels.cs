using System.Text.Json.Nodes;

namespace NotionAutoOcr.Models;

internal record NotionQueryResult(List<NotionPage> Results);

internal record NotionPage(string Id);

internal record NotionBlocksResult(List<NotionRawBlock> Results);

internal record NotionRawBlock(
    string Id,
    string Type,
    bool HasChildren,
    NotionImageData? Image,
    NotionParagraphData? Paragraph);

internal record NotionImageData(
    JsonArray? Caption,
    NotionFileRef? File,
    NotionFileRef? External);

internal record NotionFileRef(string Url);

internal record NotionParagraphData(List<NotionRichTextItem>? RichText);

internal record NotionRichTextItem(string? PlainText);
