FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Fiit.Contracts/Fiit.Contracts.csproj src/Fiit.Contracts/
COPY src/Fiit.Api/Fiit.Api.csproj src/Fiit.Api/
RUN dotnet restore src/Fiit.Api/Fiit.Api.csproj
COPY src/ src/
RUN dotnet publish src/Fiit.Api -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Fiit.Api.dll"]
