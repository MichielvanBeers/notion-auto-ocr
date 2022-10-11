#!/bin/sh

if [ -z "$SCAN_FREQUENCY" ] 
then
    echo "Running single instance of scan"
    python ocr.py
else
    echo "Found scan frequency variable, adding crontab"
    (crontab -l 2>/dev/null; echo "*/$SCAN_FREQUENCY * * * * python3 ocr.py") | crontab -
    
    echo "Watching output.."
    tail -f
fi
