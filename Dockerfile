FROM python:3.10

RUN apt-get update && apt-get -y install cron vim

COPY . /app
WORKDIR /app

RUN pip install -r requirements.txt

RUN chmod +x entrypoint.sh

# RUN adduser --system --no-create-home user 
# USER user

ENTRYPOINT ["sh","entrypoint.sh"]