using Microsoft.Extensions.Logging;
using NotionAutoOcr;
using NotionAutoOcr.Models;
using NotionAutoOcr.Ocr;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace NotionAutoOcr.Tests;

public class ScanWorkerTests : IDisposable
{
    private Config CreateConfig(bool withFrequency = false)
    {
        Environment.SetEnvironmentVariable("NOTION_TOKEN", "secret_test");
        Environment.SetEnvironmentVariable("DATABASE_ID", "db_test");
        Environment.SetEnvironmentVariable("SCAN_METHOD", "checkbox");
        Environment.SetEnvironmentVariable("MICROSOFT_API_KEY", "test_key");
        Environment.SetEnvironmentVariable("MICROSOFT_ENDPOINT", "https://test.cognitive.azure.com/");
        Environment.SetEnvironmentVariable("OCR_PROVIDER", "azure");
        Environment.SetEnvironmentVariable("SCAN_FREQUENCY", withFrequency ? "60" : null);
        Environment.SetEnvironmentVariable("DEBUG", null);
        return new Config();
    }

    public void Dispose()
    {
        foreach (var key in new[]
        {
            "NOTION_TOKEN", "DATABASE_ID", "SCAN_METHOD", "SCAN_FREQUENCY",
            "DEBUG", "OCR_PROVIDER", "MICROSOFT_API_KEY", "MICROSOFT_ENDPOINT"
        })
            Environment.SetEnvironmentVariable(key, null);
    }

    private (ScanWorker worker, INotionClient notionMock, IOcrProvider ocrMock) CreateWorker(
        Config config)
    {
        var notion = Substitute.For<INotionClient>();
        var ocr = Substitute.For<IOcrProvider>();
        var httpFactory = Substitute.For<IHttpClientFactory>();
        var logger = Substitute.For<ILogger<ScanWorker>>();

        notion.GetPagesToScanAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<string>()));

        var worker = new ScanWorker(config, notion, ocr, httpFactory, logger);
        return (worker, notion, ocr);
    }

    [Fact]
    public async Task SingleRun_ExitsAfterOneScan()
    {
        var config = CreateConfig(withFrequency: false);
        var (worker, notion, _) = CreateWorker(config);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await worker.RunScanAsync(cts.Token);

        // Notion was queried exactly once and completed without hanging.
        await notion.Received(1).GetPagesToScanAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessPage_NoOcrBlocks_UnsetsFlag()
    {
        var config = CreateConfig(withFrequency: false);
        var notion = Substitute.For<INotionClient>();
        var ocr = Substitute.For<IOcrProvider>();
        var logger = Substitute.For<ILogger<ScanWorker>>();
        var httpFactory = Substitute.For<IHttpClientFactory>();

        notion.GetPagesToScanAsync(Arg.Any<CancellationToken>())
            .Returns(["page-no-blocks"]);
        notion.GetImageBlocksInPageAsync("page-no-blocks", Arg.Any<CancellationToken>())
            .Returns(new List<ImageBlock>());

        var worker = new ScanWorker(config, notion, ocr, httpFactory, logger);
        await worker.RunScanAsync(CancellationToken.None);

        await notion.Received(1).UnsetOcrParsingAsync(
            "page-no-blocks", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancellationDuringSleep_ExitsCleanly()
    {
        var config = CreateConfig(withFrequency: true);
        var (worker, notion, _) = CreateWorker(config);

        using var cts = new CancellationTokenSource();

        // Start ExecuteAsync (which will sleep for 60 min after first scan).
        var executeTask = worker.StartAsync(cts.Token);
        await executeTask;

        // Cancel immediately — should wake the delay and exit without throwing.
        await cts.CancelAsync();

        var ex = await Record.ExceptionAsync(
            () => worker.StopAsync(CancellationToken.None));

        Assert.Null(ex);
    }

    [Fact]
    public async Task ProcessPage_OcrFailure_DoesNotUnsetFlag()
    {
        var config = CreateConfig(withFrequency: false);
        var notion = Substitute.For<INotionClient>();
        var ocr = Substitute.For<IOcrProvider>();
        var logger = Substitute.For<ILogger<ScanWorker>>();

        // Notion returns one page with one image block.
        notion.GetPagesToScanAsync(Arg.Any<CancellationToken>())
            .Returns(["page-1"]);
        notion.GetImageBlocksInPageAsync("page-1", Arg.Any<CancellationToken>())
            .Returns([new ImageBlock
            {
                ImageUrl = "https://example.com/img.png",
                OcrBlockId = "block-1",
                Ocr = true,
            }]);

        // Image download returns bytes via a real HttpClient pointing at a mock handler.
        var handler = new MockHttpHandler();
        handler.SetupGet("https://example.com/img.png", "fake-image-bytes");
        var http = new System.Net.Http.HttpClient(handler);
        var httpFactory = Substitute.For<IHttpClientFactory>();
        httpFactory.CreateClient("notion-images").Returns(http);

        // OCR throws.
        ocr.ReadTextAsync(Arg.Any<byte[]>())
            .ThrowsAsync(new InvalidOperationException("OCR engine error"));

        var worker = new ScanWorker(config, notion, ocr, httpFactory, logger);
        await worker.RunScanAsync(CancellationToken.None);

        // OCR Parsing flag must NOT be unset when a block failed.
        await notion.DidNotReceive().UnsetOcrParsingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessPage_AllSuccess_UnsetsFlag()
    {
        var config = CreateConfig(withFrequency: false);
        var notion = Substitute.For<INotionClient>();
        var ocr = Substitute.For<IOcrProvider>();
        var logger = Substitute.For<ILogger<ScanWorker>>();

        notion.GetPagesToScanAsync(Arg.Any<CancellationToken>())
            .Returns(["page-2"]);
        notion.GetImageBlocksInPageAsync("page-2", Arg.Any<CancellationToken>())
            .Returns([new ImageBlock
            {
                ImageUrl = "https://example.com/img2.png",
                OcrBlockId = "block-2",
                Ocr = true,
            }]);

        var handler = new MockHttpHandler();
        handler.SetupGet("https://example.com/img2.png", "fake-image-bytes");
        var http = new System.Net.Http.HttpClient(handler);
        var httpFactory = Substitute.For<IHttpClientFactory>();
        httpFactory.CreateClient("notion-images").Returns(http);

        ocr.ReadTextAsync(Arg.Any<byte[]>())
            .Returns(["Extracted line one"]);

        var worker = new ScanWorker(config, notion, ocr, httpFactory, logger);
        await worker.RunScanAsync(CancellationToken.None);

        // Block had no caption → AppendText + DeleteBlock path.
        await notion.Received(1).AppendTextToPageAsync(
            "page-2", Arg.Any<string[]>(), Arg.Any<CancellationToken>());
        await notion.Received(1).DeleteBlockAsync("block-2", Arg.Any<CancellationToken>());
        await notion.Received(1).UnsetOcrParsingAsync("page-2", Arg.Any<CancellationToken>());
    }
}
