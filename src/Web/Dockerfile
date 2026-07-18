# syntax=docker/dockerfile:1
# Builds the Angular SPA and serves it from nginx, which also reverse-proxies /api to the API container so the
# browser sees a single origin (US-20). Build context = src/Web.

# ---- build ----
FROM node:22 AS build
WORKDIR /web
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
RUN npm run build

# ---- nginx runtime ----
FROM nginx:alpine AS web
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /web/dist/ragbook-web/browser /usr/share/nginx/html
EXPOSE 80
