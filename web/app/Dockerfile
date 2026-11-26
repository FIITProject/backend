# Сборка из корня репозитория: docker build -f web/app/Dockerfile .
FROM node:22-alpine AS build
WORKDIR /app
COPY web/app/package.json web/app/package-lock.json ./
RUN npm ci
COPY web/app/ ./
RUN npm run build

FROM nginx:1.27-alpine
COPY web/app/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 80
