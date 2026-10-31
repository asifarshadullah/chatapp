import { randomUUID } from 'crypto';
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

  // ── Reopening a conversation from its URL ────────────────────────────────

  test('a reload reopens the conversation named in the URL', async ({ page }) => {
    const chat = new ChatPage(page);
    await chat.goto();
    await chat.sendAndAwaitReply('Say hello in three words.', 1);
    await expect(page).toHaveURL(/[?&]conversation=[0-9a-f-]{36}/);
    const reply = await chat.getAssistantMessages().first().innerText();

    await page.reload();

    await expect(chat.getUserMessages()).toHaveText(['Say hello in three words.']);
    await expect(chat.getAssistantMessages()).toHaveText([reply]);
  });

  test('an unknown conversation in the URL is not found', async ({ page }) => {
    await page.goto(`/?conversation=${randomUUID()}`);

    await expect(page.getByText('Conversation not found.')).toBeVisible();
    await expect(page).not.toHaveURL(/conversation=/);
  });

  test('new chat empties the window and the URL', async ({ page }) => {
    const chat = new ChatPage(page);
    await chat.goto();
    await chat.sendAndAwaitReply('Say hello in three words.', 1);

    await page.getByRole('button', { name: 'New chat' }).click();

    await expect(chat.getUserMessages()).toHaveCount(0);
    await expect(page).not.toHaveURL(/conversation=/);
  });

  test('a conversation can be found in the sidebar and reopened', async ({ page }) => {
    const chat = new ChatPage(page);
    await chat.goto();
    const question = `Reply with the single word: sidebar ${randomUUID().slice(0, 8)}`;
    await chat.sendAndAwaitReply(question, 1);
    const conversationId = new URL(page.url()).searchParams.get('conversation');
    expect(conversationId).toMatch(/^[0-9a-f-]{36}$/);

    // The title is the model's, so the entry is found by being the open one, not by its text.
    const conversations = page.getByRole('list', { name: 'Conversations' });
    const current = conversations.locator('[aria-current="page"]');
    await expect(current).toHaveCount(1);
    await expect(current).toHaveText(/\S/);
    const title = await current.innerText();

    await page.getByRole('button', { name: 'New chat' }).click();
    await expect(chat.getUserMessages()).toHaveCount(0);
    await expect(current).toHaveCount(0);

    await conversations.getByRole('button', { name: title, exact: true }).first().click();

    await expect(page).toHaveURL(new RegExp(`conversation=${conversationId}`));
    await expect(chat.getUserMessages()).toHaveText([question]);
    await expect(chat.getAssistantMessages()).toHaveCount(1);
  });

  test('no message sits under a header button, from desktop to phone width', async ({ page }) => {
    const chat = new ChatPage(page);
    await chat.goto();
    await chat.sendAndAwaitReply(
      'Reply with the single word: ready. This question is long on purpose, so that its bubble spans the chat.',
      1,
    );
    const firstMessage = chat.getUserMessages().first();

    for (const width of [1280, 1024, 390]) {
      await page.setViewportSize({ width, height: 800 });
      // Scrolled to the top of the list, where a header laid over it would cover the message.
      await firstMessage.evaluate((el) => el.scrollIntoView({ block: 'start' }));
      const message = (await firstMessage.boundingBox())!;

      const names = ['Settings', 'Manage Plan', 'Logout', ...(width < 900 ? ['Open conversations'] : [])];
      for (const name of names) {
        const button = (await page.getByRole('button', { name, exact: true }).boundingBox())!;
        const overlaps =
          button.x < message.x + message.width &&
          message.x < button.x + button.width &&
          button.y < message.y + message.height &&
          message.y < button.y + button.height;
        expect(overlaps, `${name} overlaps the first message at ${width} px`).toBe(false);
        expect(button.x, `${name} starts off screen at ${width} px`).toBeGreaterThanOrEqual(0);
        expect(button.x + button.width, `${name} ends off screen at ${width} px`).toBeLessThanOrEqual(width);
      }
    }
  });

  test('a reload during a reply keeps what was produced', async ({ page }) => {
    const chat = new ChatPage(page);
    await chat.goto();
    await chat.sendAndAwaitReply('Reply with the single word: ready', 1);
    await chat.sendMessage('Count from one to fifty in words, one number per line.');
    await chat.waitForAssistantResponse(2);

    // The partial reply is saved after the server notices the reload, which can come after
    // the reloaded page asks for history, so a later reload may be the one that shows it.
    await expect(async () => {
      await page.reload();
      await expect(chat.getAssistantMessages()).toHaveCount(2, { timeout: 2_000 });
    }).toPass({ timeout: 30_000 });
    await expect(chat.getUserMessages().nth(1)).toHaveText(
      'Count from one to fifty in words, one number per line.',
    );
    await expect(chat.getAssistantMessages().nth(1)).toHaveText(/\S/);
  });

  test('the Settings dialog shows the local model, remembers it, and it answers', async ({ page }) => {
    const chat = new ChatPage(page);
    await chat.goto();

    const model = async () => {
      await page.getByRole('button', { name: 'Settings' }).click();
      const dialog = page.getByRole('dialog', { name: 'Settings' });
      await expect(dialog.getByRole('combobox', { name: 'Model' })).toHaveText('Local · gemma3:4b');
      return dialog;
    };

    await (await model()).getByRole('button', { name: 'Save' }).click();
    await expect(page.getByRole('dialog')).toBeHidden();
    await page.reload();
    await (await model()).getByRole('button', { name: 'Cancel' }).click();
    await expect(page.getByRole('dialog')).toBeHidden();

    await chat.sendAndAwaitReply('Reply with the single word: ready', 1);
    await expect(chat.getAssistantMessages().first()).toHaveText(/\S/);
  });
});
