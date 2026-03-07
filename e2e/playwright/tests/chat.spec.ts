import { test, expect } from '@playwright/test';
import { ChatPage } from './helpers/chat-page';

test.describe('Chat', () => {
  // 2.5 — Page elements are present
  test('chat page has all required elements', async ({ page }) => {
    const chat = new ChatPage(page);
    await chat.goto();

    await expect(chat.getInput()).toBeVisible();
    await expect(chat.getSendButton()).toBeVisible();
  });

  // 2.3 — Empty message cannot be sent
  test('send button is disabled when input is empty', async ({ page }) => {
    const chat = new ChatPage(page);
    await chat.goto();

    await expect(chat.getSendButton()).toBeDisabled();

    await chat.getInput().fill('Hello');
    await expect(chat.getSendButton()).toBeEnabled();

    await chat.getInput().clear();
    await expect(chat.getSendButton()).toBeDisabled();
  });

  // 2.1 — Send message and receive echo response
  test('user can send a message and receive an echo response', async ({ page }) => {
    const chat = new ChatPage(page);
    await chat.goto();

    await chat.sendMessage('Hello, World!');

    await expect(chat.getUserMessages().first()).toHaveText('Hello, World!');
    await chat.waitForAssistantResponse(1);
    await expect(chat.getAssistantMessages().first()).toHaveText('Echo: Hello, World!');
  });

  // 2.2 — Multiple messages in sequence
  test('user can send multiple messages and all appear in order', async ({ page }) => {
    const chat = new ChatPage(page);
    await chat.goto();

    await chat.sendMessage('First message');
    await chat.waitForAssistantResponse(1);

    await chat.sendMessage('Second message');
    await chat.waitForAssistantResponse(2);

    await chat.sendMessage('Third message');
    await chat.waitForAssistantResponse(3);

    const userMsgs = chat.getUserMessages();
    await expect(userMsgs).toHaveCount(3);
    await expect(userMsgs.nth(0)).toHaveText('First message');
    await expect(userMsgs.nth(1)).toHaveText('Second message');
    await expect(userMsgs.nth(2)).toHaveText('Third message');

    const assistantMsgs = chat.getAssistantMessages();
    await expect(assistantMsgs).toHaveCount(3);
    await expect(assistantMsgs.nth(0)).toHaveText('Echo: First message');
    await expect(assistantMsgs.nth(1)).toHaveText('Echo: Second message');
    await expect(assistantMsgs.nth(2)).toHaveText('Echo: Third message');
  });

  // 4.2 — Messages persist within a conversation session
  test('messages persist within a conversation session', async ({ page }) => {
    const chat = new ChatPage(page);
    await chat.goto();

    await chat.sendMessage('Message 1');
    await chat.waitForAssistantResponse(1);

    await chat.sendMessage('Message 2');
    await chat.waitForAssistantResponse(2);

    await expect(chat.getUserMessages()).toHaveCount(2);
    await expect(chat.getAssistantMessages()).toHaveCount(2);

    await expect(chat.getUserMessages().nth(0)).toHaveText('Message 1');
    await expect(chat.getUserMessages().nth(1)).toHaveText('Message 2');
    await expect(chat.getAssistantMessages().nth(0)).toHaveText('Echo: Message 1');
    await expect(chat.getAssistantMessages().nth(1)).toHaveText('Echo: Message 2');
  });

  // Cycle 4.1 — Streaming: typing indicator appears then response arrives
  test('response streams word by word with typing indicator', async ({ page }) => {
    const chat = new ChatPage(page);
    await chat.goto();

    await chat.getInput().fill('Hello World');
    await chat.getSendButton().click();

    // Typing indicator appears while streaming
    await expect(page.getByLabel('typing indicator')).toBeVisible();

    // Full response eventually appears (streaming completes)
    await chat.waitForAssistantResponse(1);
    await expect(chat.getAssistantMessages().first()).toHaveText('Echo: Hello World');

    // Typing indicator disappears once done
    await expect(page.getByLabel('typing indicator')).not.toBeVisible();
  });

  // 2.4 — Input disabled during streaming
  test('input is disabled while streaming response', async ({ page }) => {
    const chat = new ChatPage(page);
    await chat.goto();

    await chat.getInput().fill('Hello there today');
    await chat.getSendButton().click();

    // Input is disabled while streaming
    await expect(chat.getInput()).toBeDisabled();

    // Input re-enables after stream completes
    await chat.waitForAssistantResponse(1);
    await expect(chat.getInput()).toBeEnabled();
  });
});
