# Runtime Smoke

Use runtime smoke when a change may affect app startup, routing, rendering, auth, state, bundling, or generated assets.

## Basic smoke

1. Start the app.
2. Open the configured local URL.
3. Wait for app shell/login page/main route.
4. Verify page is not blank.
5. Check browser console for fatal errors.
6. Check network for failed JS/CSS chunks.
7. Run extra scenario if provided.
8. Record result.

## Browser/MCP

If browser MCP, Playwright MCP, or browser automation is available, use it.

Do not replace build/test checks with browser smoke.
Runtime smoke is an additional gate.

## Fallback

If browser automation is unavailable:

- start server;
- check HTTP response;
- run existing e2e/smoke tests;
- run production build + preview if available;
- document what could not be checked.

## When required

Runtime smoke is recommended or required for:

- frontend runtime changes;
- routing changes;
- auth/session/bootstrap changes;
- UI kit changes;
- bundler/build tooling changes;
- dependency updates that affect runtime;
- large refactors in app initialization code.
