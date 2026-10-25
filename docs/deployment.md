# Deployment

The live demo runs one Docker image on Render's free web service, with its data in a MongoDB Atlas
free cluster. Deploys are started by hand from GitHub Actions. Nothing deploys on a push or a merge.

```
GitHub Actions "Deploy" ──▶ checks (CI) ──▶ publish ghcr.io/asifarshadullah/chatapp:<commit>
                                                   │
                         Render deploy hook ◀──────┘   ──▶ waits until /healthz names <commit>
```

## Deploying a branch

1. In GitHub, open **Actions → Deploy → Run workflow** and pick the branch.
2. **Checks** runs the backend and frontend jobs from CI on that branch. If any fail, nothing is
   published and the live app is unchanged.
3. **Publish image** pushes `ghcr.io/asifarshadullah/chatapp:<full commit sha>`.
4. **Release** asks Render to run that tag, then waits until `https://<live url>/healthz` reports
   the commit. It fails after 10 minutes.

GitHub only offers **Run workflow** for workflows that exist on `main`, but once `deploy.yml` is
there, any branch that also has it can be deployed.

## Rolling back

Deploy an earlier commit. Either:
- run Deploy on a branch pointing at that commit, or
- in Render, open the service's **Events** and redeploy an earlier deploy. Every deployed image
  stays in GHCR under its commit.

## One-time setup

### 1. MongoDB Atlas

1. Create a free cluster.
2. **Database Access:** add a user with a long generated password and the built-in role
   `readWrite` on the `chatapp` database only.
3. **Network Access:** allow `0.0.0.0/0`. Render's free tier has no fixed outbound IP, so the user
   and its password are what protect the database.
4. Copy the connection string (`mongodb+srv://...`), with the user and password filled in.

### 2. The first image

1. Run **Deploy** once from `main`. **Checks** and **Publish image** succeed. **Release** fails
   because the hook is not set yet.
2. In GitHub, open the `chatapp` package (your profile → **Packages**), **Package settings → Change
   visibility → Public**. Render pulls the image without credentials.

### 3. Render

1. **New → Web Service → Existing image**, with
   `ghcr.io/asifarshadullah/chatapp:<the commit sha from step 2>`.
2. Instance type **Free**.
3. **Health Check Path:** `/healthz`.
4. **Auto-Deploy:** off. Deploys come only from the workflow.
5. **Environment variables:**

   | Name | Value |
   |---|---|
   | `PORT` | `8080` (the port the app listens on) |
   | `MongoDB__ConnectionString` | the Atlas connection string |
   | `MongoDB__DatabaseName` | `chatapp` |
   | `Jwt__Secret` | a new secret, for example `openssl rand -base64 64`. The app refuses to start without one of at least 32 bytes. |
   | `Ollama__BaseUrl`, `Ollama__Model` | until #29 adds a hosted model, chat has no model to reach |

   Stripe is left unset until #30, so upgrading a plan fails. Google sign-in is left unset, and
   `/auth/google` answers 404.

6. **Settings → Deploy Hook:** copy the URL. It is a credential: anyone holding it can redeploy.
   Regenerate it here if it leaks.

### 4. GitHub

1. **Settings → Environments → New environment** `production`.
2. In it, add the secret `RENDER_DEPLOY_HOOK_URL` with the hook URL.
3. Add the variable `LIVE_URL` with the service's URL, for example
   `https://chatapp-xxxx.onrender.com`.

### 5. Deploy

Run **Deploy** again. It finishes when `/healthz` on the live URL names the commit.

## Things to know

- The free instance sleeps after 15 minutes without traffic. The first request after that waits
  about a minute.
- The image is the one `docker compose --profile app up --build` runs locally, so a problem in the
  image shows up on a laptop first.
- Don't share the URL until #10, #15 and #29 are done.
