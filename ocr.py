import requests
import json
import logging
import os
import sys
from PIL import Image
import pytesseract
import cv2
import time

pytesseract.pytesseract.tesseract_cmd = r'C:\Program Files\Tesseract-OCR\tesseract.exe'

# Setup logging
log_file = os.getcwd() + '/output.log'
logging.basicConfig(filename=log_file, filemode='w', level=logging.INFO,
                    format='%(asctime)s | %(name)s | %(levelname)s | %(message)s')
logging.getLogger().addHandler(logging.StreamHandler())

# Arguments [Token] [Database ID]
NOTION_TOKEN = str(sys.argv[1])
DATABASE_ID = str(sys.argv[2])
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

    current_time_string = str(time.time())

    image_data = requests.get(image_url).content
    with open(current_time_string + '_original.jpg', 'wb') as handler:
        handler.write(image_data)

    # load the example image and convert it to grayscale
    image = cv2.imread(current_time_string + '_original.jpg')
    gray = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)

    # apply thresholding to preprocess the image
    gray = cv2.threshold(gray, 100, 255, cv2.THRESH_BINARY | cv2.THRESH_OTSU)[1]

    # save the processed image in the /static/uploads directory
    ofilename = current_time_string + '_processed.jpg'
    cv2.imwrite(ofilename, gray)

    # perform OCR on the processed image
    text = pytesseract.image_to_string(Image.open(ofilename))

    os.remove(current_time_string + '_original.jpg')
    os.remove(current_time_string + '_processed.jpg')

    return text


def add_text_to_block(block_id, text, headers):
    page_url = f"https://api.notion.com/v1/blocks/{block_id}"

    update_data = {
        "paragraph": {
            "rich_text": [{
                "text": {
                    "content": text
                }
            }]
        }
    }

    data = json.dumps(update_data)

    response = requests.request(
        "PATCH", page_url, headers=headers, data=data)

    logging.info(f"Response PATCH request: {response.text}")

if __name__ == '__main__':
    notion_content = read_database(DATABASE_ID, HEADERS)

    for page in notion_content:
        images = get_images_to_scan_in_page(page['id'], HEADERS)

        for image in images:
            image['text'] = get_text_from_image(image['image_url'])

            add_text_to_block(image['ocr_block_id'], image['text'], HEADERS)
