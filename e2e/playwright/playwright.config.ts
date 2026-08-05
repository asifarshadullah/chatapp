import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: './tests',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  // The local LLM serves one request at a time, so parallel workers just queue
  // on it while making timeouts harder to reason about.
  workers: 1,
  // A cold model load plus a fully streamed reply comfortably exceeds the 30s default.
  timeout: 150_000,
  reporter: 'html',
  use: {
    baseURL: 'http://localhost:5173',
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
  },
  projects: [
    {
      name: 'setup',
      testMatch: /auth\.setup\.ts/,
    },
    {
      name: 'chromium',
      use: {
        ...devices['Desktop Chrome'],
        storageState: './.auth/user.json',
      },
      dependencies: ['setup'],
      testIgnore: /auth\.setup\.ts/,
    },
  ],
  webServer: [
    {
      command: 'dotnet run --project ../../backend/src/Chat.Api',
      url: 'http://localhost:5064/swagger',
      reuseExistingServer: !process.env.CI,
      // A cold `dotnet run` restores and builds before it starts listening.
      timeout: 180_000,
    },
    {
      command: 'npm run dev',
      cwd: '../../frontend/chat-ui',
      url: 'http://localhost:5173',
      reuseExistingServer: !process.env.CI,
      timeout: 60_000,
    },
  ],
});
