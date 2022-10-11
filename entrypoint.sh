#!/bin/sh

if [ -z "$SCAN_FREQUENCY" ] 
then
    echo "Running single instance of scan"
    python ocr.py
else
    echo "Found scan frequency variable, adding crontab"
    (contrab -l ; echo "*/$SCAN_FREQUENCY * * * * python ocr.py") | crontab -

    echo "Scanning every $SCAN_FREQUENCY minute(s)"
    tail -f
fi