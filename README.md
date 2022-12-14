
# Notion automated text recognition

This project lets you add text recognition to your Notion through the use of the Microsoft Vision API and Docker.

![gif of automatic text recognition in Notion](https://i.imgur.com/zYBe4r3.gif)
## Preconditions
To get started, you need the following:
* Notion account: https://www.notion.so/
* Notion integration and API key: https://developers.notion.com/docs/getting-started
* The ID of a [Notion database](https://developers.notion.com/docs/working-with-databases) that has the `Created Time` field (see Properties > Advanced)
* Microsoft Azure account: https://azure.microsoft.com/en-gb/free/cognitive-services/
* Microsoft Computer Vision API key (see [below](#creating-a-microsoft-api-key))
## Installation

This project can installed using [Docker](https://docs.docker.com/get-docker/) or [Docker Compose](https://docs.docker.com/compose/).

### Docker example
`$ docker run --name some-name-for-your-container -e NOTION_TOKEN=secret_12345678 -e DATABASE_ID=d82973h2kwldj20239e1 -e MICROSOFT_API_KEY=9834023jdsadlawdkwn -e MICROSOFT_ENDPOINT=https://[YOUR_NAME].cognitiveservices.azure.com/ -e SCAN_FREQUENCY=15 michielvanbeers/notion-auto-ocr`

### Docker compose example
```yaml
version: '3'

services:
  notion-auto-ocr:
    image: michielvanbeers/notion-auto-ocr
    restart: unless-stopped
    environment:
      - NOTION_TOKEN=secret_12345678
      - DATABASE_ID=d82973h2kwldj20239e1
      - MICROSOFT_API_KEY=9834023jdsadlawdkwn
      - MICROSOFT_ENDPOINT=https://[YOUR_NAME].cognitiveservices.azure.com/
      - SCAN_FREQUENCY=15 # Optional 
```

### Environment variables
* **NOTION_TOKEN**: API token to integrate with Notion. Don't forget to allow your intergration access to your database
* **DATABASE_ID**: ID of the database that you want to scan
* **MICROSOFT_API_KEY**: Key to access the Microsofy Computer Vision API
* **MICROSOFT_ENDPOINT**: Url of your API end-point. Can be retrieved from the Azure Portal
* **SCAN_FREQUENCY**: Optional parameter to set the frequency of scanning for new images to parse. Leave out to do a single run (recommended for testing)

## Usage
The script checks if it can find the 'ocr_text' text under an image. If it does, it will send the content of the image to Microsoft OCR API and add the result at the end of the current document. When the 'SCAN_FREQUENCY' environment variable is set, it will check if there are any new pages added since the last scan (through the use of setting timestamps).
    
## Creating a Microsoft API key
This section assumes that you already have a Microsoft Azure account. To create your (free) API resource, take the following steps:
1. Go to https://portal.azure.com/
2. Click `Create a resource`
3. Search for `Computer Vision`
4. Click on `Computer Vision`
5. Click `Create`
6. Set the following:
    - Subscription
    - Resource group (or create new one)
    - Region (nearest region near you)
    - Name (name of your liking)
    - Pricing tier (Free F0)
    - Click the terms checkbox
7. Click `Review + create`

## Acknowledgements

 - This project is heavily inspired by [yannick-cw/notion-ocr](https://github.com/yannick-cw/notion-ocr)
