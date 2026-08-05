import { test, expect } from '@playwright/test';

// The signed-in storage state from the setup project would skip the login gate
// entirely, which is the thing these tests exist to exercise.
test.use({ storageState: { cookies: [], origins: [] } });

test.describe('Authentication', () => {
  test('unauthenticated visitor sees the sign in form, not the chat', async ({ page }) => {
    await page.goto('/');

    await expect(page.getByRole('heading', { name: 'Sign in' })).toBeVisible();
    await expect(page.getByPlaceholder('Ask anything')).toBeHidden();
  });

  test('user can register and land in the chat', async ({ page }) => {
    await page.goto('/');
    await page.getByRole('button', { name: 'Register' }).click();

    await expect(page.getByRole('heading', { name: 'Create account' })).toBeVisible();

    await page.getByLabel('Email').fill(`e2e-reg-${Date.now()}@test.local`);
    await page.getByLabel('Password').fill('E2ePassw0rd!');
    await page.getByLabel('Display Name').fill('Register Journey');
    await page.getByRole('button', { name: 'Register' }).click();

    await expect(page.getByPlaceholder('Ask anything')).toBeVisible({ timeout: 30_000 });
  });

  test('registered user can sign out and sign back in', async ({ page }) => {
    const email = `e2e-login-${Date.now()}@test.local`;
    const password = 'E2ePassw0rd!';

    await page.goto('/');
    await page.getByRole('button', { name: 'Register' }).click();
    await page.getByLabel('Email').fill(email);
    await page.getByLabel('Password').fill(password);
    await page.getByLabel('Display Name').fill('Login Journey');
    await page.getByRole('button', { name: 'Register' }).click();
    await expect(page.getByPlaceholder('Ask anything')).toBeVisible({ timeout: 30_000 });

    await page.getByRole('button', { name: /logout|sign out/i }).click();
    await expect(page.getByRole('heading', { name: 'Sign in' })).toBeVisible();

    await page.getByLabel('Email').fill(email);
    await page.getByLabel('Password').fill(password);
    await page.getByRole('button', { name: 'Login' }).click();

    await expect(page.getByPlaceholder('Ask anything')).toBeVisible({ timeout: 30_000 });
  });

  test('wrong password shows an error and stays on the sign in form', async ({ page }) => {
    await page.goto('/');

    await page.getByLabel('Email').fill('nobody-e2e@test.local');
    await page.getByLabel('Password').fill('definitely-wrong');
    await page.getByRole('button', { name: 'Login' }).click();

    await expect(page.getByRole('alert')).toBeVisible();
    await expect(page.getByPlaceholder('Ask anything')).toBeHidden();
  });
});
