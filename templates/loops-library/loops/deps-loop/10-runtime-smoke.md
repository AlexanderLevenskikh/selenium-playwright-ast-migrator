# Runtime Smoke Verification

Use this file for dependency batches that may affect runtime behavior.

The goal is to catch problems that build/typecheck/tests may miss:

- app does not start;
- blank page;
- broken root route;
- fatal browser console errors;
- failed JS/CSS chunks;
- broken auth/bootstrap/config initialization;
- dev server or preview server no longer works.

## When runtime smoke is required

Runtime smoke is required after medium/high-risk batches that may affect runtime behavior, including updates to:

- `react`, `react-dom`;
- `react-router`, routing/navigation libraries;
- `redux`, `@reduxjs/toolkit`, `react-redux`, state-management libraries;
- UI kit/runtime component libraries;
- auth/OIDC/session libraries;
- Sentry/telemetry/bootstrap libraries;
- bundler/dev-server/build tooling such as Webpack, Vite, Babel, SWC, esbuild;
- environment/config/polyfill packages;
- packages that change generated assets, dev server, or app startup;
- any batch that changed runtime code or app initialization.

Runtime smoke is optional for low-risk dev-only batches if build/typecheck/tests/audit already passed.

## Basic runtime smoke

Use the configured project commands and fields from `kickoff-prompt.txt`.

Default scenario:

1. Start the app using the configured `Start command` or `Preview command`.
2. Open `Local URL`.
3. Wait for `App ready selector` if provided.
4. Verify the page is not blank.
5. Verify the app shell/login page/main route renders.
6. Check browser console for fatal errors.
7. Check network requests for failed JS/CSS chunks.
8. Run the extra smoke scenario if provided.
9. Record the result in the final report.

## Browser MCP / Playwright MCP rule

If browser MCP, Playwright MCP, or another browser automation tool is available, use it for runtime smoke.

The agent should:

1. start the local app;
2. open the browser;
3. navigate to the configured URL;
4. inspect console errors;
5. inspect failed network requests;
6. perform the smoke scenario;
7. capture the result in the report.

Do not use browser smoke as a replacement for build/typecheck/tests/audit.
Use it as an additional gate.

Correct order:

```text
install
→ build
→ typecheck
→ lint
→ tests
→ audit / dependency tree checks
→ runtime smoke
```

## Fallback when browser automation is unavailable

If browser MCP/automation is unavailable, run the strongest available fallback:

- start the app and check that the server becomes ready;
- request the local URL and verify HTTP 200/3xx expected response;
- run existing smoke/e2e tests if available;
- run production build + preview server if the project supports it;
- verify generated assets exist.

If no runtime smoke is possible, state this explicitly in the report.

## Failure policy

If runtime smoke fails:

1. capture the console/network/server error;
2. classify likely cause;
3. map the failure to the current dependency batch;
4. fix if actionable;
5. if not actionable, revert the current batch;
6. retry with a smaller batch if possible;
7. stop with `TICKET_NEEDED` only when the stop policy requires it.

Do not keep a dependency batch that passes build/tests but breaks runtime smoke, unless the failure is clearly unrelated to the batch and documented.

## Risk-based smoke requirement

### Low-risk dev-only batch

Examples:

- type packages;
- lint-only packages;
- test-only packages;
- docs tooling.

Runtime smoke usually not required unless the batch unexpectedly changes build output or runtime code.

### Medium-risk tooling batch

Examples:

- bundler plugins;
- Babel/SWC/esbuild tooling;
- test/build infrastructure;
- Storybook builder.

Runtime smoke is required if app build output, dev server, preview server, env handling, or asset loading changed.

### High-risk runtime/framework batch

Examples:

- React;
- routing;
- state management;
- UI kit;
- auth/session;
- app bootstrap;
- polyfills.

Runtime smoke is required.

## Report fields

Include this in the final report:

```md
## Runtime smoke

- Required: yes/no
- Tool: Browser MCP / Playwright / HTTP fallback / not available
- Start command:
- Local URL:
- Scenario:
- Result:
- Console errors:
- Failed network requests:
- Notes:
```
