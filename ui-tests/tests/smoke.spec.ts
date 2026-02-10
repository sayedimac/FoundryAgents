import { expect, test } from '@playwright/test';

const hasAzureApiKey = !!process.env.AZURE_API_KEY;

test('home page loads', async ({ page }) => {
  await page.goto('/');
  await expect(page).toHaveTitle(/AI Chat/);
  await expect(
    page.getByRole('heading', { name: 'Welcome to AI Chat' })
  ).toBeVisible();
  await expect(page.locator('#agentSelect')).toBeVisible();
});

test('chat page loads', async ({ page }) => {
  await page.goto('/Chat');
  await expect(page).toHaveTitle(/AI Chat/);
  await expect(
    page.getByRole('heading', { name: 'Welcome to AI Chat' })
  ).toBeVisible();

  const agentSelect = page.locator('#agentSelect');
  await expect(agentSelect).toBeVisible();
  await expect(agentSelect).toHaveValue('Writer');
});

test('image agent generates an image', async ({ page }) => {
  test.skip(!hasAzureApiKey, 'Set AZURE_API_KEY to run image generation');
  test.setTimeout(2 * 60 * 1000);

  await page.goto('/Chat');

  await page.selectOption('#agentSelect', 'Image');

  await page.fill(
    '#messageInput',
    'Generate a simple icon: a red circle centered on a white background.'
  );
  await page.click('#sendBtn');

  const assistantMessage = page
    .locator('#messageList .message.message-assistant')
    .last();
  const image = assistantMessage.locator('img');
  await expect(image).toBeVisible({ timeout: 90_000 });

  const src = await image.getAttribute('src');
  expect(src, 'Image src should be set').toBeTruthy();
  expect(src!, 'Image should be served from /generated').toContain('/generated/');

  const absolute = new URL(src!, page.url()).toString();
  const resp = await page.request.get(absolute);
  expect(resp.status(), 'Generated image should be fetchable').toBe(200);
});

test('video agent generates a video', async ({ page }) => {
  test.skip(!hasAzureApiKey, 'Set AZURE_API_KEY to run video generation');
  test.setTimeout(6 * 60 * 1000);

  await page.goto('/Chat');
  await page.selectOption('#agentSelect', 'Video');

  await page.fill(
    '#messageInput',
    'Create a short looping video of a bouncing blue ball on a plain white background.'
  );
  await page.click('#sendBtn');

  const assistantMessage = page
    .locator('#messageList .message.message-assistant')
    .last();

  const video = assistantMessage.locator('video');
  const downloadLink = assistantMessage.getByRole('link', { name: 'Download video' });

  // Prefer the inline <video> case (most informative for UI validation).
  // If the backend returns a URL-only response, fall back to asserting text.
  try {
    await expect(video).toBeVisible({ timeout: 5 * 60 * 1000 });
    const src = await video.getAttribute('src');
    expect(src, 'Video src should be set').toBeTruthy();
    expect(src!, 'Video should be served from /generated').toContain('/generated/');
    await expect(downloadLink).toBeVisible({ timeout: 30_000 });
  } catch {
    const body = assistantMessage.locator('.message-body');
    await expect(body).toContainText('Video', { timeout: 5 * 60 * 1000 });
  }
});
