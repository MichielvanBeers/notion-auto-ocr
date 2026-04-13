using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NotionAutoOcr.Ocr;

namespace NotionAutoOcr;

public class ScanWorker(
    Config config,
    INotionClient notion,
    IOcrProvider ocr,
    IHttpClientFactory httpFactory,
    ILogger<ScanWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        do
        {
            await RunScanAsync(stoppingToken);

            if (config.ScanFrequency is null)
                break;

            logger.LogInformation(
                "Next scan in {Minutes} minute(s). Waiting…", config.ScanFrequency.Value);

            await Task.Delay(
                TimeSpan.FromMinutes(config.ScanFrequency.Value),
                stoppingToken);
        }
        while (!stoppingToken.IsCancellationRequested);
    }

    internal async Task RunScanAsync(CancellationToken ct)
    {
        logger.LogInformation("Starting scan…");

        var pageIds = await notion.GetPagesToScanAsync(ct);

        foreach (var pageId in pageIds)
        {
            ct.ThrowIfCancellationRequested();
            await ProcessPageAsync(pageId, ct);
        }

        logger.LogInformation("Scan complete.");
    }

    private async Task ProcessPageAsync(string pageId, CancellationToken ct)
    {
        var imageBlocks = await notion.GetImageBlocksInPageAsync(pageId, ct);

        if (imageBlocks.Count == 0)
        {
            logger.LogDebug(
                "Page {PageId}: no OCR blocks found. Unsetting OCR Parsing to match legacy behavior.",
                pageId);
        }

        int failCount = 0;

        foreach (var block in imageBlocks)
        {
            ct.ThrowIfCancellationRequested();

            logger.LogInformation(
                "Page {PageId}: downloading image for block {BlockId}…", pageId, block.OcrBlockId);

            byte[] imageBytes;
            try
            {
                var http = httpFactory.CreateClient("notion-images");
                imageBytes = await http.GetByteArrayAsync(block.ImageUrl, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Page {PageId}: failed to download image for block {BlockId}.",
                    pageId, block.OcrBlockId);
                failCount++;
                continue;
            }

            logger.LogInformation(
                "Page {PageId}: running OCR on block {BlockId} ({Bytes} bytes)…",
                pageId, block.OcrBlockId, imageBytes.Length);

            string[] lines;
            try
            {
                lines = await ocr.ReadTextAsync(imageBytes);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Page {PageId}: OCR failed for block {BlockId}.",
                    pageId, block.OcrBlockId);
                failCount++;
                continue;
            }

            if (config.Debug)
                logger.LogDebug(
                    "Page {PageId}: OCR output for block {BlockId}:{NewLine}{Text}",
                    pageId, block.OcrBlockId, Environment.NewLine, string.Join(Environment.NewLine, lines));

            block.Text = lines;

            try
            {
                if (block.Caption is null)
                {
                    await notion.AppendTextToPageAsync(pageId, lines, ct);
                    await notion.DeleteBlockAsync(block.OcrBlockId, ct);
                }
                else
                {
                    await notion.UpdateImageCaptionAsync(block, ct);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Page {PageId}: failed to write OCR result back for block {BlockId}.",
                    pageId, block.OcrBlockId);
                failCount++;
            }
        }

        if (failCount == 0)
        {
            await notion.UnsetOcrParsingAsync(pageId, ct);
        }
        else
        {
            logger.LogWarning(
                "Page {PageId}: {Count} block(s) failed — skipping OCR Parsing flag unset.",
                pageId, failCount);
        }
    }
}
