namespace NotionAutoOcr;

public enum OcrProviderType { Azure, IronOcr }
public enum ScanMethodType { Checkbox, CreateTime }

public class Config
{
    public string NotionToken { get; }
    public string DatabaseId { get; }
    public ScanMethodType ScanMethod { get; }
    public int? ScanFrequency { get; }
    public bool Debug { get; }

    public OcrProviderType OcrProvider { get; }

    // Azure
    public string? MicrosoftApiKey { get; }
    public string? MicrosoftEndpoint { get; }

    // IronOCR
    public string? IronOcrLicenseKey { get; }

    public Config()
    {
        NotionToken = Require("NOTION_TOKEN");
        DatabaseId = Require("DATABASE_ID");

        var scanMethodRaw = Require("SCAN_METHOD");
        ScanMethod = scanMethodRaw.ToLowerInvariant() switch
        {
            "checkbox"   => ScanMethodType.Checkbox,
            "createtime" => ScanMethodType.CreateTime,
            _ => throw new InvalidOperationException(
                $"SCAN_METHOD must be 'checkbox' or 'createtime', got '{scanMethodRaw}'.")
        };

        var scanFrequencyRaw = Environment.GetEnvironmentVariable("SCAN_FREQUENCY");
        if (scanFrequencyRaw is not null)
        {
            if (!int.TryParse(scanFrequencyRaw, out var freq) || freq <= 0)
                throw new InvalidOperationException(
                    $"SCAN_FREQUENCY must be a positive integer, got '{scanFrequencyRaw}'.");
            ScanFrequency = freq;
        }

        var debugRaw = Environment.GetEnvironmentVariable("DEBUG") ?? "false";
        Debug = string.Equals(debugRaw, "true", StringComparison.OrdinalIgnoreCase);

        var ocrProviderRaw = Environment.GetEnvironmentVariable("OCR_PROVIDER") ?? "azure";
        OcrProvider = ocrProviderRaw.ToLowerInvariant() switch
        {
            "azure"   => OcrProviderType.Azure,
            "ironocr" => OcrProviderType.IronOcr,
            _ => throw new InvalidOperationException(
                $"OCR_PROVIDER must be 'azure' or 'ironocr', got '{ocrProviderRaw}'.")
        };

        if (OcrProvider == OcrProviderType.Azure)
        {
            MicrosoftApiKey  = Require("MICROSOFT_API_KEY");
            MicrosoftEndpoint = Require("MICROSOFT_ENDPOINT");
        }

        if (OcrProvider == OcrProviderType.IronOcr)
        {
            IronOcrLicenseKey = Require("IRONOCR_LICENSE_KEY");
        }

    }

    private static string Require(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Required environment variable '{name}' is missing or empty.");
        return value;
    }
}
