import requests
import json
import logging
import os
import sys
from PIL import Image
import pytesseract
import cv2
import time
from azure.cognitiveservices.vision.computervision import ComputerVisionClient
from azure.cognitiveservices.vision.computervision.models import OperationStatusCodes
from azure.cognitiveservices.vision.computervision.models import VisualFeatureTypes
from msrest.authentication import CognitiveServicesCredentials

pytesseract.pytesseract.tesseract_cmd = r'C:\Program Files\Tesseract-OCR\tesseract.exe'

# Setup logging
log_file = os.getcwd() + '/output.log'
logging.basicConfig(filename=log_file, filemode='w', level=logging.INFO,
                    format='%(asctime)s | %(name)s | %(levelname)s | %(message)s')
logging.getLogger().addHandler(logging.StreamHandler())

# Arguments [Token] [Database ID]
NOTION_TOKEN = str(sys.argv[1])
DATABASE_ID = str(sys.argv[2])
MICROSOFT_API_KEY = '921558dc1a82403fa0dcaf81d2c0d50a'
MICROSOFT_ENDPOINT = 'https://notion-automate-ocr.cognitiveservices.azure.com/'

HEADERS = {
    "Authorization": "Bearer " + NOTION_TOKEN,
    "Content-Type": "application/json",
    "Notion-Version": "2022-06-28"
}


def read_database(database_id, headers):
    read_url = f"https://api.notion.com/v1/databases/{database_id}/query"

    request_body = {
        "page_size": 5,
        "filter": {
            "and": [
                {
                    "property": "Active",
                    "checkbox": {
                        "equals": True
                    }
                },
            ]
        },
        "sorts": [
            {
                "property": "Created",
                "direction": "descending"
            }
        ]
    }

    data = json.dumps(request_body)

    res = requests.request("POST", read_url, headers=headers, data=data)
    response_json = res.json()

    print(response_json)

    pages = response_json['results']

    if pages == []:
        logging.info("The query didn't match any results")

    logging.info(f"Pages results: {pages}")

    return pages


def get_images_to_scan_in_page(page_id, headers):
    read_url = f"https://api.notion.com/v1/blocks/{page_id}/children?page_size=100"
    res = requests.request("GET", read_url, headers=headers)
    data = res.json()
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
                        image['ocr'] = True
                        image['ocr_block_id'] = block['id']

    ocr_image_blocks = filter(
        lambda image_block: image_block['ocr'], image_blocks)

    return ocr_image_blocks


def get_text_from_image(image_url):

    computervision_client = ComputerVisionClient(MICROSOFT_ENDPOINT, CognitiveServicesCredentials(MICROSOFT_API_KEY))

    read_response = computervision_client.read(image_url,  raw=True)
    read_operation_location = read_response.headers["Operation-Location"]
    operation_id = read_operation_location.split("/")[-1]

    while True:
        read_result = computervision_client.get_read_result(operation_id)
        if read_result.status not in ['notStarted', 'running']:
            break
        time.sleep(1)

    text = []

    if read_result.status == OperationStatusCodes.succeeded:
        for text_result in read_result.analyze_result.read_results:
            for line in text_result.lines:
                text.append(line.text)
            
    return text


def add_text_to_block(block_id, text, headers):
    page_url = f"https://api.notion.com/v1/blocks/{block_id}/children"

    children_object = []

    for text_block in text:
        children_object.append(
            {
                "object":"block",
                "type":"paragraph",
                "paragraph": {
                    "rich_text" : [
                        {
                            "type":"text",
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

    print("PATCH DATA:" + data)

    response = requests.request(
        "PATCH", page_url, headers=headers, data=data)

    logging.info(f"Response PATCH request: {response.text}")

    return response.ok

def delete_block(block_id, headers):
    page_url = f"https://api.notion.com/v1/blocks/{block_id}"
    
    response = requests.request(
        "DELETE", page_url, headers=headers)

    logging.info(f"Response DELETE request: {response.text}")

if __name__ == '__main__':
    notion_content = read_database(DATABASE_ID, HEADERS)

    for page in notion_content:
        images = get_images_to_scan_in_page(page['id'], HEADERS)

        for image in images:
            image['text'] = get_text_from_image(image['image_url'])

            success = add_text_to_block(page['id'], image['text'], HEADERS)

            if success:
                delete_block(image['ocr_block_id'], HEADERS)
