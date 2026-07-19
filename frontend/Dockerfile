# syntax=docker/dockerfile:1

# ---- Stage 1: build the Angular app ----
FROM node:22-alpine AS build
WORKDIR /app

# Install dependencies first (better layer caching).
# Uses `npm install` (not `npm ci`) since no lockfile is committed; a lockfile is
# resolved at build time. Commit the generated package-lock.json for reproducible builds.
COPY package.json package-lock.json* ./
RUN npm install

# Build
COPY . .
RUN npm run build

# ---- Stage 2: serve with nginx ----
FROM nginx:alpine
# Remove the default config and add ours
RUN rm -f /etc/nginx/conf.d/default.conf
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/frontend/browser /usr/share/nginx/html

EXPOSE 80
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD wget -q --spider http://localhost/ || exit 1
CMD ["nginx", "-g", "daemon off;"]
