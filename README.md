
# Notion automated text recognition

This project adds OCR to your Notion pages using Docker. It supports two OCR backends: **Microsoft Azure Computer Vision** and **IronOCR** (sponsored by [IronSoftware](https://ironsoftware.com/)).

![gif of automatic text recognition by paragraph in Notion](https://i.imgur.com/zYBe4r3.gif)
![gif of automatic text recognition by caption in Notion - From Marc GUYARD](https://i.imgur.com/jl3w1Ji.gif)

## Preconditions

* Notion account and an integration API key: https://developers.notion.com/docs/getting-started
* A [Notion database](https://developers.notion.com/docs/working-with-databases) with the `Created time` or `OCR Parsing` (checkbox) property
* **Azure** (default): Microsoft Azure account with a Computer Vision resource (see [below](#creating-a-microsoft-api-key))
* **IronOCR**: An [IronOCR license key](https://ironsoftware.com/csharp/ocr/licensing/)

## Installation

### Docker example — Azure (default)
```sh
docker run --name notion-ocr \
  -e NOTION_TOKEN=secret_12345678 \
  -e DATABASE_ID=d82973h2kwldj20239e1 \
  -e MICROSOFT_API_KEY=9834023jdsadlawdkwn \
  -e MICROSOFT_ENDPOINT=https://[YOUR_NAME].cognitiveservices.azure.com/ \
  -e SCAN_METHOD=checkbox \
  -e SCAN_FREQUENCY=15 \
  michielvanbeers/notion-auto-ocr
```

### Docker example — IronOCR
```sh
docker run --name notion-ocr \
  --platform linux/amd64 \
  -e NOTION_TOKEN=secret_12345678 \
  -e DATABASE_ID=d82973h2kwldj20239e1 \
  -e OCR_PROVIDER=ironocr \
  -e IRONOCR_LICENSE_KEY=IRONOCR-LICENSEKEY-12345 \
  -e SCAN_METHOD=checkbox \
  -e SCAN_FREQUENCY=15 \
  michielvanbeers/notion-auto-ocr
```

On Apple Silicon hosts, IronOCR container runs are supported via Linux x64 containers (`--platform linux/amd64`). Native host execution (`dotnet run`) is the preferred IronOCR development loop.

## Support matrix

| Host | Execution mode | Azure | IronOCR | Status |
|---|---|---|---|---|
| macOS ARM | Native (`dotnet run`) | Yes | Yes | Supported |
| macOS ARM | VS Code Docker F5 | Yes | Not yet reliable | Experimental |
| macOS ARM | Docker/Compose targeting Linux x64 | Yes | Yes | Supported |
| Linux x64 | Docker/Compose/CI | Yes | Yes | Primary container target |
| Linux ARM64 | Docker/Compose/CI | Yes | No validated IronOCR support | Unsupported for IronOCR |

### Recommended IronOCR workflows

1. Use native execution on Apple Silicon for day-to-day IronOCR debugging.
2. Validate IronOCR container behavior on Linux x64 (or `linux/amd64` from Apple Silicon).
3. Treat VS Code Docker F5 for IronOCR on Apple Silicon as experimental.

### Docker Compose example
```yaml
services:
  notion-auto-ocr:
    image: michielvanbeers/notion-auto-ocr
    restart: unless-stopped
    environment:
      - NOTION_TOKEN=secret_12345678
      - DATABASE_ID=d82973h2kwldj20239e1
      - OCR_PROVIDER=azure          # or ironocr
      - MICROSOFT_API_KEY=9834023jdsadlawdkwn
      - MICROSOFT_ENDPOINT=https://[YOUR_NAME].cognitiveservices.azure.com/
      - SCAN_METHOD=checkbox
      - SCAN_FREQUENCY=15           # optional
      - DEBUG=true                  # optional
```

### Environment variables

| Variable | Required | Default | Description |
|---|---|---|---|
| `NOTION_TOKEN` | Yes | — | Notion integration API token |
| `DATABASE_ID` | Yes | — | ID of the Notion database to scan |
| `SCAN_METHOD` | Yes | — | `checkbox` or `createtime` |
| `OCR_PROVIDER` | No | `azure` | `azure` or `ironocr` |
| `MICROSOFT_API_KEY` | If `OCR_PROVIDER=azure` | — | Azure Computer Vision API key |
| `MICROSOFT_ENDPOINT` | If `OCR_PROVIDER=azure` | — | Azure Computer Vision endpoint URL |
| `IRONOCR_LICENSE_KEY` | If `OCR_PROVIDER=ironocr` | — | IronOCR license key |
| `SCAN_FREQUENCY` | No | — | Scan interval in minutes. Omit for a single run |
| `DEBUG` | No | `false` | Set to `true` to enable debug logging |

## Usage

Add `ocr_text` as the caption of any image block in a Notion page that belongs to your database. On the next scan the text will be extracted and the caption will be replaced with the OCR result (or a new paragraph block will be appended, depending on how you've configured your database).

When `SCAN_FREQUENCY` is set the container loops indefinitely, rescanning at the given interval. Without it, the container performs one scan and exits — useful for testing or cron-based scheduling.

## Version history

| Tag | Runtime | Notes |
|---|---|---|
| `latest` / `v2` | C# .NET 10 | Current release — supports Azure and IronOCR |
| `v1` | Python 3.10 | Legacy — Azure only, no new features |

## Creating a Microsoft API key

This section assumes you already have a Microsoft Azure account.

1. Go to https://portal.azure.com/
2. Click **Create a resource** and search for **Computer Vision**
3. Click **Create**
4. Set Subscription, Resource group, Region, a Name of your choice, and **Pricing tier Free F0**
5. Click **Review + create**

The API key and endpoint URL are shown under **Keys and Endpoint** in the resource overview.

## Acknowledgements

- Inspired by [yannick-cw/notion-ocr](https://github.com/yannick-cw/notion-ocr)
- `SCAN_METHOD` and caption replacement by [Marc GUYARD](https://github.com/mguyard)
- IronOCR integration sponsored by [IronSoftware](https://ironsoftware.com/)
