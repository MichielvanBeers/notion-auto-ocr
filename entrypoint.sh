#!/bin/sh

echo "Writing environment variables to /etc/environment"
printenv | grep -v "no_proxy" >> /etc/environment

if [ -z "$SCAN_FREQUENCY" ] 
then
    echo "Running single instance of scan"
    python ocr.py
else
    echo "Found scan frequency variable, adding crontab"
    (crontab -l 2>/dev/null; echo "*/$SCAN_FREQUENCY * * * * python /app/ocr.py &>/output.log") | crontab -

    echo "Scanning every $SCAN_FREQUENCY minute(s)"
    cron -f
fi