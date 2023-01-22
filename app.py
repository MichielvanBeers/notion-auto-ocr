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
SCAN_METHOD = os.environ['SCAN_METHOD'].lower() # checkbox or createtime

# This is a dictionary that contains the headers that will be sent with every request to the Notion
# API.
HEADERS = {
    "Authorization": "Bearer " + NOTION_TOKEN,
    "Content-Type": "application/json",
    "Notion-Version": "2022-06-28"
}

def get_scan_request_body():
    """
    > The function returns a request body for the `/pages` endpoint based on the `SCAN_METHOD` parameter
    """
    match SCAN_METHOD:
        case "checkbox":
            request_body = {
                "page_size": 20,
                "filter": {
                    "property": "OCR Parsing",
                    "checkbox": {
                        "equals": True
                    }
                }
            }
        case "createtime":
            request_body = {
                "page_size": 20,
                "sorts": [
                    {
                        "property": "Created time",
                        "direction": "descending"
                    }
                ]
            }

            if SCAN_FREQUENCY is not None:
                current_date_time = datetime.datetime.utcnow()
                timestamp_last_pages_request = current_date_time - \
                    datetime.timedelta(minutes=(int(SCAN_FREQUENCY) + 1))
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
                            "property": "Created time",
                            "direction": "descending"
                        }
                    ]
                }
        case _:
            print(f"Scan method parameter is invalid. Valid values are checkbox or createtime. You have defined", SCAN_METHOD)
            sys.exit()
    
    return request_body


def read_database(database_id, headers):
    """
    It takes a database ID and a set of headers, and returns a list of pages
    
    :param database_id: The ID of the database you want to read from
    :param headers: The headers that we created earlier
    :return: A list of dictionaries. Each dictionary represents a row in the database.
    """
    read_url = f"https://api.notion.com/v1/databases/{database_id}/query"

    request_body = get_scan_request_body()

    data = json.dumps(request_body)

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
    """
    It gets all the images in a page, and checks if the image has a caption with the text `ocr_text` in
    it (paragraph or caption). If it does, it will be added to the list of images to be scanned
    
    :param page_id: The ID of the page you want to scan for images
    :param headers: The headers that you need to pass to the API
    :return: A list of dictionaries. Each dictionary contains the following keys:
        - image_url: The URL of the image to be scanned
        - ocr_block_id: The ID of the block that contains the text to be replaced
        - list_index: The index of the image in the list of blocks
        - caption_index: The index of the caption in
        - caption_full_content: The complete array of caption in Notion
        - caption : Content of caption entry matching the `ocr_text`
        - text : Where will be store the text find by ComputerVision
        - ocr : Boolean if OCR analysis is requested
    """
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
            if block['image']['caption']:
                for index_caption, caption in enumerate(block['image']['caption']):
                    if caption['plain_text'] and 'ocr_text' in caption['plain_text'].split('\n'):
                        caption_index = index_caption
                        caption_text = block['image']['caption'][index_caption]['plain_text']
                        ocr_enable = True


            image_blocks.append(
                {
                    "image_url": block['image']['file']['url'],
                    "ocr_block_id": block['id'],
                    "list_index": index,
                    "caption_index": caption_index if 'caption_index' in locals() else 0,
                    "caption_full_content": block['image']['caption'] if block['image']['caption'] else None,
                    "caption": caption_text if 'caption_text' in locals() else None,
                    "text": "",
                    "ocr": ocr_enable if 'ocr_enable' in locals() else False
                }
            )
        if block['type'] == 'paragraph' and block['paragraph']['rich_text']:
            if block['paragraph']['rich_text'][0]['plain_text'] == 'ocr_text':
                for image in image_blocks:
                    if image['list_index'] == (index - 1):
                        print(f"Found 'ocr_text' after image. Index: {index}")
                        if image['caption'] is not None:
                            print(f"Already found 'ocr_text' in image caption. Bypassing this paragraph")
                        else:
                            image['ocr'] = True
                            image['ocr_block_id'] = block['id']

    ocr_image_blocks = filter(
        lambda image_block: image_block['ocr'], image_blocks)

    return ocr_image_blocks


def get_text_from_image(image_url):
    """
    It takes an image URL, sends it to the Azure Computer Vision API, and returns the text found in the
    image
    
    :param image_url: The URL of the image you want to analyze
    :return: A list of strings
    """
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

def push_update_data(page_url, update_data, headers, message="Successfully pushed to API"):
    """
    It takes a page URL, a dictionary of data to update, and a dictionary of headers, and then it pushes
    the data to the API
    
    :param page_url: The URL of the page you want to update
    :param update_data: The data to be pushed to the API
    :param headers: The headers for the request
    :param message: The message to print to the console when the update is successful, defaults to
    Successfully pushed to API (optional)
    :return: A boolean value.
    """
    data = json.dumps(update_data)
    response = requests.request(
        "PATCH", page_url, headers=headers, data=data)

    if not response.ok:
        print(f"An error occurred when updating the page content.")
        print(f"Response: {data}")
        sys.exit()

    print(message)

    return response.ok


def replace_caption_in_image(image, headers):
    """
    > It takes the image data and the headers, and then it replaces the caption of the image with the
    text that was extracted from the image
    
    :param image: the image object from the list of images
    :param headers: The headers you got from the previous step
    :return: A boolean value coming from push_update_data function as API PATCH result
    """
    page_url = f"https://api.notion.com/v1/blocks/{image['ocr_block_id']}"

    full_text = "\n".join(image['text'])
    text_length = len(full_text)

    ocr_text_position = image['caption'].replace("\n","").rfind("ocr_text")
    if ocr_text_position == 0:
        base_index = image["caption_index"]
    else:
        base_index = image["caption_index"]+1

    if text_length > 2000:
        start_search = 0
        search_limit = 1999
        split_array = []
        split_array.append("\n*********************\n")
        while start_search < text_length:
            end_search = start_search + search_limit if start_search + search_limit <= text_length else text_length
            number_of_remaining_characters = text_length - start_search
            if number_of_remaining_characters > 2000:
                space_position = full_text.rfind(" ", start_search, end_search)
                split_array.append(full_text[start_search:space_position])
                start_search = space_position + 1
            else:
                split_array.append(full_text[start_search:text_length])
                start_search = text_length
        split_array.append("\n*********************")
        image["caption_full_content"][image["caption_index"]]["text"]["content"] = image["caption_full_content"][image["caption_index"]]["text"]["content"].replace("ocr_text","")
        image["caption_full_content"][image["caption_index"]]["plain_text"] = image["caption_full_content"][image["caption_index"]]["plain_text"].replace("ocr_text","")
        for text in split_array:
            caption_text = {
                "type": "text", 
                "text": { "content": text},
                "plain_text": text
            }
            image['caption_full_content'].insert(base_index, caption_text)
            base_index += 1
        
        
    else:
        image["caption_full_content"][image["caption_index"]]["text"]["content"] = image["caption_full_content"][image["caption_index"]]["text"]["content"].replace("ocr_text","")
        image["caption_full_content"][image["caption_index"]]["plain_text"] = image["caption_full_content"][image["caption_index"]]["plain_text"].replace("ocr_text","")
        
        split_array = ["\n*********************\n", "\n".join(image['text']), "\n*********************\n"]

        for text in split_array:
            caption_text = {
                "type": "text", 
                "text": { "content": text},
                "plain_text": text
            }
            image['caption_full_content'].insert(base_index, caption_text)
            base_index += 1

    update_data = {
        "image": {
            "caption": image['caption_full_content']
        }
    }

    return push_update_data(page_url, update_data, headers, "Succesfully added the text to the image caption.")


def add_text_to_block(page_id, text, headers):
    """
    > It takes the block_id, text and the headers, and then it add the text in a new children on the page bottom
    
    :param page_id: the Notion page Id
    :param text: the text receive by Azure Computer Vision API
    :param headers: The headers you got from the previous step
    :return: A boolean value coming from push_update_data function as API PATCH result
    """
    page_url = f"https://api.notion.com/v1/blocks/{page_id}/children"

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

    return push_update_data(page_url, update_data, headers, "Succesfully added the text to the page.")

def unset_ocr_parsing(page_id, headers):
    """
    It takes a page ID and a header, and it unchecks the OCR Parsing property in the page
    
    :param page_id: The ID of the page you want to update
    :param headers: The headers that you got from the previous step
    """
    page_url = f"https://api.notion.com/v1/pages/{page_id}"

    update_data = {
        "properties": {
            "OCR Parsing": { "checkbox": False }
        }
    }

    push_update_data(page_url, update_data, headers, "Succesfully unset OCR Parsing property in the page.")


def delete_block(block_id, headers):
    """
    It deletes the paragraph block with the ID `ocr_text_block_id`
    
    :param block_id: The ID of the block you want to delete
    :param headers: The headers that we'll use to authenticate with the Notion API
    """
    page_url = f"https://api.notion.com/v1/blocks/{block_id}"
    response = requests.request(
        "DELETE", page_url, headers=headers)

    if not response.ok:
        print(f"An error occurred when updating the page content.")
        print(f"Response: {response.text}")
        sys.exit()

    print(f"Deleted 'ocr_text' paragraph.")


if __name__ == '__main__':
    print(f"[{time.time()}] Running scan..")
    notion_content = read_database(DATABASE_ID, HEADERS)

    for page in notion_content:
        images = get_images_to_scan_in_page(page['id'], HEADERS)
        ocr_block_failed = 0

        for image in images:
            print("OCR Failed : ", ocr_block_failed)
            image['text'] = get_text_from_image(image['image_url'])
            
            if image['caption'] is None:
                success = add_text_to_block(page['id'], image['text'], HEADERS)
                if success:
                    delete_block(image['ocr_block_id'], HEADERS)
                else:
                    ocr_block_failed += 1
                
            else:
                success = replace_caption_in_image(image, HEADERS)
                if success == False:
                    ocr_block_failed += 1
        
        if ocr_block_failed == 0:
            unset_ocr_parsing(page['id'], HEADERS)
