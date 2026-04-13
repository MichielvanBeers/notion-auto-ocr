FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/NotionAutoOcr/NotionAutoOcr.csproj src/NotionAutoOcr/
RUN dotnet restore src/NotionAutoOcr/NotionAutoOcr.csproj

COPY . .
RUN dotnet publish src/NotionAutoOcr/NotionAutoOcr.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "NotionAutoOcr.dll"]