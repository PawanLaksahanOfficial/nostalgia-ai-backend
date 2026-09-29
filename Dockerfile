FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY nostalgia-ai-backend/nostalgia-ai-backend.csproj nostalgia-ai-backend/
COPY Application/Application.csproj Application/
COPY Domain/Domain.csproj Domain/
COPY Infrastructure/Infrastructure.csproj Infrastructure/
RUN dotnet restore nostalgia-ai-backend/nostalgia-ai-backend.csproj

COPY . .
RUN dotnet publish nostalgia-ai-backend/nostalgia-ai-backend.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Video pipeline tools (all free):
#  - ffmpeg/ffprobe: composes the video. Without them the worker leaves every job "Pending".
#  - fonts-dejavu-core: font for the watermark and captions.
#  - edge-tts: Microsoft Edge neural voices for the narration, in its own Python venv.
RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg fonts-dejavu-core python3 python3-venv \
    && python3 -m venv /opt/edge-tts \
    && /opt/edge-tts/bin/pip install --no-cache-dir edge-tts \
    && ln -s /opt/edge-tts/bin/edge-tts /usr/local/bin/edge-tts \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app .

# The worker looks for the watermark font at assets/fonts/<Video:FontFileName>.
RUN mkdir -p assets/fonts \
    && cp /usr/share/fonts/truetype/dejavu/DejaVuSans.ttf assets/fonts/DejaVuSans.ttf

ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE=false
EXPOSE 8080

CMD ASPNETCORE_URLS=http://+:${PORT:-8080} dotnet nostalgia-ai-backend.dll
