FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Fiit.Contracts/Fiit.Contracts.csproj src/Fiit.Contracts/
COPY web/Fiit.Web/Fiit.Web.csproj web/Fiit.Web/
RUN dotnet restore web/Fiit.Web/Fiit.Web.csproj
COPY src/Fiit.Contracts/ src/Fiit.Contracts/
COPY web/Fiit.Web/ web/Fiit.Web/
RUN dotnet publish web/Fiit.Web -c Release -o /out --no-restore

FROM nginx:1.27-alpine
COPY web/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /out/wwwroot /usr/share/nginx/html
EXPOSE 80
