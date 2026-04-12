using NotionAutoOcr;

namespace NotionAutoOcr.Tests;

public class ConfigTests : IDisposable
{
    // Minimal valid env vars (Azure provider, all required set)
    private readonly Dictionary<string, string> _defaults = new()
    {
        ["NOTION_TOKEN"]       = "secret_test",
        ["DATABASE_ID"]        = "db_test",
        ["SCAN_METHOD"]        = "checkbox",
        ["MICROSOFT_API_KEY"]  = "key_test",
        ["MICROSOFT_ENDPOINT"] = "https://example.cognitiveservices.azure.com/",
    };

    private void SetEnv(Dictionary<string, string> overrides)
    {
        foreach (var kv in _defaults)
            Environment.SetEnvironmentVariable(kv.Key, kv.Value);
        foreach (var kv in overrides)
            Environment.SetEnvironmentVariable(kv.Key, kv.Value);
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

    [Fact]
    public void MissingNotionToken_Throws()
    {
        SetEnv([]);
        Environment.SetEnvironmentVariable("NOTION_TOKEN", null);
        Assert.Throws<InvalidOperationException>(() => new Config());
    }

    [Fact]
    public void MissingDatabaseId_Throws()
    {
        SetEnv([]);
        Environment.SetEnvironmentVariable("DATABASE_ID", null);
        Assert.Throws<InvalidOperationException>(() => new Config());
    }

    [Fact]
    public void OcrProvider_DefaultsToAzure()
    {
        SetEnv([]);
        Environment.SetEnvironmentVariable("OCR_PROVIDER", null);
        var config = new Config();
        Assert.Equal(OcrProviderType.Azure, config.OcrProvider);
    }

    [Fact]
    public void OcrProvider_IronOcr_RequiresLicenseKey()
    {
        SetEnv(new() { ["OCR_PROVIDER"] = "ironocr" });
        Environment.SetEnvironmentVariable("IRONOCR_LICENSE_KEY", null);
        Assert.Throws<InvalidOperationException>(() => new Config());
    }

    [Fact]
    public void OcrProvider_IronOcr_OmitsAzureRequirements()
    {
        SetEnv(new()
        {
            ["OCR_PROVIDER"]        = "ironocr",
            ["IRONOCR_LICENSE_KEY"] = "iron_key",
        });
        Environment.SetEnvironmentVariable("MICROSOFT_API_KEY", null);
        Environment.SetEnvironmentVariable("MICROSOFT_ENDPOINT", null);
        var config = new Config();
        Assert.Equal(OcrProviderType.IronOcr, config.OcrProvider);
    }

    [Fact]
    public void InvalidScanMethod_Throws()
    {
        SetEnv(new() { ["SCAN_METHOD"] = "badvalue" });
        Assert.Throws<InvalidOperationException>(() => new Config());
    }

    [Fact]
    public void ScanMethod_Checkbox_Parsed()
    {
        SetEnv([]);
        var config = new Config();
        Assert.Equal(ScanMethodType.Checkbox, config.ScanMethod);
    }

    [Fact]
    public void ScanMethod_CreateTime_Parsed()
    {
        SetEnv(new() { ["SCAN_METHOD"] = "createtime" });
        var config = new Config();
        Assert.Equal(ScanMethodType.CreateTime, config.ScanMethod);
    }

    [Fact]
    public void ScanFrequency_NonInteger_Throws()
    {
        SetEnv(new() { ["SCAN_FREQUENCY"] = "notanumber" });
        Assert.Throws<InvalidOperationException>(() => new Config());
    }

    [Fact]
    public void Debug_DefaultsFalse()
    {
        SetEnv([]);
        Environment.SetEnvironmentVariable("DEBUG", null);
        var config = new Config();
        Assert.False(config.Debug);
    }

    [Fact]
    public void Debug_TrueWhenSet()
    {
        SetEnv(new() { ["DEBUG"] = "true" });
        var config = new Config();
        Assert.True(config.Debug);
    }

}
