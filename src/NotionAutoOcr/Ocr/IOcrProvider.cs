namespace NotionAutoOcr.Ocr;

public interface IOcrProvider
{
    Task<string[]> ReadTextAsync(byte[] imageBytes);
}
