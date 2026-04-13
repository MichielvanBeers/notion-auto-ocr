using System.Text.Json.Nodes;

namespace NotionAutoOcr.Models;

public class ImageBlock
{
    public required string ImageUrl { get; init; }
    public required string OcrBlockId { get; set; }
    public int ListIndex { get; init; }
    public int CaptionIndex { get; init; }
    public JsonArray? CaptionFullContent { get; init; }
    public string? Caption { get; init; }
    public string[] Text { get; set; } = [];
    public bool Ocr { get; set; }
}
