FROM python:3.10

RUN apt-get update && apt-get -y install cron

COPY . /app
WORKDIR /app

RUN pip install -r requirements.txt

RUN chmod +x entrypoint.sh

ENTRYPOINT ["sh","entrypoint.sh"]