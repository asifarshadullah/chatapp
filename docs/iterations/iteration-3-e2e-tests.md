# Iteration 3: Playwright E2E Tests

## Goal
Add end-to-end tests that verify the full user flow: open the app, send a message,
see the echo response — all automated with Playwright.

## Context
Backend (Iteration 1) and frontend (Iteration 2) are both working.
E2E tests validate the full-stack integration automatically.

## Prerequisites
- Iteration 1 & 2 complete — backend + frontend working together
- Backend running at `http://localhost:5xxx`
- Frontend running at `http://localhost:5173`

---

## Phase 1: Setup

### Task 1.1: Scaffold Playwright project
**Directory:** `e2e/playwright/`

```bash
mkdir -p e2e/playwright && cd e2e/playwright
npm init playwright@latest
```

Configuration choices:
- Language: TypeScript
- Tests directory: `tests/`
- Install browsers: Chromium only (for speed)
- Add GitHub Actions workflow: No (can add later)

### Task 1.2: Configure Playwright
**File:** `e2e/playwright/playwright.config.ts`

Key configuration:
```typescript
export default defineConfig({
  testDir: './tests',
  fullyParallel: true,
  retries: process.env.CI ? 2 : 0,
  reporter: 'html',
  use: {
    baseURL: 'http://localhost:5173',
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
  },
  projects: [
    { name: 'chromium', use: { ...devices['Desktop Chrome'] } },
  ],
  webServer: [
    {
      command: 'dotnet run --project ../../backend/src/Chat.Api',
      url: 'http://localhost:5000/swagger',
      reuseExistingServer: !process.env.CI,
      timeout: 30000,
    },
    {
      command: 'npm run dev',
      cwd: '../../frontend/chat-ui',
      url: 'http://localhost:5173',
      reuseExistingServer: !process.env.CI,
      timeout: 15000,
    },
  ],
});
```

The `webServer` config automatically starts both backend and frontend before tests run.

---

## Phase 2: E2E Tests

### Task 2.1: Test — Send message and receive echo response
**File:** `e2e/playwright/tests/chat.spec.ts`

```
Test: "user can send a message and receive an echo response"
1. Navigate to /
2. Verify chat input is visible
3. Type "Hello, World!" in the chat input
4. Click send button (or press Enter)
5. Assert: user message "Hello, World!" is visible in the chat
6. Assert: assistant response "Echo: Hello, World!" is visible in the chat
```

### Task 2.2: Test — Multiple messages in sequence
```
Test: "user can send multiple messages and all appear in order"
1. Navigate to /
2. Send "First message"
3. Wait for echo response
4. Send "Second message"
5. Wait for echo response
6. Send "Third message"
7. Wait for echo response
8. Assert: All 6 messages visible (3 user + 3 assistant) in correct order
```

### Task 2.3: Test — Empty message cannot be sent
```
Test: "send button is disabled when input is empty"
1. Navigate to /
2. Assert: send button is disabled (or clicking does nothing)
3. Type "Hello"
4. Assert: send button is enabled
5. Clear input
6. Assert: send button is disabled again
```

### Task 2.4: Test — Loading state during API call
```
Test: "shows loading indicator while waiting for response"
1. Navigate to /
2. Type "Hello" and send
3. Assert: loading indicator appears
4. Assert: loading indicator disappears when response arrives
```

### Task 2.5: Test — Page elements are present
```
Test: "chat page has all required elements"
1. Navigate to /
2. Assert: chat message area is visible
3. Assert: text input is visible
4. Assert: send button is visible
```

---

## Phase 3: Helper utilities

### Task 3.1: Create page object or helper functions
**File:** `e2e/playwright/tests/helpers/chat-page.ts`

Encapsulate common actions:
```typescript
export class ChatPage {
  constructor(private page: Page) {}

  async sendMessage(text: string) { ... }
  async getMessages() { ... }
  async getUserMessages() { ... }
  async getAssistantMessages() { ... }
  async isLoading() { ... }
}
```

This keeps tests clean and maintainable as the UI evolves in later iterations.

---

## Acceptance criteria
1. `npx playwright test` — all tests pass
2. Tests automatically start backend + frontend via `webServer` config
3. Tests run in headless Chromium
4. Test report generated in `playwright-report/`
5. Tests complete in under 30 seconds
6. Page object/helpers encapsulate UI interactions

## Verification commands
```bash
cd e2e/playwright

# Install dependencies
npm install

# Install Chromium browser
npx playwright install chromium

# Run all E2E tests
npx playwright test

# Run with visible browser (for debugging)
npx playwright test --headed

# Run with Playwright UI (interactive debugging)
npx playwright test --ui

# Show test report
npx playwright show-report
```

## What you will learn
- Playwright setup and configuration
- `webServer` config for automatic backend/frontend startup
- Page Object pattern for maintainable E2E tests
- Assertions for async UI behavior (waiting for elements)
- Debugging E2E tests with traces and screenshots
