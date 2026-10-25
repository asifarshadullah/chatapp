## 1. Refuse an unsafe signing secret

- [x] 1.1 Write failing startup tests in `Chat.Identity.Tests`, next to `RefreshTokenSettingsStartupTests` and in its style (hosting "The app refuses a signing secret anyone could know"):
  - Production with `Jwt:Secret` set to the old placeholder fails to start, and the error names `Jwt:Secret`.
  - Production with a 31-byte secret fails to start.
  - Production with no secret fails to start.
  - Production with a 64-byte secret starts.
  - Development with the development settings starts.

  Verify the three refusals fail today, since the app starts with each of them
- [x] 1.2 Bind `JwtSettings` with `Validate(...)` and `ValidateOnStart()` as in design.md, exempting Development. Set `Jwt:Secret` in `appsettings.json` to empty. Verify 1.1 passes and the whole backend suite still passes

## 2. Serve the UI from the API's origin

- [x] 2.1 Write failing tests in `Chat.Api.Tests` with a factory whose web root is a temporary folder holding `index.html` and `assets/app-abc123.js` (hosting "One origin serves the app and the API"):
  - `/` and `/billing/success` return the entry page, with `Cache-Control: no-cache`.
  - `/assets/app-abc123.js` returns the file, cached as immutable.
  - `/api/nope` and `/auth/nope` return 404, and the body is not the entry page.
  - An existing API route is not shadowed: `POST /api/chat/messages` with an empty message still returns 400.

  Verify they fail today, except the existing-route check
- [x] 2.2 Add static files, the cache headers, the `index.html` fallback and the 404 fallbacks for `/api` and `/auth` to `Program.cs` (design.md). Verify 2.1 passes and `dotnet run` with `npm run dev` still serves the app on `:5173`

## 3. Health endpoint

- [x] 3.1 Write failing tests in `Chat.Api.Tests` (hosting "The app reports its health"):
  - An unauthenticated `GET /healthz` returns 200 with `status` `healthy`.
  - With `APP_COMMIT` set to a value, `commit` is that value.
  - Without it, `commit` is null.

  Verify they fail today
- [x] 3.2 Map `/healthz` as in design.md. Verify 3.1 passes

## 4. Proxy headers and CORS

- [x] 4.1 Write a test in `Chat.Identity.Tests` using `AuthApiFactory` (hosting "Behind a TLS proxy the app uses the public scheme and host"). With `FORWARDEDHEADERS_ENABLED=true` and a Google client id and secret configured, `GET /auth/google` sent with `X-Forwarded-Proto: https` and `Host: live.example` redirects to Google with `redirect_uri` `https://live.example/signin-google`.

  This passes today, because the switch is built into ASP.NET; it guards the setting's name, which the Dockerfile relies on. Verify it fails when the setting is left out
- [x] 4.2 Write a failing test in `Chat.Api.Tests` (hosting "No other origin is granted cross-origin access"). A preflight `OPTIONS /auth/refresh` and a `GET /api/chat/<id>/history`, each with `Origin: http://localhost:5173`, get no `Access-Control-Allow-Origin` header. Verify it fails today
- [x] 4.3 Remove the CORS registration and middleware from `Program.cs`. Verify 4.2 passes, and that the Playwright suite still passes through the Vite proxy

- [x] 4.4 Found while testing the image: without a Google client id every request failed with 500. Write failing tests in `Chat.Identity.Tests` (hosting "An unconfigured sign-in provider does not break the app"): with `Google:ClientId` empty, `GET /auth/me` answers 401 and `GET /auth/google` answers 404. Register Google only when configured and return 404 from `GoogleLogin` without the scheme (design.md). Verify the tests pass and the existing Google tests still pass

## 5. The image

- [x] 5.1 Add `.dockerignore` and the three-stage `Dockerfile` described in design.md:
  - pinned base images;
  - `APP_COMMIT` as a build argument;
  - `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`;
  - the non-root `app` user and port 8080.

  Verify by hand:
  - `docker build --build-arg APP_COMMIT=$(git rev-parse HEAD) .` succeeds.
  - Started with `ASPNETCORE_ENVIRONMENT=Production`, the local MongoDB connection string and a 64-byte `Jwt__Secret`:
    - `/healthz` names that commit;
    - `/` returns the app;
    - `/billing/success` returns the app;
    - with `Google__ClientId` set to any value, `GET /auth/google` sent with `X-Forwarded-Proto: https` redirects with an `https` `redirect_uri`;
    - `docker exec ... id` shows a non-root user;
    - the image contains no `appsettings.Development.json`.
  - Started without `Jwt__Secret`, the container exits with an error naming `Jwt:Secret`.
- [x] 5.2 Add the `app` service to `docker-compose.yml` under the `app` profile, as in design.md. Verify:
  - `docker compose up -d` still starts only `mongodb` and `mongo-express`;
  - with Ollama running, `docker compose --profile app up --build` serves the app on `http://localhost:8080`, where a new user can sign up, send a message and see a streamed reply.

## 6. Workflows

- [x] 6.1 Update `ci.yml`:
  - add the `workflow_call` trigger;
  - change the concurrency group to `${{ github.workflow }}-${{ github.ref }}`;
  - add a `docker` job that builds the image without pushing, with the Actions cache, and runs only when `github.workflow == 'CI'`, so it is skipped inside a deploy.

  Verify with `actionlint`, and that the `docker` job runs and passes on the pull request
- [x] 6.2 Add `deploy.yml` with the `checks`, `publish` and `release` jobs from design.md:
  - `workflow_dispatch` only;
  - the `deploy` concurrency group;
  - `packages: write` only on `publish`;
  - the `production` environment on `release`;
  - URL-encoded `imgURL`;
  - the `/healthz` wait, with a 10-minute timeout.

  Verify with `actionlint`. The workflow itself can only be run once it is on `main` (design.md, Migration Plan)

## 7. Docs

- [x] 7.1 Write `docs/deployment.md` from design.md's Migration Plan:
  - the Atlas setup, with the least-privilege user;
  - making the GHCR package public;
  - the Render service and its environment variables, including `PORT=8080`;
  - the GitHub environment, secret and variable;
  - how to deploy a branch, and how to roll back.

  Verify every setting named in it matches `deploy.yml` and `Program.cs`
- [x] 7.2 Update the README:
  - the CI status badge replaces the hard-coded `tests-119` badge;
  - the stale test count in Features is removed;
  - a "Run the production image locally" section gives the two commands, the Chrome or Firefox note, and `OLLAMA_HOST=0.0.0.0` on Linux;
  - two screenshots of the app, taken from the local image, are saved under `docs/images/`;
  - the deployment guide is linked.

  Verify the badge URL resolves to the CI workflow and every link resolves

## 8. Verification

- [x] 8.1 Run the suites as CI does: `npm run typecheck`, `npm run lint` and `npm run test:run` in `frontend/chat-ui`, and `dotnet test backend/ChatApp.sln`. Run `npx playwright test` in `e2e/playwright`, since `Program.cs` changed. Run `openspec validate --all --strict`. Verify all pass
- [x] 8.2 Mutation checks, restoring the code after each:
  - drop the `/api` 404 fallback: the `/api/nope` test fails;
  - drop the placeholder comparison: the placeholder test fails;
  - remove `ASPNETCORE_FORWARDEDHEADERS_ENABLED` from the Dockerfile: the 5.1 `/auth/google` check gets an `http` `redirect_uri`.
