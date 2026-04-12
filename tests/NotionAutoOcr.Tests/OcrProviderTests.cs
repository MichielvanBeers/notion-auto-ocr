using Azure;
using Azure.AI.Vision.ImageAnalysis;
using Microsoft.Extensions.Logging;
using NotionAutoOcr;
using NotionAutoOcr.Ocr;
using NSubstitute;

namespace NotionAutoOcr.Tests;

public class AzureOcrProviderTests : IDisposable
{
    private void SetEnv()
    {
        Environment.SetEnvironmentVariable("NOTION_TOKEN", "secret_test");
        Environment.SetEnvironmentVariable("DATABASE_ID", "db_test");
        Environment.SetEnvironmentVariable("SCAN_METHOD", "checkbox");
        Environment.SetEnvironmentVariable("MICROSOFT_API_KEY", "test_key");
        Environment.SetEnvironmentVariable("MICROSOFT_ENDPOINT", "https://test.cognitive.azure.com/");
        Environment.SetEnvironmentVariable("OCR_PROVIDER", "azure");
    }

    public void Dispose()
    {
        foreach (var key in new[]
        {
            "NOTION_TOKEN", "DATABASE_ID", "SCAN_METHOD",
            "MICROSOFT_API_KEY", "MICROSOFT_ENDPOINT", "OCR_PROVIDER"
        })
            Environment.SetEnvironmentVariable(key, null);
    }

    [Fact]
    public async Task ReadTextAsync_ReturnsExtractedLines()
    {
        SetEnv();
        var config = new Config();
        var logger = Substitute.For<ILogger<AzureOcrProvider>>();

        // Build a fake ImageAnalysisResult using the model factory.
        // Positional arg order: caption, denseCaptions, metadata, modelVersion,
        //                       objects, people, read, smartCrops, tags
        var readResult = AIVisionImageAnalysisModelFactory.ReadResult(
        [
            AIVisionImageAnalysisModelFactory.DetectedTextBlock(
            [
                AIVisionImageAnalysisModelFactory.DetectedTextLine("Hello World", [], []),
                AIVisionImageAnalysisModelFactory.DetectedTextLine("Second line", [], []),
            ])
        ]);
        var fakeResult = AIVisionImageAnalysisModelFactory.ImageAnalysisResult(
            null, null, null, null, null, null, readResult, null, null);

        var mockClient = Substitute.For<ImageAnalysisClient>();
        mockClient
            .AnalyzeAsync(
                Arg.Any<BinaryData>(),
                Arg.Any<VisualFeatures>(),
                Arg.Any<ImageAnalysisOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(Response.FromValue(fakeResult, Substitute.For<Response>()));

        var provider = new AzureOcrProvider(mockClient, logger);
        var lines = await provider.ReadTextAsync([0x89, 0x50, 0x4e, 0x47]); // PNG magic bytes

        Assert.Equal(["Hello World", "Second line"], lines);
    }

    [Fact]
    public async Task ReadTextAsync_NullReadResult_ReturnsEmpty()
    {
        SetEnv();
        var config = new Config();
        var logger = Substitute.For<ILogger<AzureOcrProvider>>();

        var fakeResult = AIVisionImageAnalysisModelFactory.ImageAnalysisResult(
            null, null, null, null, null, null, null, null, null);

        var mockClient = Substitute.For<ImageAnalysisClient>();
        mockClient
            .AnalyzeAsync(
                Arg.Any<BinaryData>(),
                Arg.Any<VisualFeatures>(),
                Arg.Any<ImageAnalysisOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(Response.FromValue(fakeResult, Substitute.For<Response>()));

        var provider = new AzureOcrProvider(mockClient, logger);
        var lines = await provider.ReadTextAsync([0x00]);

        Assert.Empty(lines);
    }
}

public class IronOcrProviderTests : IDisposable
{
    private void SetEnv(string? licenseKey = "IRONOCR_TEST_KEY")
    {
        Environment.SetEnvironmentVariable("NOTION_TOKEN", "secret_test");
        Environment.SetEnvironmentVariable("DATABASE_ID", "db_test");
        Environment.SetEnvironmentVariable("SCAN_METHOD", "checkbox");
        Environment.SetEnvironmentVariable("OCR_PROVIDER", "ironocr");
        Environment.SetEnvironmentVariable("IRONOCR_LICENSE_KEY", licenseKey);
        Environment.SetEnvironmentVariable("MICROSOFT_API_KEY", null);
        Environment.SetEnvironmentVariable("MICROSOFT_ENDPOINT", null);
    }

    public void Dispose()
    {
        foreach (var key in new[]
        {
            "NOTION_TOKEN", "DATABASE_ID", "SCAN_METHOD",
            "OCR_PROVIDER", "IRONOCR_LICENSE_KEY", "MICROSOFT_API_KEY", "MICROSOFT_ENDPOINT"
        })
            Environment.SetEnvironmentVariable(key, null);
    }

    [Fact]
    public async Task KnownImage_ReturnsNonEmptyText()
    {
        // Integration test — requires a real IronOCR license key.
        // Set IRONOCR_LICENSE_KEY to a valid key to opt in.
        // Without it this test passes silently; full validation happens in Stage 3 Docker testing.
        var realKey = Environment.GetEnvironmentVariable("IRONOCR_LICENSE_KEY");
        if (string.IsNullOrEmpty(realKey))
            return;

        SetEnv(realKey);
        var config = new Config();
        var logger = Substitute.For<ILogger<IronOcrProvider>>();
        var provider = new IronOcrProvider(config, logger);

        var bytes = await File.ReadAllBytesAsync(Path.Combine("fixtures", "hello-world.png"));
        var lines = await provider.ReadTextAsync(bytes);

        Assert.NotEmpty(lines);
    }
}
