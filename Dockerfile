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
COPY --from=build /app .

ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE=false
EXPOSE 8080

CMD ASPNETCORE_URLS=http://+:${PORT:-8080} dotnet nostalgia-ai-backend.dll
