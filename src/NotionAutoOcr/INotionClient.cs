using NotionAutoOcr.Models;

namespace NotionAutoOcr;

public interface INotionClient
{
    Task<List<string>> GetPagesToScanAsync(CancellationToken ct);
    Task<List<ImageBlock>> GetImageBlocksInPageAsync(string pageId, CancellationToken ct);
    Task UpdateImageCaptionAsync(ImageBlock image, CancellationToken ct);
    Task AppendTextToPageAsync(string pageId, string[] lines, CancellationToken ct);
    Task DeleteBlockAsync(string blockId, CancellationToken ct);
    Task UnsetOcrParsingAsync(string pageId, CancellationToken ct);
}
