FROM python:3.10

RUN apt-get update

COPY . /app
WORKDIR /app

RUN pip install -r requirements.txt

RUN chmod +x entrypoint.sh

RUN adduser --system --no-create-home user 
USER user

ENTRYPOINT ["sh","entrypoint.sh"]