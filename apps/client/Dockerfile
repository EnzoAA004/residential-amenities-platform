FROM node:24-alpine AS build
WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund

COPY . .
RUN npm run build

FROM nginx:1.29-alpine AS runtime

COPY nginx.conf.template /etc/nginx/templates/default.conf.template
COPY --from=build /app/dist/client/browser /usr/share/nginx/html

ENV API_UPSTREAM=http://127.0.0.1:8080

EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]
