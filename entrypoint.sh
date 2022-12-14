#!/bin/sh

echo "Writing environment variables to /etc/environment"
printenv | grep -v "no_proxy" >> /etc/environment

if [ -z "$SCAN_FREQUENCY" ] 
then
    echo "Running single instance of scan"
    python app.py
else
    echo "Found scan frequency variable, adding crontab"
    (crontab -l 2>/dev/null; echo "*/$SCAN_FREQUENCY * * * * cd /app; /usr/local/bin/python3 ./app.py > output.log") | crontab -  

    echo "Scanning every $SCAN_FREQUENCY minute(s)"
    service cron start && tail -f output.log
fi