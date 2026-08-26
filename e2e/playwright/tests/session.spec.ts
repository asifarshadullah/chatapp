import { test, expect } from '@playwright/test';
import { ChatPage, STREAM_TIMEOUT } from './helpers/chat-page';

/**
 * Session continuity across access-token expiry, against the real backend.
 *
 * Rather than waiting out the configured lifetime, these rewrite the stored expiry so the
 * client treats its access token as stale. The refresh credential is an http-only cookie
 * the browser still holds, so the renewal exercised here is the real one.
 */
test.describe('Session', () => {
  // These tests must NOT reuse the shared signed-in state from auth.setup.ts. A refresh
  // credential is single-use: the first test to renew consumes the stored cookie, and the
  // next test replaying it looks exactly like a stolen token being replayed — which revokes
  // the family, as it should. Each test therefore establishes its own session.
  test.use({ storageState: { cookies: [], origins: [] } });

  /**
   * Registers a new account. `staySignedIn` defaults to true because these tests reload and
   * renew, and a remembered session is the one that survives a browser restart — the case
   * the rest of the suite assumes.
   */
  async function signUp(page: import('@playwright/test').Page, staySignedIn = true) {
    await page.goto('/');
    await page.getByRole('button', { name: 'Register' }).click();
    await page.getByLabel('Email').fill(`session-${Date.now()}-${Math.random().toString(36).slice(2)}@test.local`);
    await page.getByLabel('Password').fill('E2ePassw0rd!');
    await page.getByLabel('Display Name').fill('Session User');
    if (staySignedIn) await page.getByLabel('Keep me signed in').check();
    await page.getByRole('button', { name: 'Register' }).click();
    await expect(page.getByPlaceholder('Ask anything')).toBeVisible({ timeout: STREAM_TIMEOUT });
  }

  /** Makes the client consider its access token due for renewal on next use. */
  async function markTokenStale(page: import('@playwright/test').Page) {
    await page.evaluate(() => {
      localStorage.setItem('auth_token_expiry', new Date(Date.now() + 1_000).toISOString());
    });
  }

  test('a conversation survives access-token expiry', async ({ page }) => {
    const chat = new ChatPage(page);
    await signUp(page);

    await chat.sendMessage('Reply with the single word: first');
    await chat.waitForAssistantResponse(1);
    const firstReply = await chat.getAssistantMessages().first().innerText();

    await markTokenStale(page);

    // The next send must renew silently and go through.
    await chat.sendMessage('Reply with the single word: second');
    await chat.waitForAssistantResponse(2);

    await expect(chat.getInput()).toBeVisible();
    await expect(chat.getUserMessages()).toHaveCount(2);
    // The earlier exchange is still on screen — the session continued rather than restarting.
    expect(await chat.getAssistantMessages().first().innerText()).toBe(firstReply);
  });

  test('the user is not returned to sign in when the access token lapses', async ({ page }) => {
    const chat = new ChatPage(page);
    await signUp(page);
    await markTokenStale(page);

    await page.reload();

    await expect(chat.getInput()).toBeVisible({ timeout: STREAM_TIMEOUT });
    await expect(page.getByText('Your session has expired. Please sign in again.')).toHaveCount(0);
    await expect(page.getByText('Failed to connect to chat server.')).toHaveCount(0);
  });

  test('a stale token is renewed against the server on load', async ({ page }) => {
    const chat = new ChatPage(page);
    await signUp(page);
    await markTokenStale(page);

    const refreshed = page.waitForResponse(
      (r) => r.url().includes('/auth/refresh') && r.status() === 200,
    );
    await page.reload();
    const response = await refreshed;

    // Deliberately not asserting the new access token differs from the old one: two JWTs
    // minted in the same second for the same user carry identical claims, so they are the
    // same string. What matters is that the exchange happened and the session continued.
    expect(response.ok()).toBe(true);
    await expect(chat.getInput()).toBeVisible();
    expect(await page.evaluate(() => localStorage.getItem('auth_token'))).not.toBeNull();
  });

  test('signing out clears the refresh cookie', async ({ page, context }) => {
    await signUp(page);

    // Sign-out reaches the server asynchronously, and the cookie is only cleared when its
    // response arrives — so wait for it rather than racing the redraw.
    const loggedOut = page.waitForResponse((r) => r.url().includes('/auth/logout'));
    await page.getByRole('button', { name: 'Logout' }).click();
    await loggedOut;

    await expect(page.getByLabel('Email')).toBeVisible();

    const refreshCookie = (await context.cookies()).find((c) => c.name === 'refresh_token');
    expect(refreshCookie?.value ?? '').toBe('');
  });

  test('a signed-out session cannot be resumed', async ({ page }) => {
    await signUp(page);

    const loggedOut = page.waitForResponse((r) => r.url().includes('/auth/logout'));
    await page.getByRole('button', { name: 'Logout' }).click();
    await loggedOut;

    await page.reload();

    // No session marker and no cookie: the sign-in form, not a silent restoration.
    await expect(page.getByLabel('Password')).toBeVisible();
  });

  // ── Task 6.1 — the choice survives, or does not survive, a browser restart ─

  /**
   * Reopens the app the way closing and reopening the browser does: session cookies are
   * gone, persistent ones remain, and sessionStorage is empty while localStorage is not.
   */
  async function restartBrowser(
    context: import('@playwright/test').BrowserContext,
    browser: import('@playwright/test').Browser,
  ) {
    const state = await context.storageState();
    const restarted = await browser.newContext({
      storageState: {
        // expires === -1 marks a cookie the browser holds only for the browsing session.
        // Both the refresh credential of an unremembered session and the companion beacon
        // the client reads are such cookies, so filtering here reproduces a real restart.
        cookies: state.cookies.filter((c) => c.expires !== -1),
        origins: state.origins,
      },
    });
    return restarted;
  }

  /** Opening the app in another tab: same cookies, same localStorage, fresh sessionStorage. */
  async function openSecondTab(context: import('@playwright/test').BrowserContext) {
    const tab = await context.newPage();
    await tab.goto('/');
    return tab;
  }

  test('a remembered session survives closing the browser', async ({ page, context, browser }) => {
    await signUp(page, true);

    const restarted = await restartBrowser(context, browser);
    const reopened = await restarted.newPage();
    await reopened.goto('/');

    // Straight back into the app: the refresh cookie outlived the browsing session.
    await expect(reopened.getByPlaceholder('Ask anything')).toBeVisible({ timeout: STREAM_TIMEOUT });
    await expect(reopened.getByLabel('Password')).toHaveCount(0);
    await restarted.close();
  });

  test('an ordinary session does not survive closing the browser', async ({ page, context, browser }) => {
    await signUp(page, false);

    const restarted = await restartBrowser(context, browser);
    const reopened = await restarted.newPage();
    await reopened.goto('/');

    // The credential went with the browsing session, which is what declining asks for.
    await expect(reopened.getByLabel('Password')).toBeVisible();
    await expect(reopened.getByPlaceholder('Ask anything')).toHaveCount(0);
    await restarted.close();
  });

  test('an ordinary session still works while the browser stays open', async ({ page }) => {
    await signUp(page, false);

    await page.reload();

    // Declining to be remembered is not the same as being signed out on reload.
    await expect(page.getByPlaceholder('Ask anything')).toBeVisible({ timeout: STREAM_TIMEOUT });
  });

  test('a second tab of an ordinary session is signed in', async ({ page, context }) => {
    await signUp(page, false);

    const second = await openSecondTab(context);

    // Declining to be remembered ends the session at browser close, not at the tab it
    // started in. This is the regression the sessionStorage marker introduced.
    await expect(second.getByPlaceholder('Ask anything')).toBeVisible({ timeout: STREAM_TIMEOUT });
    await expect(second.getByLabel('Password')).toHaveCount(0);
    await second.close();
  });

  test('a second tab of a remembered session is signed in', async ({ page, context }) => {
    await signUp(page, true);

    const second = await openSecondTab(context);

    await expect(second.getByPlaceholder('Ask anything')).toBeVisible({ timeout: STREAM_TIMEOUT });
    await second.close();
  });

  test('an ordinary session makes no renewal attempt after a restart', async ({ page, context, browser }) => {
    await signUp(page, false);

    const restarted = await restartBrowser(context, browser);
    const reopened = await restarted.newPage();
    let renewalAttempted = false;
    reopened.on('request', (r) => {
      if (r.url().includes('/auth/refresh')) renewalAttempted = true;
    });
    await reopened.goto('/');
    await expect(reopened.getByLabel('Password')).toBeVisible();

    // A request that could only be refused would flash the app shell on the way to the
    // sign-in form; the client knows the credential went with the browsing session.
    expect(renewalAttempted).toBe(false);
    await restarted.close();
  });
});
