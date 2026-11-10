# syntax=docker/dockerfile:1
# ---------------------------------------------------------------------------
# Builds the shell + both remotes and serves them from ONE nginx:
#   /            -> shell
#   /catalog/    -> catalog remote   (remoteEntry.js at /catalog/remoteEntry.js)
#   /inventory/  -> inventory remote
# In a larger org each remote would have its own pipeline + container/CDN path,
# so teams deploy independently. One image keeps this demo simple.
# ---------------------------------------------------------------------------
FROM node:22-alpine AS build
WORKDIR /web
ARG API_BASE_URL=http://localhost:5050
ENV API_BASE_URL=$API_BASE_URL \
    CATALOG_REMOTE_URL=/catalog \
    INVENTORY_REMOTE_URL=/inventory
COPY package.json package-lock.json ./
COPY shared/package.json shared/
COPY mfe-catalog/package.json mfe-catalog/
COPY mfe-inventory/package.json mfe-inventory/
COPY shell/package.json shell/
RUN npm ci
COPY . .
RUN npm run build

FROM nginx:1.27-alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /web/shell/dist /usr/share/nginx/html
COPY --from=build /web/mfe-catalog/dist /usr/share/nginx/html/catalog
COPY --from=build /web/mfe-inventory/dist /usr/share/nginx/html/inventory
EXPOSE 80
