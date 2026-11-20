# Build context is the repo root: docker build -f Nunam.WebUI/Dockerfile .
FROM node:24-alpine AS build
WORKDIR /app
COPY Nunam.WebUI/package.json Nunam.WebUI/package-lock.json ./
RUN npm ci
COPY Nunam.WebUI/ ./
RUN npm run build

FROM nginx:1.29-alpine
# 127.0.0.1 never proxies into the container, so by default no forwarded header is trusted.
ENV NUNAM_TRUSTED_PROXY=127.0.0.1/32
COPY Nunam.WebUI/nginx.conf.template /etc/nginx/templates/default.conf.template
COPY Nunam.WebUI/security-headers.conf /etc/nginx/snippets/security-headers.conf
COPY --from=build /app/dist /usr/share/nginx/html
