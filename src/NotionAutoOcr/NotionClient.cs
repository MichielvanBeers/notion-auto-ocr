using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using NotionAutoOcr.Models;

namespace NotionAutoOcr;

public class NotionClient(HttpClient http, Config config, ILogger<NotionClient> logger) : INotionClient
{
    private const string ApiBase = "https://api.notion.com/v1";

    internal static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    // ── Public API ───────────────────────────────────────────────────────────

    public async Task<List<string>> GetPagesToScanAsync(CancellationToken ct)
    {
        var url = $"{ApiBase}/databases/{config.DatabaseId}/query";
        var body = BuildScanRequestBody();

        if (config.Debug)
            logger.LogDebug("Database query body: {Body}", body.ToJsonString());

        using var response = await http.PostAsync(url, ToJsonContent(body), ct);
        await EnsureSuccessAsync(response, "database query");

        var result = await response.Content.ReadFromJsonAsync<NotionQueryResult>(JsonOpts, ct)
            ?? throw new InvalidOperationException("Empty response from database query.");

        logger.LogInformation("Found {Count} page(s) matching scan criteria.", result.Results.Count);
        return result.Results.Select(p => p.Id).ToList();
    }

    public Task<List<ImageBlock>> GetImageBlocksInPageAsync(string pageId, CancellationToken ct)
        => GetImageBlocksCoreAsync(pageId, ct, isSub: false);

    public async Task UpdateImageCaptionAsync(ImageBlock image, CancellationToken ct)
    {
        var url = $"{ApiBase}/blocks/{image.OcrBlockId}";

        // Clone the caption array so we have an unparented JsonArray to mutate freely.
        var caption = JsonNode.Parse(image.CaptionFullContent!.ToJsonString())!.AsArray();

        var fullText = string.Join("\n", image.Text);

        // Determine insert position: if ocr_text starts at position 0 of the caption
        // entry's plain_text, insert AT captionIndex; otherwise insert after it.
        var captionPlain = image.Caption!.Replace("\n", "");
        int baseIndex = captionPlain.LastIndexOf("ocr_text", StringComparison.Ordinal) == 0
            ? image.CaptionIndex
            : image.CaptionIndex + 1;

        // Remove "ocr_text" from the original caption entry's text.
        var entry = caption[image.CaptionIndex]?.AsObject();
        if (entry is not null)
        {
            if (entry["text"] is JsonObject textNode && textNode["content"] is JsonValue cv)
                textNode["content"] = cv.GetValue<string>().Replace("ocr_text", "");
            if (entry["plain_text"] is JsonValue pv)
                entry["plain_text"] = pv.GetValue<string>().Replace("ocr_text", "");
        }

        // Insert segments in reverse order at baseIndex to preserve final ordering.
        var segments = BuildTextSegments(fullText);
        foreach (var segment in ((IEnumerable<string>)segments).Reverse())
        {
            var node = new JsonObject
            {
                ["type"] = "text",
                ["text"] = new JsonObject { ["content"] = segment },
                ["plain_text"] = segment,
            };
            caption.Insert(baseIndex, node);
        }

        var patchBody = new JsonObject
        {
            ["image"] = new JsonObject { ["caption"] = caption }
        };

        using var response = await http.PatchAsync(url, ToJsonContent(patchBody), ct);
        await EnsureSuccessAsync(response, $"update caption for block {image.OcrBlockId}");
        logger.LogInformation("Updated caption on image block {Id}.", image.OcrBlockId);
    }

    public async Task AppendTextToPageAsync(string pageId, string[] lines, CancellationToken ct)
    {
        var url = $"{ApiBase}/blocks/{pageId}/children";

        var children = new JsonArray();
        foreach (var line in lines)
        {
            children.Add(new JsonObject
            {
                ["object"] = "block",
                ["type"] = "paragraph",
                ["paragraph"] = new JsonObject
                {
                    ["rich_text"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"] = "text",
                            ["text"] = new JsonObject { ["content"] = line }
                        }
                    }
                }
            });
        }

        var body = new JsonObject { ["children"] = children };
        using var response = await http.PatchAsync(url, ToJsonContent(body), ct);
        await EnsureSuccessAsync(response, $"append text to page {pageId}");
        logger.LogInformation("Appended {Count} paragraph(s) to page {PageId}.", lines.Length, pageId);
    }

    public async Task DeleteBlockAsync(string blockId, CancellationToken ct)
    {
        var url = $"{ApiBase}/blocks/{blockId}";
        using var request = new HttpRequestMessage(HttpMethod.Delete, url);
        using var response = await http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, $"delete block {blockId}");
        logger.LogDebug("Deleted block {Id}.", blockId);
    }

    public async Task UnsetOcrParsingAsync(string pageId, CancellationToken ct)
    {
        var url = $"{ApiBase}/pages/{pageId}";
        var body = new JsonObject
        {
            ["properties"] = new JsonObject
            {
                ["OCR Parsing"] = new JsonObject { ["checkbox"] = false }
            }
        };
        using var response = await http.PatchAsync(url, ToJsonContent(body), ct);
        await EnsureSuccessAsync(response, $"unset OCR Parsing on page {pageId}");
        logger.LogInformation("Unset OCR Parsing flag on page {PageId}.", pageId);
    }

    // ── Private implementation ───────────────────────────────────────────────

    private async Task<List<ImageBlock>> GetImageBlocksCoreAsync(
        string pageId, CancellationToken ct, bool isSub)
    {
        var url = $"{ApiBase}/blocks/{pageId}/children?page_size=100";
        using var response = await http.GetAsync(url, ct);
        await EnsureSuccessAsync(response, $"blocks for {pageId}");

        var data = await response.Content.ReadFromJsonAsync<NotionBlocksResult>(JsonOpts, ct)
            ?? throw new InvalidOperationException($"Empty response for blocks of {pageId}.");

        var imageBlocks = new List<ImageBlock>();
        var currentLevelImagesByIndex = new Dictionary<int, ImageBlock>();

        for (int index = 0; index < data.Results.Count; index++)
        {
            var block = data.Results[index];

            if (config.Debug)
                logger.LogDebug("Analyzing block {Id} ({Type})", block.Id, block.Type);

            if (block.HasChildren)
            {
                if (config.Debug)
                    logger.LogDebug("Block {Id} has children, scanning recursively.", block.Id);
                var children = await GetImageBlocksCoreAsync(block.Id, ct, isSub: true);
                imageBlocks.AddRange(children);
            }

            if (block.Type == "image" && block.Image is not null)
            {
                var imageUrl = block.Image.File?.Url ?? block.Image.External?.Url;
                if (imageUrl is null)
                {
                    logger.LogWarning("Image block {Id} has no accessible URL, skipping.", block.Id);
                    continue;
                }

                string? captionText = null;
                int captionIndex = 0;
                bool ocrEnabled = false;

                var caption = block.Image.Caption;
                if (caption is { Count: > 0 })
                {
                    for (int ci = 0; ci < caption.Count; ci++)
                    {
                        var plain = caption[ci]?["plain_text"]?.GetValue<string>();
                        if (plain is not null && plain.Split('\n').Contains("ocr_text"))
                        {
                            captionIndex = ci;
                            captionText = plain;
                            ocrEnabled = true;
                            break;
                        }
                    }
                }

                var imageBlock = new ImageBlock
                {
                    ImageUrl = imageUrl,
                    OcrBlockId = block.Id,
                    ListIndex = index,
                    CaptionIndex = captionIndex,
                    // Clone into an unparented JsonArray so it can be mutated and re-serialized.
                    CaptionFullContent = caption is { Count: > 0 }
                        ? JsonNode.Parse(caption.ToJsonString())!.AsArray()
                        : null,
                    Caption = captionText,
                    Ocr = ocrEnabled,
                };

                imageBlocks.Add(imageBlock);
                currentLevelImagesByIndex[index] = imageBlock;
            }

            if (block.Type == "paragraph" && block.Paragraph?.RichText is { Count: > 0 })
            {
                var plain = block.Paragraph.RichText[0].PlainText;
                if (plain == "ocr_text")
                {
                    currentLevelImagesByIndex.TryGetValue(index - 1, out var preceding);
                    if (preceding is not null && preceding.Caption is null)
                    {
                        if (config.Debug)
                            logger.LogDebug(
                                "Found 'ocr_text' paragraph (block {PId}) after image at index {Index}.",
                                block.Id, index);
                        preceding.Ocr = true;
                        preceding.OcrBlockId = block.Id;
                    }
                }
            }
        }

        if (isSub)
            return imageBlocks;

        var ocrBlocks = imageBlocks.Where(b => b.Ocr).ToList();
        logger.LogInformation(
            "Found {Count} image block(s) to OCR in page {PageId}.", ocrBlocks.Count, pageId);
        return ocrBlocks;
    }

    private JsonObject BuildScanRequestBody()
    {
        return config.ScanMethod switch
        {
            ScanMethodType.Checkbox => new JsonObject
            {
                ["page_size"] = 20,
                ["filter"] = new JsonObject
                {
                    ["property"] = "OCR Parsing",
                    ["checkbox"] = new JsonObject { ["equals"] = true }
                }
            },

            ScanMethodType.CreateTime when config.ScanFrequency is not null
                => BuildCreateTimeWithFrequency(),

            ScanMethodType.CreateTime => new JsonObject
            {
                ["page_size"] = 20,
                ["sorts"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["property"] = "Created time",
                        ["direction"] = "descending"
                    }
                }
            },

            _ => throw new InvalidOperationException(
                $"Unhandled ScanMethod: {config.ScanMethod}")
        };
    }

    private JsonObject BuildCreateTimeWithFrequency()
    {
        var cutoff = DateTime.UtcNow
            .Subtract(TimeSpan.FromMinutes(config.ScanFrequency!.Value + 1))
            .ToString("o");

        return new JsonObject
        {
            ["page_size"] = 20,
            ["filter"] = new JsonObject
            {
                ["timestamp"] = "created_time",
                ["created_time"] = new JsonObject { ["after"] = cutoff }
            },
            ["sorts"] = new JsonArray
            {
                new JsonObject
                {
                    ["property"] = "Created time",
                    ["direction"] = "descending"
                }
            }
        };
    }

    internal static List<string> BuildTextSegments(string fullText)
    {
        const int maxLen = 2000;
        var segments = new List<string> { "\n*********************\n" };

        if (fullText.Length > maxLen)
        {
            int start = 0;
            while (start < fullText.Length)
            {
                int remaining = fullText.Length - start;
                if (remaining <= maxLen)
                {
                    segments.Add(fullText[start..]);
                    break;
                }

                int end = start + 1999;
                int spaceIdx = fullText.LastIndexOf(' ', end, end - start + 1);
                if (spaceIdx > start)
                {
                    segments.Add(fullText[start..spaceIdx]);
                    start = spaceIdx + 1;
                }
                else
                {
                    // No space found in window — hard cut to avoid infinite loop.
                    segments.Add(fullText[start..end]);
                    start = end;
                }
            }
        }
        else
        {
            segments.Add(fullText);
        }

        segments.Add("\n*********************");
        return segments;
    }

    private static StringContent ToJsonContent(JsonObject body)
        => new(body.ToJsonString(), Encoding.UTF8, "application/json");

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string context)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException(
                $"Notion API error during {context}: {(int)response.StatusCode} — {body}");
        }
    }
}
