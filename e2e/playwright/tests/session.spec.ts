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

  async function signUp(page: import('@playwright/test').Page) {
    await page.goto('/');
    await page.getByRole('button', { name: 'Register' }).click();
    await page.getByLabel('Email').fill(`session-${Date.now()}-${Math.random().toString(36).slice(2)}@test.local`);
    await page.getByLabel('Password').fill('E2ePassw0rd!');
    await page.getByLabel('Display Name').fill('Session User');
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
});
