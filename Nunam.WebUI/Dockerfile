# Build context is the repo root: docker build -f Nunam.WebUI/Dockerfile .
FROM node:24-alpine AS build
WORKDIR /app
COPY Nunam.WebUI/package.json Nunam.WebUI/package-lock.json ./
RUN npm ci
COPY Nunam.WebUI/ ./
RUN npm run build

FROM nginx:1.29-alpine
COPY Nunam.WebUI/nginx.conf /etc/nginx/conf.d/default.conf
COPY Nunam.WebUI/security-headers.conf /etc/nginx/snippets/security-headers.conf
COPY --from=build /app/dist /usr/share/nginx/html
