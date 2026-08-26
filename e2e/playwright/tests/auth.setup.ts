import { test as setup, expect } from '@playwright/test';
import path from 'path';

export const STORAGE_STATE = path.join(__dirname, '..', '.auth', 'user.json');

/**
 * Registers a fresh account once per run and saves the resulting localStorage
 * token, so the chat specs start already signed in instead of each paying the
 * cost of the login journey. Registration is used rather than login because the
 * backend database is not reset between runs — a new email always succeeds.
 *
 * "Keep me signed in" is checked deliberately: an ordinary session lives in
 * sessionStorage and a browser-session cookie, neither of which storageState
 * carries into the specs. A shared signed-in state is exactly the case the
 * remembered session is for.
 */
setup('create authenticated user', async ({ page }) => {
  const email = `e2e-${Date.now()}@test.local`;

  await page.goto('/');

  // In login mode the text button toggles to the register form.
  await page.getByRole('button', { name: 'Register' }).click();

  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password').fill('E2ePassw0rd!');
  await page.getByLabel('Display Name').fill('E2E User');
  await page.getByLabel('Keep me signed in').check();

  // The submit button is now the one labelled Register.
  await page.getByRole('button', { name: 'Register' }).click();

  // Landing on the chat screen is what proves the token was stored.
  await expect(page.getByPlaceholder('Ask anything')).toBeVisible({ timeout: 30_000 });

  await page.context().storageState({ path: STORAGE_STATE });
});
