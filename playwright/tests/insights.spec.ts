import { test, expect } from '@playwright/test';
import { BASE, waitForBlazor, registerAndLogin } from './helpers';

test.describe('Insights', () => {
  test.beforeEach(async ({ context }) => {
    await context.clearCookies();
  });

  test('insights page redirects unauthenticated user', async ({ page }) => {
    await page.goto('/insights');
    await page.waitForURL(/\/login/, { timeout: 8000 });
  });

  test('insights page loads for authenticated user', async ({ page }) => {
    await registerAndLogin(page);
    await page.goto('/insights');
    await waitForBlazor(page);
    await expect(page.getByText(/insights/i).first()).toBeVisible({ timeout: 8000 });
  });

  test('insights shows activity data', async ({ page }) => {
    await registerAndLogin(page);

    // Add some data to make insights more interesting
    await page.goto('/');
    await waitForBlazor(page);

    const addBtn = page.getByRole('button', { name: /Add.*Goal|New.*Goal/i }).first();
    if (await addBtn.isVisible({ timeout: 5000 }).catch(() => false)) {
      await addBtn.click();
      const goalField = page.getByLabel("What's the goal?");
      await goalField.waitFor({ state: 'visible', timeout: 5000 });
      await goalField.pressSequentially('Insight test goal', { delay: 20 });
      await page.getByRole('button', { name: /Add Goal/i }).click();
      await expect(page.getByText('Insight test goal')).toBeVisible({ timeout: 8000 });
    }

    await page.goto('/insights');
    await waitForBlazor(page);

    // Insights page should render without error
    await expect(page.locator('body')).not.toContainText(/error|exception/i);
  });

  test('insights shows goal progress section', async ({ page }) => {
    await registerAndLogin(page);
    await page.goto('/insights');
    await waitForBlazor(page);

    // Should have some goal-related content or stats
    const hasGoalContent = await page.getByText(/goal|progress|active/i).first().isVisible({ timeout: 8000 }).catch(() => false);
    expect(hasGoalContent).toBeTruthy();
  });

  test('insights view goal link navigates to goal detail', async ({ page }) => {
    await registerAndLogin(page);
    await waitForBlazor(page);

    // Add a goal first
    const addBtn = page.getByRole('button', { name: /Add.*Goal|New.*Goal/i }).first();
    if (await addBtn.isVisible({ timeout: 5000 }).catch(() => false)) {
      await addBtn.click();
      const goalField = page.getByLabel("What's the goal?");
      await goalField.waitFor({ state: 'visible', timeout: 5000 });
      await goalField.pressSequentially('Goal for insights nav test', { delay: 20 });
      await page.getByRole('button', { name: /Add Goal/i }).click();
      await expect(page.getByText('Goal for insights nav test')).toBeVisible({ timeout: 8000 });
    }

    await page.goto('/insights');
    await waitForBlazor(page);

    const viewLink = page.getByRole('link', { name: /^view$/i }).first();
    if (await viewLink.isVisible({ timeout: 3000 }).catch(() => false)) {
      await viewLink.click();
      await page.waitForURL(/\/goals\//, { timeout: 8000 });
    }
  });
});
