using Azure;
using Azure.AI.Vision.ImageAnalysis;
using Microsoft.Extensions.Logging;

namespace NotionAutoOcr.Ocr;

public class AzureOcrProvider : IOcrProvider
{
    private readonly ImageAnalysisClient _client;
    private readonly ILogger<AzureOcrProvider> _logger;

    public AzureOcrProvider(Config config, ILogger<AzureOcrProvider> logger)
        : this(
            new ImageAnalysisClient(
                new Uri(config.MicrosoftEndpoint!),
                new AzureKeyCredential(config.MicrosoftApiKey!)),
            logger)
    {
        logger.LogWarning(
            "Azure Image Analysis v4.0 is deprecated (EOL: September 2028). " +
            "See https://learn.microsoft.com/azure/ai-services/computer-vision/migration-options");
    }

    // Internal constructor for unit tests — accepts a pre-built (substituable) client.
    internal AzureOcrProvider(ImageAnalysisClient client, ILogger<AzureOcrProvider> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<string[]> ReadTextAsync(byte[] imageBytes)
    {
        var result = await _client.AnalyzeAsync(
            BinaryData.FromBytes(imageBytes),
            VisualFeatures.Read,
            new ImageAnalysisOptions(),
            CancellationToken.None);

        var lines = result.Value.Read?.Blocks
            .SelectMany(b => b.Lines)
            .Select(l => l.Text)
            .ToArray() ?? [];

        _logger.LogDebug("Azure OCR extracted {Count} line(s).", lines.Length);
        return lines;
    }
}
