using IronOcr;
using Microsoft.Extensions.Logging;

namespace NotionAutoOcr.Ocr;

public class IronOcrProvider : IOcrProvider
{
    private const int ReadTimeoutMs = 30_000;

    private readonly IronTesseract _ocr;
    private readonly ILogger<IronOcrProvider> _logger;

    public IronOcrProvider(Config config, ILogger<IronOcrProvider> logger)
    {
        _logger = logger;

        IronOcr.License.LicenseKey = config.IronOcrLicenseKey!;
        _logger.LogInformation(
            "IronOCR license key is set — licensed: {IsLicensed}",
            IronOcr.License.IsLicensed);

        _ocr = new IronTesseract { Language = OcrLanguage.English };
    }

    public async Task<string[]> ReadTextAsync(byte[] imageBytes)
    {
        using var input = new OcrInput();
        input.LoadImage(imageBytes, default);
        var result = await _ocr.ReadAsync(input, ReadTimeoutMs);

        var lines = result.Lines
            .Select(l => l.Text)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToArray();

        _logger.LogDebug("IronOCR extracted {Count} line(s).", lines.Length);
        return lines;
    }
}
