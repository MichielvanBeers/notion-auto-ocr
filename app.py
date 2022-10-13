import requests
import json
import os
import sys
import datetime
import time
from azure.cognitiveservices.vision.computervision import ComputerVisionClient
from azure.cognitiveservices.vision.computervision.models import OperationStatusCodes
from msrest.authentication import CognitiveServicesCredentials

NOTION_TOKEN = os.environ['NOTION_TOKEN']
DATABASE_ID = os.environ['DATABASE_ID']
MICROSOFT_API_KEY = os.environ['MICROSOFT_API_KEY']
MICROSOFT_ENDPOINT = os.environ['MICROSOFT_ENDPOINT']
SCAN_FREQUENCY = os.environ['SCAN_FREQUENCY'] if 'SCAN_FREQUENCY' in os.environ else None

HEADERS = {
    "Authorization": "Bearer " + NOTION_TOKEN,
    "Content-Type": "application/json",
    "Notion-Version": "2022-06-28"
}


def read_database(database_id, headers):
    read_url = f"https://api.notion.com/v1/databases/{database_id}/query"

    request_body = {
        "page_size": 20,
        "sorts": [
            {
                "property": "Created",
                "direction": "descending"
            }
        ]
    }

    if SCAN_FREQUENCY is not None:
        current_date_time = datetime.datetime.now()
        timestamp_last_pages_request = current_date_time - datetime.timedelta(minutes=(SCAN_FREQUENCY + 1))
        timestamp_last_pages_request_iso = timestamp_last_pages_request.isoformat()
        request_body = {
            "page_size": 20,
            "filter": {
                "timestamp": "created_time",
                "created_time": {
                    "after": timestamp_last_pages_request_iso
                }
            },
            "sorts": [
                {
                    "property": "Created",
                    "direction": "descending"
                }
            ]
        }

    data = json.dumps(request_body)

    print(data)

    res = requests.request("POST", read_url, headers=headers, data=data)
    response_json = res.json()

    if not res.ok:
        print(f"An error occurred when requesting the database content.")
        print(f"Response: {response_json}")
        sys.exit()

    pages = response_json['results']

    if pages == []:
        print("The query didn't match any results")

    return pages


def get_images_to_scan_in_page(page_id, headers):
    read_url = f"https://api.notion.com/v1/blocks/{page_id}/children?page_size=100"
    res = requests.request("GET", read_url, headers=headers)
    data = res.json()

    if not res.ok:
        print(f"An error occurred when getting the images on the page content.")
        print(f"Response: {data}")
        sys.exit()

    results = data['results']

    image_blocks = []

    for index, block in enumerate(results):
        if block['type'] == 'image':
            image_blocks.append(
                {
                    "image_url": block['image']['file']['url'],
                    "ocr_block_id": "",
                    "list_index": index,
                    "text": "",
                    "ocr": False
                }
            )
        if block['type'] == 'paragraph' and block['paragraph']['rich_text']:
            if block['paragraph']['rich_text'][0]['plain_text'] == 'ocr_text':
                for image in image_blocks:
                    if image['list_index'] == (index - 1):
                        print(f"Found 'ocr_text' after image. Index: {index}")
                        image['ocr'] = True
                        image['ocr_block_id'] = block['id']

    ocr_image_blocks = filter(
        lambda image_block: image_block['ocr'], image_blocks)

    return ocr_image_blocks


def get_text_from_image(image_url):

    computervision_client = ComputerVisionClient(
        MICROSOFT_ENDPOINT, CognitiveServicesCredentials(MICROSOFT_API_KEY))
    read_response = computervision_client.read(image_url,  raw=True)
    read_operation_location = read_response.headers["Operation-Location"]
    operation_id = read_operation_location.split("/")[-1]

    while True:
        read_result = computervision_client.get_read_result(operation_id)
        if read_result.status not in ['notStarted', 'running']:
            print('Awaiting response from Azure Vision API...')
            break
        time.sleep(1)

    text = []

    if read_result.status == OperationStatusCodes.succeeded:
        for text_result in read_result.analyze_result.read_results:
            for line in text_result.lines:
                print(f'Found text: {line.text}')
                text.append(line.text)

    return text


def add_text_to_block(block_id, text, headers):
    page_url = f"https://api.notion.com/v1/blocks/{block_id}/children"

    children_object = []

    for text_block in text:
        children_object.append(
            {
                "object": "block",
                "type": "paragraph",
                "paragraph": {
                    "rich_text": [
                        {
                            "type": "text",
                            "text": {
                                "content": text_block
                            }
                        }
                    ]
                }
            }
        )

    update_data = {
        "children": children_object
    }

    data = json.dumps(update_data)
    response = requests.request(
        "PATCH", page_url, headers=headers, data=data)

    if not response.ok:
        print(f"An error occurred when updating the page content.")
        print(f"Response: {data}")
        sys.exit()

    print("Succesfully added the text to the page.")

    return response.ok


def delete_block(block_id, headers):
    page_url = f"https://api.notion.com/v1/blocks/{block_id}"
    response = requests.request(
        "DELETE", page_url, headers=headers)

    if not response.ok:
        print(f"An error occurred when updating the page content.")
        print(f"Response: {response.text}")
        sys.exit()

    print(f"Deleted 'ocr_text' tag.")


if __name__ == '__main__':
    print(f"[{time.time()}] Running scan..")
    notion_content = read_database(DATABASE_ID, HEADERS)

    print(notion_content)

    for page in notion_content:
        images = get_images_to_scan_in_page(page['id'], HEADERS)

        print(images)

        for image in images:
            image['text'] = get_text_from_image(image['image_url'])

            print(image)

            success = add_text_to_block(page['id'], image['text'], HEADERS)

            if success:
                delete_block(image['ocr_block_id'], HEADERS)
