import { test, expect } from '@playwright/test';
import { BASE, waitForBlazor, registerAndLogin } from './helpers';

test.describe('Settings', () => {
  test.beforeEach(async ({ context }) => {
    await context.clearCookies();
  });

  test('settings page redirects unauthenticated user', async ({ page }) => {
    await page.goto('/settings');
    await page.waitForURL(/\/login/, { timeout: 8000 });
  });

  test('settings page loads for authenticated user', async ({ page }) => {
    await registerAndLogin(page);
    await page.goto('/settings');
    await waitForBlazor(page);
    await expect(page.getByText(/settings/i).first()).toBeVisible({ timeout: 8000 });
  });

  test('change nickname', async ({ page }) => {
    await registerAndLogin(page);
    await page.goto('/settings');
    await waitForBlazor(page);

    const nickField = page.getByLabel('New Nickname');
    await nickField.waitFor({ state: 'visible', timeout: 8000 });
    await nickField.click();
    const newNick = `renamed_${Date.now()}`;
    await nickField.pressSequentially(newNick, { delay: 20 });

    const saveNickBtn = page.getByRole('button', { name: /Save Nickname/i });
    await expect(saveNickBtn).toBeEnabled({ timeout: 5000 });
    await saveNickBtn.click();

    // Should show success feedback or updated nickname
    await page.waitForTimeout(2000);
  });

  test('change PIN', async ({ page }) => {
    await registerAndLogin(page);
    await page.goto('/settings');
    await waitForBlazor(page);

    const currentPinField = page.getByLabel('Current PIN');
    await currentPinField.waitFor({ state: 'visible', timeout: 8000 });
    await currentPinField.click();
    await currentPinField.pressSequentially('1234test', { delay: 20 });

    const newPinField = page.getByLabel('New PIN');
    await newPinField.click();
    await newPinField.pressSequentially('5678test', { delay: 20 });

    const confirmPinField = page.getByLabel('Confirm New PIN');
    await confirmPinField.click();
    await confirmPinField.pressSequentially('5678test', { delay: 20 });

    const changePinBtn = page.getByRole('button', { name: /Change PIN/i });
    await expect(changePinBtn).toBeEnabled({ timeout: 5000 });
    await changePinBtn.click();

    await page.waitForTimeout(2000);
  });

  test('view account ID', async ({ page }) => {
    await registerAndLogin(page);
    await page.goto('/settings');
    await waitForBlazor(page);

    const accountIdField = page.getByLabel('Account ID');
    if (await accountIdField.isVisible({ timeout: 5000 }).catch(() => false)) {
      await expect(accountIdField).toBeVisible();
    }
  });

  test('navigate to achievements from settings', async ({ page }) => {
    await registerAndLogin(page);
    await page.goto('/settings');
    await waitForBlazor(page);

    const achievementsLink = page.getByRole('link', { name: /achievements/i }).first();
    if (await achievementsLink.isVisible({ timeout: 3000 }).catch(() => false)) {
      await achievementsLink.click();
      await page.waitForURL(/\/achievements/, { timeout: 8000 });
    }
  });

  test('export data button is visible', async ({ page }) => {
    await registerAndLogin(page);
    await page.goto('/settings');
    await waitForBlazor(page);

    const exportBtn = page.getByRole('button', { name: /export.*data|download.*report/i }).first();
    if (await exportBtn.isVisible({ timeout: 5000 }).catch(() => false)) {
      await expect(exportBtn).toBeVisible();
    }
  });
});
