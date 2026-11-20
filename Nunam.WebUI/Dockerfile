# Build context is the repo root: docker build -f Nunam.WebUI/Dockerfile .
FROM node:24-alpine@sha256:116b800c3754b440ee0aa3ed2f20f5bf3d99a554bfd9adeea09ea8017f6e85f5 AS build
WORKDIR /app
COPY Nunam.WebUI/package.json Nunam.WebUI/package-lock.json ./
RUN npm ci
COPY Nunam.WebUI/ ./
RUN npm run build

FROM nginx:1.29-alpine@sha256:5616878291a2eed594aee8db4dade5878cf7edcb475e59193904b198d9b830de
# 127.0.0.1 never proxies into the container, so by default no forwarded header is trusted.
ENV NUNAM_TRUSTED_PROXY=127.0.0.1/32
COPY Nunam.WebUI/nginx.conf.template /etc/nginx/templates/default.conf.template
COPY Nunam.WebUI/security-headers.conf /etc/nginx/snippets/security-headers.conf
COPY --from=build /app/dist /usr/share/nginx/html
