import { expect, test } from '@playwright/test';

test('home page loads', async ({ page }) => {
  await page.goto('/');
  await expect(page).toHaveTitle(/Home Page - WebApp/);
  await expect(page.getByRole('heading', { name: 'Welcome' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Chat' })).toBeVisible();
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
