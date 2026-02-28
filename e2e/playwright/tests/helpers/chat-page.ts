import type { Page, Locator } from '@playwright/test';

export class ChatPage {
  constructor(private page: Page) {}

  async goto() {
    await this.page.goto('/');
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

  async waitForAssistantResponse(count: number = 1) {
    await this.page.locator('.bubble--assistant').nth(count - 1).waitFor({ state: 'visible' });
  }

  async isLoading(): Promise<boolean> {
    return this.page.getByRole('button', { name: 'Send' }).isDisabled();
  }
}
