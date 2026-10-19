# Names Out Of A Hat — frontend

The React 19 + TypeScript single-page app, built with Vite and served from S3 behind CloudFront. What the application does, and why, is in the [repository README](../../README.md).

## Running it

```bash
npm install
npm run dev
```

There's no local API. The dev server talks to the live one, whose address is in `src/config.ts`, so signing in sends a real magic link and changes made locally change real data. The API answers any origin, so this works from `localhost` without a proxy.

## Checks

```bash
npm test
```

```bash
npm run lint
```

```bash
npm run build
```

Tests are Vitest with jsdom and Testing Library, set up in `src/test/setup.ts` and kept beside the files they test.

## Layout

- `src/pages` — one component per route: the signed-in home page, the organizer's view of an exchange, a participant's view of one, and the sign-in callback.
- `src/components` — dialogs and the pieces the pages share.
- `src/api.ts`, `src/config.ts` — calls to the API and where it lives.

## Deploying

The **Deploy Frontend to S3** workflow builds the app, syncs it to S3 and invalidates the CloudFront cache. It's run by hand. The security headers, Content-Security-Policy included, aren't set here: a CloudFront Function in `iac/terraform/functions/frontend-security-headers.js` adds them, so a new third-party script or API host needs a change there too.
