using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using NotionAutoOcr;
using NotionAutoOcr.Models;
using NSubstitute;

namespace NotionAutoOcr.Tests;

public class NotionClientTests : IDisposable
{
    // ── Environment setup ────────────────────────────────────────────────────

    private void SetEnv(string scanMethod = "checkbox", string? scanFrequency = null)
    {
        Environment.SetEnvironmentVariable("NOTION_TOKEN", "secret_test");
        Environment.SetEnvironmentVariable("DATABASE_ID", "test_db_id");
        Environment.SetEnvironmentVariable("SCAN_METHOD", scanMethod);
        Environment.SetEnvironmentVariable("SCAN_FREQUENCY", scanFrequency);
        Environment.SetEnvironmentVariable("MICROSOFT_API_KEY", "test_key");
        Environment.SetEnvironmentVariable("MICROSOFT_ENDPOINT", "https://test.cognitive.azure.com/");
        Environment.SetEnvironmentVariable("OCR_PROVIDER", "azure");
        Environment.SetEnvironmentVariable("DEBUG", null);
    }

    public void Dispose()
    {
        foreach (var key in new[]
        {
            "NOTION_TOKEN", "DATABASE_ID", "SCAN_METHOD", "SCAN_FREQUENCY",
            "DEBUG", "OCR_PROVIDER", "MICROSOFT_API_KEY", "MICROSOFT_ENDPOINT",
            "IRONOCR_LICENSE_KEY"
        })
            Environment.SetEnvironmentVariable(key, null);
    }

    private (NotionClient client, MockHttpHandler handler) CreateClient(
        string scanMethod = "checkbox", string? scanFrequency = null)
    {
        SetEnv(scanMethod, scanFrequency);
        var config = new Config();
        var handler = new MockHttpHandler();
        var http = new HttpClient(handler);
        http.DefaultRequestHeaders.Add("Notion-Version", "2022-06-28");
        var logger = Substitute.For<ILogger<NotionClient>>();
        return (new NotionClient(http, config, logger), handler);
    }

    private static string ReadFixture(string name)
        => File.ReadAllText(Path.Combine("fixtures", name));

    // ── GetPagesToScanAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task GetPages_NotionVersionHeader_IsSent()
    {
        var (client, handler) = CreateClient();
        handler.SetupPost(
            "https://api.notion.com/v1/databases/test_db_id/query",
            ReadFixture("database-query-response.json"));

        await client.GetPagesToScanAsync(CancellationToken.None);

        Assert.NotNull(handler.LastRequest);
        Assert.True(handler.LastRequest!.Headers.Contains("Notion-Version"));
        Assert.Equal("2022-06-28",
            handler.LastRequest.Headers.GetValues("Notion-Version").Single());
    }

    [Fact]
    public async Task GetPages_CheckboxMethod_SendsCheckboxFilter()
    {
        var (client, handler) = CreateClient(scanMethod: "checkbox");
        handler.SetupPost(
            "https://api.notion.com/v1/databases/test_db_id/query",
            ReadFixture("database-query-response.json"));

        await client.GetPagesToScanAsync(CancellationToken.None);

        var body = JsonDocument.Parse(handler.LastRequestBody!);
        var root = body.RootElement;
        Assert.True(root.GetProperty("filter").GetProperty("checkbox").GetProperty("equals").GetBoolean());
        Assert.Equal("OCR Parsing", root.GetProperty("filter").GetProperty("property").GetString());
    }

    [Fact]
    public async Task GetPages_CreateTimeMethod_SendsSortsFilter()
    {
        var (client, handler) = CreateClient(scanMethod: "createtime");
        handler.SetupPost(
            "https://api.notion.com/v1/databases/test_db_id/query",
            ReadFixture("database-query-response.json"));

        await client.GetPagesToScanAsync(CancellationToken.None);

        var body = JsonDocument.Parse(handler.LastRequestBody!);
        var root = body.RootElement;
        Assert.True(root.TryGetProperty("sorts", out var sorts));
        Assert.Equal("Created time", sorts[0].GetProperty("property").GetString());
    }

    [Fact]
    public async Task GetPages_CreateTimeWithFrequency_SendsCreatedTimeFilter()
    {
        var (client, handler) = CreateClient(scanMethod: "createtime", scanFrequency: "10");
        handler.SetupPost(
            "https://api.notion.com/v1/databases/test_db_id/query",
            ReadFixture("database-query-response.json"));

        await client.GetPagesToScanAsync(CancellationToken.None);

        var body = JsonDocument.Parse(handler.LastRequestBody!);
        var filter = body.RootElement.GetProperty("filter");
        Assert.Equal("created_time", filter.GetProperty("timestamp").GetString());
        Assert.True(filter.GetProperty("created_time").TryGetProperty("after", out _));
    }

    [Fact]
    public async Task GetPages_ReturnsPageIds()
    {
        var (client, handler) = CreateClient();
        handler.SetupPost(
            "https://api.notion.com/v1/databases/test_db_id/query",
            ReadFixture("database-query-response.json"));

        var pages = await client.GetPagesToScanAsync(CancellationToken.None);

        Assert.Equal(["page-aaa", "page-bbb"], pages);
    }

    // ── GetImageBlocksInPageAsync ────────────────────────────────────────────

    [Fact]
    public async Task GetImageBlocks_CaptionOcr_ReturnsOcrBlock()
    {
        var (client, handler) = CreateClient();
        handler.SetupGet(
            "https://api.notion.com/v1/blocks/page-1/children?page_size=100",
            ReadFixture("blocks-caption-ocr.json"));

        var blocks = await client.GetImageBlocksInPageAsync("page-1", CancellationToken.None);

        Assert.Single(blocks);
        var block = blocks[0];
        Assert.True(block.Ocr);
        Assert.Equal("block-image-caption", block.OcrBlockId);
        Assert.Equal("ocr_text", block.Caption);
        Assert.Equal(0, block.CaptionIndex);
        Assert.Equal("https://example.com/image-caption.png", block.ImageUrl);
        Assert.NotNull(block.CaptionFullContent);
    }

    [Fact]
    public async Task GetImageBlocks_ParagraphOcr_ReturnsOcrBlockWithParagraphId()
    {
        var (client, handler) = CreateClient();
        handler.SetupGet(
            "https://api.notion.com/v1/blocks/page-2/children?page_size=100",
            ReadFixture("blocks-paragraph-ocr.json"));

        var blocks = await client.GetImageBlocksInPageAsync("page-2", CancellationToken.None);

        Assert.Single(blocks);
        var block = blocks[0];
        Assert.True(block.Ocr);
        // OcrBlockId should be the paragraph block, not the image block
        Assert.Equal("block-ocr-para", block.OcrBlockId);
        Assert.Null(block.Caption);
        Assert.Null(block.CaptionFullContent);
    }

    [Fact]
    public async Task GetImageBlocks_NoOcrMarker_ReturnsEmpty()
    {
        var (client, handler) = CreateClient();
        handler.SetupGet(
            "https://api.notion.com/v1/blocks/page-3/children?page_size=100",
            ReadFixture("blocks-no-ocr.json"));

        var blocks = await client.GetImageBlocksInPageAsync("page-3", CancellationToken.None);

        Assert.Empty(blocks);
    }

    [Fact]
    public async Task GetImageBlocks_ParagraphOcr_IgnoresNestedChildImageForPrecedingMatch()
    {
        var (client, handler) = CreateClient();
        handler.SetupGet(
            "https://api.notion.com/v1/blocks/page-nested/children?page_size=100",
            ReadFixture("blocks-nested-scope-parent.json"));
        handler.SetupGet(
            "https://api.notion.com/v1/blocks/block-toggle-parent/children?page_size=100",
            ReadFixture("blocks-nested-scope-child.json"));

        var blocks = await client.GetImageBlocksInPageAsync("page-nested", CancellationToken.None);

        Assert.Single(blocks);
        var block = blocks[0];
        Assert.Equal("https://example.com/image-parent.png", block.ImageUrl);
        Assert.Equal("block-ocr-parent", block.OcrBlockId);
        Assert.True(block.Ocr);
    }

    // ── AppendTextToPageAsync ────────────────────────────────────────────────

    [Fact]
    public async Task AppendText_SendsCorrectBodyStructure()
    {
        var (client, handler) = CreateClient();
        handler.SetupPatch(
            "https://api.notion.com/v1/blocks/page-x/children",
            """{"object":"block"}""");

        await client.AppendTextToPageAsync("page-x", ["Line one", "Line two"], CancellationToken.None);

        var body = JsonDocument.Parse(handler.LastRequestBody!);
        var children = body.RootElement.GetProperty("children");
        Assert.Equal(2, children.GetArrayLength());
        Assert.Equal("paragraph", children[0].GetProperty("type").GetString());
        Assert.Equal("Line one",
            children[0].GetProperty("paragraph")
                .GetProperty("rich_text")[0]
                .GetProperty("text")
                .GetProperty("content").GetString());
    }

    // ── UnsetOcrParsingAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task UnsetOcrParsing_SendsCheckboxFalse()
    {
        var (client, handler) = CreateClient();
        handler.SetupPatch(
            "https://api.notion.com/v1/pages/page-y",
            """{}""");

        await client.UnsetOcrParsingAsync("page-y", CancellationToken.None);

        var body = JsonDocument.Parse(handler.LastRequestBody!);
        var checkbox = body.RootElement
            .GetProperty("properties")
            .GetProperty("OCR Parsing")
            .GetProperty("checkbox");
        Assert.False(checkbox.GetBoolean());
    }

    // ── BuildTextSegments ────────────────────────────────────────────────────

    [Fact]
    public void BuildTextSegments_ShortText_WrapsInDelimiters()
    {
        var segments = NotionClient.BuildTextSegments("Hello World");

        Assert.Equal(3, segments.Count);
        Assert.Equal("\n*********************\n", segments[0]);
        Assert.Equal("Hello World", segments[1]);
        Assert.Equal("\n*********************", segments[2]);
    }

    [Fact]
    public void BuildTextSegments_LongText_SplitsIntoChunks()
    {
        // Build a text longer than 2000 chars
        var longText = string.Join(" ", Enumerable.Repeat("word", 500));
        var segments = NotionClient.BuildTextSegments(longText);

        // Should start/end with delimiters and have multiple text chunks
        Assert.Equal("\n*********************\n", segments[0]);
        Assert.Equal("\n*********************", segments[^1]);
        Assert.True(segments.Count > 3);
        // Each text chunk must be <= 2000 chars
        foreach (var seg in segments[1..^1])
            Assert.True(seg.Length <= 2000, $"Chunk too long: {seg.Length}");
    }
}

// ── Mock HTTP handler ────────────────────────────────────────────────────────

internal sealed class MockHttpHandler : HttpMessageHandler
{
    private readonly Dictionary<(HttpMethod, string), string> _routes = new();

    public HttpRequestMessage? LastRequest { get; private set; }
    public string? LastRequestBody { get; private set; }

    public void SetupGet(string url, string json) => _routes[(HttpMethod.Get, url)] = json;
    public void SetupPost(string url, string json) => _routes[(HttpMethod.Post, url)] = json;
    public void SetupPatch(string url, string json) => _routes[(HttpMethod.Patch, url)] = json;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        if (request.Content is not null)
            LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);

        var key = (request.Method, request.RequestUri!.ToString());
        if (_routes.TryGetValue(key, out var body))
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };

        return new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent($"No mock for {request.Method} {request.RequestUri}")
        };
    }
}
