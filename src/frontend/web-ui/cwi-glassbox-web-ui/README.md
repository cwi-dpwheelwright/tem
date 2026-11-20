This is a Next.js operator console for the CWI Glassbox web UI.

## Getting started

First, run the development server:

```bash
npm run dev
# or
yarn dev
# or
pnpm dev
# or
bun dev
```

Open [http://localhost:3000](http://localhost:3000) with your browser to see the result.

You can start editing the page by modifying `src/app/page.tsx`.

## Runtime configuration
Required env vars:
- `NEXT_PUBLIC_API_BASE_URL`
- `NEXT_PUBLIC_OIDC_AUTHORITY`
- `NEXT_PUBLIC_OIDC_CLIENT_ID`
- `NEXT_PUBLIC_OIDC_REDIRECT_URI`
- `NEXT_PUBLIC_OIDC_POST_LOGOUT_REDIRECT_URI`

Optional:
- `NEXT_PUBLIC_PACKS_BASE_URL` (enables pack catalog + activation UI)

## Security hardening
- CSP headers are configured in `next.config.ts`.
- UI renders untrusted payloads as JSON (never `dangerouslySetInnerHTML`).
- API requests use Bearer tokens; no cookies are used, so CSRF is not required for the UI surface.

## Release & rollback
1. Build the app: `npm run build`.
2. Publish the `standalone` output (see `next.config.ts`) and static assets in `public/`.
3. Version static assets by deployment (cache-busting) and set immutable cache headers for hashed assets.
4. For rollback, redeploy the previous build artifacts and invalidate CDN cache for `/` and critical JSON routes.

## Tests
- Unit/lint: `npm run lint`, `npm run typecheck`, `npm run test:unit`
- E2E smoke: `npm run test:e2e` (requires `PLAYWRIGHT_BASE_URL`)
- Contract drift: `npm run codegen:check` (verifies generated API types are up to date)

## Learn More

To learn more about Next.js, take a look at the following resources:

- [Next.js Documentation](https://nextjs.org/docs) - learn about Next.js features and API.
- [Learn Next.js](https://nextjs.org/learn) - an interactive Next.js tutorial.

You can check out [the Next.js GitHub repository](https://github.com/vercel/next.js) - your feedback and contributions are welcome!

## Deploy on Vercel

The easiest way to deploy your Next.js app is to use the [Vercel Platform](https://vercel.com/new?utm_medium=default-template&filter=next.js&utm_source=create-next-app&utm_campaign=create-next-app-readme) from the creators of Next.js.

Check out our [Next.js deployment documentation](https://nextjs.org/docs/app/building-your-application/deploying) for more details.
