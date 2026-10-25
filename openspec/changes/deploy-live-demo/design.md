## Context

- `Program.cs` serves only the API. In development the browser talks to Vite on `:5173`, which
  proxies `/api` and `/auth` to the API on `:5064`, so every call the UI makes is same-origin.
  The UI uses relative paths (`/api`, `/auth/...`) throughout.
- A CORS policy allows `http://localhost:5173` with credentials. Nothing uses it, because the
  Vite proxy makes the calls same-origin, but it would also apply in production.
- `appsettings.json` commits `Jwt:Secret` as `REPLACE-WITH-A-SECURE-256-BIT-SECRET-IN-PRODUCTION`:
  50 bytes, long enough to sign tokens. A deploy that forgot to set the secret would start, and
  anyone could forge tokens with a value published in the repo.
- `appsettings.json` has no `MongoDB` or `Ollama` section. Those exist only in
  `appsettings.Development.json`.
- The Google challenge builds its callback with `Url.Action`, and checkout builds its return URLs
  from `Request.Scheme` and `Request.Host`. Behind a TLS-terminating proxy both would read `http`.
- `RefreshTokenSettings` is already validated at startup with `ValidateOnStart`, and
  `Chat.Identity.Tests` has startup tests for it.
- Render's free web service: 512 MB of RAM, 0.1 CPU, sleeps after 15 minutes idle, TLS
  terminated by Render's proxy, no static outbound IP. It can run a prebuilt image, and its
  deploy hook takes an `imgURL` that picks the tag for that deploy.
- The repository is public, so a GHCR package published from it can be public too.

## Goals / Non-Goals

**Goals:**

- One image, built once per deploy, that runs the same on Render and on a laptop.
- A deploy is something the owner starts and can see finish, with the commit it deployed.
- Nothing secret in the repo, the image or the workflow logs.

**Non-Goals:**

- More than one instance, or zero-downtime deploys. Render's free tier runs one instance.
- Keeping the free instance awake. The acceptance criteria allow for a cold start.
- Persisting ASP.NET data protection keys. See Risks.
- Infrastructure as code (a `render.yaml` blueprint). The one-time setup is short and documented
  instead; a blueprint can come later without changing anything here.

## Decisions

**ASP.NET serves the built UI, on the same origin as the API.**
The image copies Vite's `dist` into the API's `wwwroot`. Static files are served first, then the
API, then a fallback to `index.html` for any other path, so a link such as `/billing/success`
loads the app. `/api/{**rest}` and `/auth/{**rest}` get their own fallback that returns 404, so a
mistyped API call fails as an API call instead of returning HTML with 200.
Alternatives: a separate static host for the UI (Render static site, GitHub Pages) would need
CORS with credentials, a cross-site refresh cookie (`SameSite=None`), and two deploys to keep in
step. One origin keeps the cookie `SameSite=Lax` and needs no CORS at all.

`index.html` is served with `Cache-Control: no-cache`, and the hashed files under `/assets` as
immutable for a year. Otherwise a browser could keep an old `index.html` that points at assets the
new deploy no longer has.

In development there is no `wwwroot`, so the fallback finds no file and returns 404. Vite still
serves the UI on `:5173`, exactly as today.

The `/api` and `/auth` fallbacks match any HTTP method, so a known route called with the wrong method,
such as `GET /auth/login`, now answers 404 instead of 405. The page fallback answers only GET and
HEAD, so other methods on a non-API path, such as the old `POST /chatHub/negotiate`, answer 405
instead of 404. No client relies on either.

**Google is registered only when it is configured.**
Found while testing the image. The authentication middleware asks every remote sign-in handler
whether each request is its callback, and the Google handler validates its options when it is
asked. With no client id, as on the live app, every request failed with 500. Development hid this
because `appsettings.Development.json` carries a dummy id.
So `Program.cs` adds Google only when `Google:ClientId` is set. `GET /auth/google` checks for the
scheme and answers 404 without it, instead of throwing on an unknown scheme.
Alternative: a dummy client id in production. Every request would work, but the sign-in route
would send users to a Google error page.

**CORS is removed, not moved into configuration.**
No client calls the API cross-origin: the UI is same-origin in development (through the Vite
proxy) and in production. A configurable list would be configuration for nothing, and the current
policy would let a page on `localhost:5173` on any visitor's machine make credentialed calls to the
live API.

**Forwarded headers through ASP.NET's built-in switch, set in the image.**
The Dockerfile sets `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`. ASP.NET then applies
`X-Forwarded-Proto` and `X-Forwarded-For` from any proxy, which is what a host with no fixed proxy
address needs. Render's proxy keeps the public `Host` header, so `X-Forwarded-Host` is not
trusted, which also keeps a forged host out of the URLs the app builds.
The Google handler builds its `redirect_uri` (`/signin-google`) from the request's scheme and
host, and so does checkout. Both therefore become `https` behind the proxy.
Setting it in the image rather than in Render's settings means it cannot be forgotten. The image is
only ever reached through Render's proxy or on a developer's own machine, so trusting the header
there costs nothing. `dotnet run` does not set it.
Alternative: `UseForwardedHeaders` in `Program.cs` with cleared known networks. Same effect, more
code, and it would also apply to `dotnet run`.
`UseHttpsRedirection` stays. Behind the proxy a request already reads as HTTPS, so it does not
redirect, and Render redirects plain HTTP at its edge.

**Health is a liveness check that names the commit.**
`/healthz` answers 200 with `{ "status": "healthy", "commit": "<sha>" }` and needs no sign-in. The
commit comes from a build argument the Dockerfile passes in as `APP_COMMIT`; without it, as under
`dotnet run`, `commit` is null.
It does not check MongoDB: Render restarts an instance whose health check fails, and a restart does
not fix an Atlas outage. Naming the commit lets the deploy workflow see when the new image is live.

**The signing secret is validated at startup, like the refresh-token settings.**
`JwtSettings` is bound with `AddOptions().Validate(...).ValidateOnStart()`, the same pattern as
`RefreshTokenSettings`. Outside Development, `Secret` must be at least 32 UTF-8 bytes (the HMAC-SHA256
minimum) and must not equal the old placeholder. `appsettings.json` drops the placeholder and leaves
`Secret` empty, so a deploy without the setting fails at startup instead of running.
The old placeholder is still refused because it stays public in the repo's history.
Development is exempt because `appsettings.Development.json` carries a local secret that the
tests and `dotnet run` rely on.

**A three-stage Dockerfile, with version-pinned base images.**
1. `node:20` (matching `.nvmrc`): `npm ci` and `npm run build` for `frontend/chat-ui`.
2. `dotnet/sdk:8.0`: restore from the project files first, for layer caching, then publish
   `Chat.Api` in Release.
3. `dotnet/aspnet:8.0`: the published API plus the UI build in `wwwroot`, running as the image's
   non-root `app` user and listening on 8080, the .NET 8 image default.

The image does not run tests. CI and the deploy workflow do that before it is built.
Debian-based images rather than Alpine, so the MongoDB driver and globalization behave exactly as
they do on the developer machines and in CI. `.dockerignore` keeps `bin`, `obj`, `node_modules`,
`dist`, `e2e` and `.git` out of the build context.
`appsettings.Development.json` is marked not to be published, so the image carries neither the
development signing secret nor the local database password. An
`org.opencontainers.image.source` label links the GHCR package to the repository.

**CI builds the image without pushing; the deploy workflow calls CI.**
`ci.yml` gains a `docker` job that builds the image with `push: false` and the GitHub Actions
cache. It also gains a `workflow_call` trigger so the deploy workflow can run the same jobs instead
of copying them.
Its concurrency group changes from `ci-${{ github.ref }}` to `${{ github.workflow }}-${{ github.ref }}`.
In a called workflow, `github.workflow` is the caller's name, so the checks a deploy runs neither
cancel nor get cancelled by a push to the same branch.
When CI runs inside a deploy, its `docker` job is skipped (`if: github.workflow == 'CI'`), since
the deploy builds the image itself. The condition tests the workflow name, not the event, because
CI can also be started by hand.

**`deploy.yml`: started by hand, on any branch, in three jobs.**
It runs only on `workflow_dispatch`, so GitHub's "Run workflow" button picks the branch, and only
people with write access can start it. Its concurrency group is `deploy` without cancellation, so
two deploys queue instead of racing.
1. `checks`: calls `ci.yml`.
2. `publish` (needs `checks`): logs in to GHCR with the workflow's own token (`packages: write`)
   and pushes `ghcr.io/asifarshadullah/chatapp:<full commit sha>`, passing the commit as
   `APP_COMMIT`.
3. `release` (needs `publish`, in the `production` environment): calls the Render deploy hook from
   the `RENDER_DEPLOY_HOOK_URL` secret, adding `imgURL` URL-encoded. Then it polls
   `<LIVE_URL>/healthz` until `commit` matches, failing after 10 minutes. `LIVE_URL` is a
   repository variable, not a secret.

The `production` environment gives each deploy a record and a link in GitHub, and keeps the hook
secret scoped to that job.
Alternatives:
- Render building from the repo: Render builds only its configured branch, and a build there runs
  outside the tests.
- Render's "deploy after CI passes": automatic on one branch, which is what the owner does not want
  yet.

**Compose gets an `app` profile.**
`docker-compose.yml` gains an `app` service, built from the same Dockerfile and in the `app`
profile, so `docker compose up -d` for the tests still starts only MongoDB and mongo-express.
The service:
- publishes `8080`;
- runs as `Production`;
- reaches MongoDB as `mongodb:27017` and Ollama at `host.docker.internal:11434`, with
  `extra_hosts: host-gateway` for Linux;
- carries a local-only signing secret marked as such.

Local run: `ollama pull gemma3:4b`, then `docker compose --profile app up --build`, then open
`http://localhost:8080`.

## Risks / Trade-offs

- [The free instance sleeps after 15 minutes; the first visit then waits about a minute] →
  Accepted by #21's criteria. The README says so next to the demo link.
- [Atlas must accept connections from anywhere, since Render's free tier has no fixed outbound IP]
  → A dedicated database user with `readWrite` on the app's database only, and a long generated
  password. Recorded in the deployment guide.
- [The live app cannot chat until #29, and upgrading fails until #30] → The URL is not shared
  before #29. A failed upgrade shows the existing error message.
- [Data protection keys live in the container and are lost on every restart] → Only the external
  sign-in cookie depends on them, and Google sign-in is out of scope. Persisting keys to MongoDB
  belongs with turning Google sign-in on.
- [512 MB of RAM] → The .NET runtime sizes its heap from the container limit. If it runs short,
  `DOTNET_GCHeapHardLimit` or workstation GC is the first lever.
- [A GHCR package starts private, and Render cannot pull a private image without credentials] →
  The deployment guide makes it public after the first publish.
- [The deploy hook URL is a credential: anyone holding it can redeploy] → It is a GitHub secret
  scoped to the `production` environment. A deploy can only pick tags of our own image, and the
  hook can be regenerated in Render.
- [Secure cookies over `http://localhost` in the compose run] → Chrome and Firefox treat localhost
  as secure. Safari may drop the refresh cookie, so a reload signs the user out there. The README
  says to use Chrome or Firefox for the local image.
- [On Linux, Ollama listens only on `127.0.0.1`, which a container cannot reach through
  `host-gateway`] → The README notes `OLLAMA_HOST=0.0.0.0` for Linux. Docker Desktop and OrbStack
  on macOS forward to the host's loopback, so they need nothing.

## Migration Plan

GitHub only offers to run a `workflow_dispatch` workflow that exists on the default branch, so the
first deploy can happen only after this change merges. After that, the Deploy workflow can deploy
any branch. The demo link goes into the README in a follow-up docs commit once the first deploy
is live.

One-time setup, written up in `docs/deployment.md`:
1. Atlas: a free cluster, the database user, network access from anywhere, and the connection
   string.
2. Run Deploy once. `checks` and `publish` succeed; `release` fails because the hook is not set
   yet. Make the `chatapp` package on GHCR public.
3. Render: a web service from that image, on the free instance, with:
   - health check path `/healthz`;
   - auto-deploy off;
   - environment variables:
     - `PORT=8080`, so Render routes to the port the app listens on;
     - `MongoDB__ConnectionString` and `MongoDB__DatabaseName`;
     - `Jwt__Secret`, a generated 64-byte value;
     - `Ollama__BaseUrl` and `Ollama__Model` until #29.
4. GitHub:
   - the `production` environment with the `RENDER_DEPLOY_HOOK_URL` secret;
   - the `LIVE_URL` variable.
5. Run Deploy again. It finishes when `/healthz` names the commit.

Rollback: run Deploy on an earlier commit's branch, or redeploy an earlier image from Render's
dashboard. Every deployed image stays in GHCR under its commit.
