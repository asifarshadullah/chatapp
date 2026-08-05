import { expect, type Page, type Locator } from '@playwright/test';

/** Local LLM replies are far slower than a stubbed echo, so waits are generous. */
export const STREAM_TIMEOUT = 120_000;

export class ChatPage {
  constructor(private page: Page) {}

  async goto() {
    await this.page.goto('/');
    await expect(this.getInput()).toBeVisible();
  }

  async sendMessage(text: string) {
    await this.page.getByPlaceholder('Ask anything').fill(text);
    await this.page.getByRole('button', { name: 'Send' }).click();
  }

  async sendMessageViaEnter(text: string) {
    await this.page.getByPlaceholder('Ask anything').fill(text);
    await this.page.getByPlaceholder('Ask anything').press('Enter');
  }

  getUserMessages(): Locator {
    return this.page.locator('.bubble--user');
  }

  getAssistantMessages(): Locator {
    return this.page.locator('.bubble--assistant');
  }

  getSendButton(): Locator {
    return this.page.getByRole('button', { name: 'Send' });
  }

  getInput(): Locator {
    return this.page.getByPlaceholder('Ask anything');
  }

  getTypingIndicator(): Locator {
    return this.page.getByLabel('typing indicator');
  }

  /** Waits for the nth assistant bubble to exist. It may still be filling in. */
  async waitForAssistantResponse(count: number = 1) {
    await this.getAssistantMessages()
      .nth(count - 1)
      .waitFor({ state: 'visible', timeout: STREAM_TIMEOUT });
  }

  /**
   * Waits until streaming has finished. The bubble appears as soon as the first
   * token arrives, so asserting on its text before this returns races the
   * stream and yields a partial response.
   */
  async waitForStreamComplete() {
    await expect(this.getTypingIndicator()).toBeHidden({ timeout: STREAM_TIMEOUT });
    await expect(this.getInput()).toBeEnabled({ timeout: STREAM_TIMEOUT });
  }

  /** Sends a message and returns once the assistant's reply is fully streamed. */
  async sendAndAwaitReply(text: string, expectedCount: number) {
    await this.sendMessage(text);
    await this.waitForAssistantResponse(expectedCount);
    await this.waitForStreamComplete();
  }

  async isLoading(): Promise<boolean> {
    return this.page.getByRole('button', { name: 'Send' }).isDisabled();
  }
}
