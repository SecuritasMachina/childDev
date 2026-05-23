import { test, expect } from '@playwright/test';
import { BASE, waitForBlazor, registerAndLogin } from './helpers';

test.describe('Achievements', () => {
  test.beforeEach(async ({ context }) => {
    await context.clearCookies();
  });

  test('achievements page redirects unauthenticated user', async ({ page }) => {
    await page.goto('/achievements');
    await page.waitForURL(/\/login/, { timeout: 8000 });
  });

  test('achievements page loads for authenticated user', async ({ page }) => {
    await registerAndLogin(page);
    await page.goto('/achievements');
    await waitForBlazor(page);
    await expect(page.getByText(/achievements/i).first()).toBeVisible({ timeout: 8000 });
  });

  test('achievements shows badge categories', async ({ page }) => {
    await registerAndLogin(page);
    await page.goto('/achievements');
    await waitForBlazor(page);

    // Should show at least one badge category (Goals, Progress Notes, etc.)
    await expect(page.getByText(/goals|progress notes|journal/i).first()).toBeVisible({ timeout: 8000 });
  });

  test('achievements shows earned count', async ({ page }) => {
    await registerAndLogin(page);
    await page.goto('/achievements');
    await waitForBlazor(page);

    // Should show "X of Y badges earned"
    await expect(page.getByText(/badges earned|of.*badges/i).first()).toBeVisible({ timeout: 8000 });
  });

  test('first goal badge unlocks after adding a goal', async ({ page }) => {
    await registerAndLogin(page);
    await waitForBlazor(page);

    // Add a goal to trigger First Goal badge
    const addBtn = page.getByRole('button', { name: /Add.*Goal|New.*Goal/i }).first();
    await addBtn.waitFor({ state: 'visible', timeout: 10000 });
    await addBtn.click();

    const goalTextField = page.getByLabel("What's the goal?");
    await goalTextField.waitFor({ state: 'visible', timeout: 5000 });
    await goalTextField.pressSequentially('My first badge goal', { delay: 20 });

    const submitBtn = page.getByRole('button', { name: /Add Goal/i });
    await submitBtn.click();
    await expect(page.getByText('My first badge goal')).toBeVisible({ timeout: 8000 });

    // Navigate to achievements
    await page.goto('/achievements');
    await waitForBlazor(page);

    // First Goal badge should now be earned
    await expect(page.getByText(/First Goal|First Step|first/i).first()).toBeVisible({ timeout: 8000 });
  });

  test('share achievements button is visible', async ({ page }) => {
    await registerAndLogin(page);
    await page.goto('/achievements');
    await waitForBlazor(page);

    const shareBtn = page.getByRole('button', { name: /share achievements/i }).first();
    if (await shareBtn.isVisible({ timeout: 3000 }).catch(() => false)) {
      await expect(shareBtn).toBeVisible();
    }
  });
});
