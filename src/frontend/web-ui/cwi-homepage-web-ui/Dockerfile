FROM node:20-alpine AS deps
WORKDIR /app
COPY src/frontend/web-ui/cwi-homepage-web-ui/package.json src/frontend/web-ui/cwi-homepage-web-ui/package-lock.json ./
RUN npm ci

FROM node:20-alpine AS builder
WORKDIR /app
ENV NEXT_TELEMETRY_DISABLED=1
ARG NEXT_PUBLIC_CWI_BETA_ACCESS_URL
ENV NEXT_PUBLIC_CWI_BETA_ACCESS_URL=${NEXT_PUBLIC_CWI_BETA_ACCESS_URL}
COPY src/frontend/web-ui/cwi-homepage-web-ui/package.json src/frontend/web-ui/cwi-homepage-web-ui/package-lock.json ./
COPY src/frontend/web-ui/cwi-homepage-web-ui/next.config.ts \
  src/frontend/web-ui/cwi-homepage-web-ui/tsconfig.json \
  src/frontend/web-ui/cwi-homepage-web-ui/postcss.config.mjs \
  src/frontend/web-ui/cwi-homepage-web-ui/eslint.config.mjs \
  src/frontend/web-ui/cwi-homepage-web-ui/next-env.d.ts \
  ./
COPY src/frontend/web-ui/cwi-homepage-web-ui/src ./src
COPY --from=deps /app/node_modules ./node_modules
RUN npm run build

FROM node:20-alpine AS runner
WORKDIR /app
ARG NEXT_PUBLIC_CWI_BETA_ACCESS_URL
ENV NODE_ENV=production
ENV NEXT_TELEMETRY_DISABLED=1
ENV NEXT_PUBLIC_CWI_BETA_ACCESS_URL=${NEXT_PUBLIC_CWI_BETA_ACCESS_URL}
RUN addgroup -S app && adduser -S app -G app
COPY --from=builder /app/.next/standalone ./
COPY --from=builder /app/.next/static ./.next/static
USER app
EXPOSE 3000
CMD ["node", "server.js"]
