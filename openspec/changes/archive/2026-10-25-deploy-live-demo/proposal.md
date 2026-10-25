## Why

The app only runs on a developer machine, so nobody can try it without cloning the repo and installing MongoDB and Ollama (#21). CI tests every push but nothing can be deployed: there is no Dockerfile, the frontend relies on the Vite dev proxy, and production settings exist nowhere.

## What Changes

- **A production image.** A multi-stage Dockerfile builds the React app and the API and serves both from ASP.NET on one origin. Deep links such as `/billing/success` load the app; unknown `/api` and `/auth` paths stay 404.
- **Runs behind a TLS proxy.** The app takes the scheme and host from the host's proxy headers, so it builds HTTPS URLs and does not redirect in a loop.
- **A health endpoint** the host can poll.
- **Refuses a known JWT secret.** Outside Development, the app will not start when `Jwt:Secret` is missing, too short, or the placeholder committed in `appsettings.json`. Anyone could forge tokens signed with that public value.
- **CORS removed.** The browser reaches the API on the same origin in development (through the Vite proxy) and in production, so no cross-origin access is granted. **BREAKING** for any client calling the API from another origin; none exists.
- **Manual deploys.** A `Deploy` workflow, started by hand from any branch, runs the CI jobs, and only if they pass pushes an image tagged with the commit to GitHub Container Registry and asks Render to deploy that image. Nothing deploys automatically.
- **CI builds the image** on every push and pull request, so a broken Dockerfile fails CI before anyone tries to deploy.
- **Local run in two commands.** Docker Compose gains an `app` profile that runs the same image against the local MongoDB and the host's Ollama.
- **README:** the CI status badge replaces the hard-coded test count, plus screenshots and the two-command local run. The live demo link follows the first deploy, which can only run once this change is on `main`.
- **Hosting:** Render's free web service and a MongoDB Atlas free cluster, configured through environment variables. Setting them up is a manual step, recorded in the docs.

Out of scope:
- a hosted model (#29): until it lands, the live app signs users in but cannot answer them;
- billing (#30): the Stripe settings keep their placeholders, so upgrading fails on the live app;
- Google sign-in: the UI has no button for it.

The URL is not shared until #10, #15 and #29 are done.

## Capabilities

### New Capabilities
- `platform/hosting`: how the deployed app serves the UI and the API from one origin, runs behind a TLS proxy, reports its health, and refuses an unsafe signing secret.
- `platform/deployment`: how a build reaches the live host — manually, from a chosen branch, only after the tests pass.

### Modified Capabilities

None.

## Impact

- **Backend:** `Program.cs` (static files and SPA fallback, forwarded headers, health checks, JWT secret validation, CORS removal); `appsettings.json`; new tests in `Chat.Api.Tests` and `Chat.Identity.Tests`.
- **Build and CI:** new `Dockerfile` and `.dockerignore`; `ci.yml` gains a Docker build job and becomes callable; new `deploy.yml`; `docker-compose.yml` gains the `app` profile.
- **Docs:** README; a deployment guide for the one-time Render, Atlas and GHCR setup.
- **External:** GitHub Container Registry (a public package), Render, and MongoDB Atlas. One GitHub secret, the Render deploy hook URL (pushing to GHCR uses the workflow's own token); runtime secrets live in Render.
- **Issues:** #21's "deploys `main` after CI" becomes a manual deploy from a chosen branch; #21 is updated to match.
