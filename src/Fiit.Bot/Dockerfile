# Сборка из корня репозитория: docker build -f src/Fiit.Bot/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Fiit.Contracts/Fiit.Contracts.csproj src/Fiit.Contracts/
COPY src/Fiit.Messaging/Fiit.Messaging.csproj src/Fiit.Messaging/
COPY src/Fiit.Bot.Core/Fiit.Bot.Core.csproj src/Fiit.Bot.Core/
COPY src/Fiit.Bot/Fiit.Bot.csproj src/Fiit.Bot/
RUN dotnet restore src/Fiit.Bot/Fiit.Bot.csproj
COPY src/ src/
RUN dotnet publish src/Fiit.Bot -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Fiit.Bot.dll"]
