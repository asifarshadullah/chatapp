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
5. **Secret Files** (Environment → Secret Files → Add Secret File). Each file is named after
   the setting it holds, and contains only the value. Render mounts them at `/etc/secrets`,
   where the app reads them, and a file wins over an environment variable of the same name.
   Unlike environment variables, they stay out of the process environment.

   | File name | Contents |
   |---|---|
   | `MongoDB__ConnectionString` | the Atlas connection string |
   | `Jwt__Secret` | a new secret, for example `openssl rand -base64 48 \| tr -d '\n'`. The app refuses to start without one of at least 32 bytes. |
   | `Ai__Models__groq__ApiKey` | a Groq API key from https://console.groq.com/keys. The app refuses to start without it, because it is then offered no model. |

   Write each value on one line. One trailing line ending is dropped, and the Groq key is
   trimmed entirely, so a stray `\r` cannot break it; other values keep any extra whitespace.
   If a deploy fails at startup with `UnauthorizedAccessException` on `/etc/secrets`, the app's
   non-root user cannot read the files. Put the same values in environment variables of the same
   names instead: they work too, just less tightly held.

6. **Environment variables**, which are not secret:

   | Name | Value |
   |---|---|
   | `PORT` | `8080` (the port the app listens on) |
   | `MongoDB__DatabaseName` | `chatapp` |

   Everything else about the Groq option (`https://api.groq.com/openai/v1`, `openai/gpt-oss-120b`,
   reasoning effort `low`) ships in `appsettings.json`. These are optional:

   | Name | Default | Why you might set it |
   |---|---|---|
   | `Ai__Models__groq__Model` | `openai/gpt-oss-120b` | Groq retired the model; check https://console.groq.com/docs/deprecations |
   | `Billing__Plans__Free__DailyMessages` | `10` | a Free user's messages per UTC day; lower it if the shared Groq allowance runs out |
   | `Billing__Plans__Pro__DailyMessages` | `100` | a Pro user's messages per UTC day |
   | `Ai__MaxHistoryMessages` | `10` | how many of a conversation's latest messages each request carries |
   | `Ai__MaxReplyTokens` | `800` | the longest reply, in tokens |
   | `Ai__MaxTitleTokens` | `300` | the most a conversation's title may use, reasoning included; raise it if titles stay as the first message |

   Until the Stripe Secret Files from step 6 are added, the Manage Plan page says upgrades are
   turned off. Google sign-in is left unset, and `/auth/google` answers 404.

7. **Settings → Deploy Hook:** copy the URL. It is a credential: anyone holding it can redeploy.
   Regenerate it here if it leaks.

### 4. GitHub

1. **Settings → Environments → New environment** `production`.
2. In it, add the secret `RENDER_DEPLOY_HOOK_URL` with the hook URL.
3. Add the variable `LIVE_URL` with the service's URL, for example
   `https://chatapp-xxxx.onrender.com`.

### 5. Deploy

Run **Deploy** again. It finishes when `/healthz` on the live URL names the commit.

Then sign up on the live URL and send one message. The app can only check that a key is set, not
that Groq accepts it, so this first reply is what shows the key works.

### 6. Stripe, in test mode

The demo takes no real money: it runs on Stripe's test mode, where only test cards work. The app
refuses to start with a live key (`sk_live_...`) unless `Stripe__AllowLiveKey` is `true`; leave it
unset.

1. In the Stripe dashboard, switch to **Test mode**.
2. **Product catalog → Add product:** Pro, recurring, monthly, USD 19, matching the price the app
   shows. Copy the price ID (`price_...`).
3. **Developers → Webhooks → Add endpoint:** `https://chatapp-xxxx.onrender.com/webhooks/stripe`
   with these events:
   - `checkout.session.completed`
   - `customer.subscription.created`, `customer.subscription.updated`, `customer.subscription.deleted`
   - `invoice.paid`, `invoice.payment_succeeded`, `invoice.payment_failed`

   Copy its signing secret (`whsec_...`).
4. On Render, add three more Secret Files:

   | File name | Contents |
   |---|---|
   | `Stripe__SecretKey` | the test secret key (`sk_test_...`) from **Developers → API keys** |
   | `Stripe__WebhookSecret` | the webhook's signing secret |
   | `Stripe__PriceIds__Pro` | the Pro price ID |

5. Run **Deploy**. Manage Plan now says it is in test mode.
6. Sign up as a new user, subscribe to Pro with card `4242 4242 4242 4242`, and check that Manage
   Plan shows Pro and a renewal date. Then cancel, and check it shows the date Pro ends.

Every Stripe event makes the app read the subscription back from Stripe, so events may arrive late,
twice or out of order. If the free instance is asleep, Stripe retries the event until it answers.

## Things to know

- The free instance sleeps after 15 minutes without traffic. The first request after that waits
  about a minute.
- When changing environment variables or Secret Files, choose **Save only**, then run Deploy.
  **Save and deploy** restarts the image set when the service was created in step 3, not the
  last one deployed, so the live app goes back to that first commit or fails to start.
- The image is the one `docker compose --profile app up --build` runs locally, so a problem in the
  image shows up on a laptop first.
- Groq's free tier allows about 8,000 tokens a minute and 200,000 a day for `gpt-oss-120b`, shared
  by every user: roughly 100 messages a day in all. When it runs out, users see "The demo has
  reached its model limit for now. Please try again later." until Groq's window resets.
- The deployed app offers only Groq. The local model is defined only in
  `appsettings.Development.json`, which is not in the image.
- Anyone can reach Pro with the test card. That is the point of the demo; Groq's shared daily
  allowance still bounds how much the app can be used.

Secrets are held in two places only: Render, for the live app, and your Mac's Keychain or user
secrets, for local runs. GitHub holds only the deploy hook URL. CI uses a fake model, and Deploy never needs
the Groq key.

## Moving to plan-based limits

An image built before #30 read one daily limit, `Chat__DailyMessageLimit`, for everyone. The
limit now follows the user's plan. If that variable is set on Render, delete it; set
`Billing__Plans__Free__DailyMessages` instead if Free needs a different limit.

## Moving from the Ollama settings

An image built before #29 read `Ollama__BaseUrl` and `Ollama__Model`, and its secrets from
environment variables. Before deploying a newer one:
1. On Render, add the three Secret Files above: `Ai__Models__groq__ApiKey` with the Groq key, and
   `Jwt__Secret` and `MongoDB__ConnectionString` with the values the environment variables hold
   now.
2. Run **Deploy** on `main`. Without the key the new image refuses to start, and Render keeps the
   previous deploy running.
3. Once it is green, delete the environment variables `Jwt__Secret`, `MongoDB__ConnectionString`,
   `Ollama__BaseUrl` and `Ollama__Model`. The image then reads its secrets only from the files.
   Deleting them before step 2 would break the running deploy, which still reads them.
4. Sign in on the live app and send a message.
