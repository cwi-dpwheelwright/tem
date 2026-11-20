FROM node:20-alpine AS deps
WORKDIR /app
COPY src/frontend/web-ui/cwi-glassbox-web-ui/package.json src/frontend/web-ui/cwi-glassbox-web-ui/package-lock.json ./
RUN npm ci

FROM node:20-alpine AS builder
WORKDIR /app
ENV NEXT_TELEMETRY_DISABLED=1
ARG NEXT_PUBLIC_API_BASE_URL
ARG NEXT_PUBLIC_OIDC_AUTHORITY
ARG NEXT_PUBLIC_OIDC_CLIENT_ID
ARG NEXT_PUBLIC_OIDC_REDIRECT_URI
ARG NEXT_PUBLIC_OIDC_POST_LOGOUT_REDIRECT_URI
ARG NEXT_PUBLIC_OIDC_SCOPES
ARG NEXT_PUBLIC_PACKS_BASE_URL
ENV NEXT_PUBLIC_API_BASE_URL=${NEXT_PUBLIC_API_BASE_URL}
ENV NEXT_PUBLIC_OIDC_AUTHORITY=${NEXT_PUBLIC_OIDC_AUTHORITY}
ENV NEXT_PUBLIC_OIDC_CLIENT_ID=${NEXT_PUBLIC_OIDC_CLIENT_ID}
ENV NEXT_PUBLIC_OIDC_REDIRECT_URI=${NEXT_PUBLIC_OIDC_REDIRECT_URI}
ENV NEXT_PUBLIC_OIDC_POST_LOGOUT_REDIRECT_URI=${NEXT_PUBLIC_OIDC_POST_LOGOUT_REDIRECT_URI}
ENV NEXT_PUBLIC_OIDC_SCOPES=${NEXT_PUBLIC_OIDC_SCOPES}
ENV NEXT_PUBLIC_PACKS_BASE_URL=${NEXT_PUBLIC_PACKS_BASE_URL}
COPY src/frontend/web-ui/cwi-glassbox-web-ui/package.json src/frontend/web-ui/cwi-glassbox-web-ui/package-lock.json ./
COPY src/frontend/web-ui/cwi-glassbox-web-ui/next.config.ts \
  src/frontend/web-ui/cwi-glassbox-web-ui/tsconfig.json \
  src/frontend/web-ui/cwi-glassbox-web-ui/postcss.config.mjs \
  src/frontend/web-ui/cwi-glassbox-web-ui/eslint.config.mjs \
  src/frontend/web-ui/cwi-glassbox-web-ui/next-env.d.ts \
  ./
COPY src/frontend/web-ui/cwi-glassbox-web-ui/src ./src
COPY src/frontend/web-ui/cwi-glassbox-web-ui/public ./public
COPY src/frontend/web-ui/cwi-glassbox-web-ui/scripts ./scripts
COPY contracts /contracts
COPY --from=deps /app/node_modules ./node_modules
RUN npm run build

FROM node:20-alpine AS runner
WORKDIR /app
ENV NODE_ENV=production
ENV NEXT_TELEMETRY_DISABLED=1
RUN addgroup -S app && adduser -S app -G app
COPY --from=builder /app/public ./public
COPY --from=builder /app/.next/standalone ./
COPY --from=builder /app/.next/static ./.next/static
USER app
EXPOSE 3000
CMD ["node", "server.js"]
