import { test, expect } from '@playwright/test';
import { ChatPage, STREAM_TIMEOUT } from './helpers/chat-page';

/**
 * These run against the real backend and a real local LLM, so replies are
 * non-deterministic. Assertions check the shape of the interaction — a reply
 * arrives, it is non-empty, it lands in the right order — never its wording.
 */
test.describe('Chat', () => {
  // Page elements are present
  test('chat page has all required elements', async ({ page }) => {
    const chat = new ChatPage(page);
    await chat.goto();

    await expect(chat.getInput()).toBeVisible();
    await expect(chat.getSendButton()).toBeVisible();
  });

  // Empty message cannot be sent
  test('send button is disabled when input is empty', async ({ page }) => {
    const chat = new ChatPage(page);
    await chat.goto();

    await expect(chat.getSendButton()).toBeDisabled();

    await chat.getInput().fill('Hello');
    await expect(chat.getSendButton()).toBeEnabled();

    await chat.getInput().clear();
    await expect(chat.getSendButton()).toBeDisabled();
  });

  // Send a message and receive a reply from the model
  test('user can send a message and receive a reply', async ({ page }) => {
    const chat = new ChatPage(page);
    await chat.goto();

    await chat.sendAndAwaitReply('Say hello in three words.', 1);

    await expect(chat.getUserMessages().first()).toHaveText('Say hello in three words.');
    // Any non-whitespace reply proves the round trip; the wording is the model's.
    await expect(chat.getAssistantMessages().first()).toHaveText(/\S/);
  });

  // Multiple messages in sequence
  test('user can send multiple messages and all appear in order', async ({ page }) => {
    test.slow(); // three sequential model replies
    const chat = new ChatPage(page);
    await chat.goto();

    await chat.sendAndAwaitReply('Say the word first.', 1);
    await chat.sendAndAwaitReply('Say the word second.', 2);
    await chat.sendAndAwaitReply('Say the word third.', 3);

    const userMsgs = chat.getUserMessages();
    await expect(userMsgs).toHaveCount(3);
    await expect(userMsgs.nth(0)).toHaveText('Say the word first.');
    await expect(userMsgs.nth(1)).toHaveText('Say the word second.');
    await expect(userMsgs.nth(2)).toHaveText('Say the word third.');

    const assistantMsgs = chat.getAssistantMessages();
    await expect(assistantMsgs).toHaveCount(3);
    for (let i = 0; i < 3; i++) {
      await expect(assistantMsgs.nth(i)).toHaveText(/\S/);
    }
  });

  // Messages persist within a conversation session
  test('messages persist within a conversation session', async ({ page }) => {
    test.slow(); // two sequential model replies
    const chat = new ChatPage(page);
    await chat.goto();

    await chat.sendAndAwaitReply('Reply with the word one.', 1);
    await chat.sendAndAwaitReply('Reply with the word two.', 2);

    await expect(chat.getUserMessages()).toHaveCount(2);
    await expect(chat.getAssistantMessages()).toHaveCount(2);

    // Earlier turns stay on screen rather than being replaced by the latest one.
    await expect(chat.getUserMessages().nth(0)).toHaveText('Reply with the word one.');
    await expect(chat.getUserMessages().nth(1)).toHaveText('Reply with the word two.');
  });

  // Streaming: typing indicator appears, then the response arrives
  test('response streams with a typing indicator', async ({ page }) => {
    const chat = new ChatPage(page);
    await chat.goto();

    await chat.sendMessage('Count to three.');

    await expect(chat.getTypingIndicator()).toBeVisible({ timeout: STREAM_TIMEOUT });

    await chat.waitForAssistantResponse(1);
    await chat.waitForStreamComplete();

    await expect(chat.getAssistantMessages().first()).toHaveText(/\S/);
    await expect(chat.getTypingIndicator()).toBeHidden();
  });

  // Input is disabled during streaming
  test('input is disabled while streaming response', async ({ page }) => {
    const chat = new ChatPage(page);
    await chat.goto();

    await chat.sendMessage('Say hi.');

    await expect(chat.getInput()).toBeDisabled();

    await chat.waitForAssistantResponse(1);
    await chat.waitForStreamComplete();

    await expect(chat.getInput()).toBeEnabled();
  });
});
