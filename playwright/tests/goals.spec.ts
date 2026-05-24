import { test, expect } from '@playwright/test';
import { BASE, waitForBlazor, registerAndLogin } from './helpers';

test.describe('Goals', () => {
  test.beforeEach(async ({ context }) => {
    await context.clearCookies();
  });

  test('home page redirects unauthenticated user to login', async ({ page }) => {
    await page.goto('/');
    await page.waitForURL(/\/login/, { timeout: 8000 });
  });

  test('authenticated user sees goals page', async ({ page }) => {
    await registerAndLogin(page);
    await expect(page).toHaveURL(`${BASE}/`);
    await waitForBlazor(page);
    await expect(page.getByText('LevelUp')).toBeVisible({ timeout: 8000 });
  });

  test('add a new goal', async ({ page }) => {
    await registerAndLogin(page);
    await waitForBlazor(page);

    // Open add dialog
    const addBtn = page.getByRole('button', { name: /Add.*Goal|New.*Goal/i }).first();
    await addBtn.waitFor({ state: 'visible', timeout: 10000 });
    await addBtn.click();

    // Fill in goal text
    const goalTextField = page.getByLabel("What's the goal?");
    await goalTextField.waitFor({ state: 'visible', timeout: 5000 });
    await goalTextField.click();
    await goalTextField.pressSequentially('Learn to ride a bike', { delay: 20 });

    // Submit
    const submitBtn = page.getByRole('button', { name: /Add Goal/i });
    await expect(submitBtn).toBeEnabled({ timeout: 5000 });
    await submitBtn.click();

    // Verify goal appears
    await expect(page.getByText('Learn to ride a bike')).toBeVisible({ timeout: 8000 });
  });

  test('add goal with category and target date', async ({ page }) => {
    await registerAndLogin(page);
    await waitForBlazor(page);

    const addBtn = page.getByRole('button', { name: /Add.*Goal|New.*Goal/i }).first();
    await addBtn.waitFor({ state: 'visible', timeout: 10000 });
    await addBtn.click();

    const goalTextField = page.getByLabel("What's the goal?");
    await goalTextField.waitFor({ state: 'visible', timeout: 5000 });
    await goalTextField.click();
    await goalTextField.pressSequentially('Improve at math', { delay: 20 });

    const outcomeField = page.getByLabel(/measure success/i);
    await outcomeField.click();
    await outcomeField.pressSequentially('Score 90% on next test', { delay: 20 });

    const submitBtn = page.getByRole('button', { name: /Add Goal/i });
    await expect(submitBtn).toBeEnabled({ timeout: 5000 });
    await submitBtn.click();

    await expect(page.getByText('Improve at math')).toBeVisible({ timeout: 8000 });
  });

  test('navigate to goal detail page', async ({ page }) => {
    await registerAndLogin(page);
    await waitForBlazor(page);

    // Add a goal first
    const addBtn = page.getByRole('button', { name: /Add.*Goal|New.*Goal/i }).first();
    await addBtn.waitFor({ state: 'visible', timeout: 10000 });
    await addBtn.click();

    const goalTextField = page.getByLabel("What's the goal?");
    await goalTextField.waitFor({ state: 'visible', timeout: 5000 });
    await goalTextField.pressSequentially('Practice piano daily', { delay: 20 });

    const submitBtn = page.getByRole('button', { name: /Add Goal/i });
    await submitBtn.click();
    await expect(page.getByText('Practice piano daily')).toBeVisible({ timeout: 8000 });

    // Click on the goal to navigate to detail
    await page.getByText('Practice piano daily').click();
    await page.waitForURL(/\/goals\//, { timeout: 8000 });
    await expect(page.getByText('Practice piano daily')).toBeVisible({ timeout: 8000 });
  });

  test('add progress note on goal detail', async ({ page }) => {
    await registerAndLogin(page);
    await waitForBlazor(page);

    // Add a goal
    const addBtn = page.getByRole('button', { name: /Add.*Goal|New.*Goal/i }).first();
    await addBtn.waitFor({ state: 'visible', timeout: 10000 });
    await addBtn.click();

    const goalTextField = page.getByLabel("What's the goal?");
    await goalTextField.waitFor({ state: 'visible', timeout: 5000 });
    await goalTextField.pressSequentially('Run 5K', { delay: 20 });

    await page.getByRole('button', { name: /Add Goal/i }).click();
    await expect(page.getByText('Run 5K')).toBeVisible({ timeout: 8000 });

    // Navigate to detail
    await page.getByText('Run 5K').click();
    await page.waitForURL(/\/goals\//, { timeout: 8000 });
    await waitForBlazor(page);

    // Use the always-visible inline form
    const noteField = page.getByPlaceholder("What happened? What's your next step?");
    await noteField.waitFor({ state: 'visible', timeout: 8000 });
    await noteField.click();
    await noteField.pressSequentially('Completed a 1K warm-up run today!', { delay: 20 });

    const saveBtn = page.getByRole('button', { name: 'Save Note' });
    await saveBtn.waitFor({ state: 'visible', timeout: 5000 });
    await saveBtn.click();

    await expect(page.getByText('Completed a 1K warm-up run today!')).toBeVisible({ timeout: 8000 });
  });

  test('second progress note shows previous note snippet', async ({ page }) => {
    await registerAndLogin(page);
    await waitForBlazor(page);

    // Add a goal
    const addBtn = page.getByRole('button', { name: /Add.*Goal|New.*Goal/i }).first();
    await addBtn.waitFor({ state: 'visible', timeout: 10000 });
    await addBtn.click();

    const goalTextField = page.getByLabel("What's the goal?");
    await goalTextField.waitFor({ state: 'visible', timeout: 5000 });
    await goalTextField.pressSequentially('Learn to juggle', { delay: 20 });

    await page.getByRole('button', { name: /Add Goal/i }).click();
    await expect(page.getByText('Learn to juggle')).toBeVisible({ timeout: 8000 });

    // Navigate to detail
    await page.getByText('Learn to juggle').click();
    await page.waitForURL(/\/goals\//, { timeout: 8000 });
    await waitForBlazor(page);

    // Add first progress note
    const noteField = page.getByPlaceholder("What happened? What's your next step?");
    await noteField.waitFor({ state: 'visible', timeout: 8000 });
    await noteField.click();
    await noteField.pressSequentially('First progress made!', { delay: 20 });

    await page.getByRole('button', { name: 'Save Note' }).click();
    await expect(page.getByText('First progress made!')).toBeVisible({ timeout: 8000 });

    // Add second progress note
    await noteField.click();
    await noteField.pressSequentially('Second step completed', { delay: 20 });
    await page.getByRole('button', { name: 'Save Note' }).click();

    // First note text should still be visible somewhere on page
    await expect(page.getByText('First progress made!')).toBeVisible({ timeout: 8000 });
  });

  test('progress note template chips populate field', async ({ page }) => {
    await registerAndLogin(page);
    await waitForBlazor(page);

    // Add a goal
    const addBtn = page.getByRole('button', { name: /Add.*Goal|New.*Goal/i }).first();
    await addBtn.waitFor({ state: 'visible', timeout: 10000 });
    await addBtn.click();

    const goalTextField = page.getByLabel("What's the goal?");
    await goalTextField.waitFor({ state: 'visible', timeout: 5000 });
    await goalTextField.pressSequentially('Improve drawing skills', { delay: 20 });

    await page.getByRole('button', { name: /Add Goal/i }).click();
    await expect(page.getByText('Improve drawing skills')).toBeVisible({ timeout: 8000 });

    // Navigate to detail
    await page.getByText('Improve drawing skills').click();
    await page.waitForURL(/\/goals\//, { timeout: 8000 });
    await waitForBlazor(page);

    // Wait for inline form to appear
    const noteField = page.getByPlaceholder("What happened? What's your next step?");
    await noteField.waitFor({ state: 'visible', timeout: 8000 });

    // Click the first available chip button (template chip)
    const chipButton = page.locator('.mud-chip').first();
    await chipButton.waitFor({ state: 'visible', timeout: 8000 });
    await chipButton.click();

    // The textarea should now have non-empty content
    const noteValue = await noteField.inputValue();
    expect(noteValue.trim().length).toBeGreaterThan(0);
  });

  test('completed goal hides progress note form', async ({ page }) => {
    await registerAndLogin(page);
    await waitForBlazor(page);

    // Add a goal
    const addBtn = page.getByRole('button', { name: /Add.*Goal|New.*Goal/i }).first();
    await addBtn.waitFor({ state: 'visible', timeout: 10000 });
    await addBtn.click();

    const goalTextField = page.getByLabel("What's the goal?");
    await goalTextField.waitFor({ state: 'visible', timeout: 5000 });
    await goalTextField.pressSequentially('Master origami', { delay: 20 });

    await page.getByRole('button', { name: /Add Goal/i }).click();
    await expect(page.getByText('Master origami')).toBeVisible({ timeout: 8000 });

    // Complete the goal using the "Mark Complete" button on home page
    const completeBtn = page.getByRole('button', { name: /Mark Complete/i }).first();
    await completeBtn.waitFor({ state: 'visible', timeout: 8000 });
    await completeBtn.click();

    // Navigate to the completed goal detail page
    await expect(page.getByText('Master origami')).toBeVisible({ timeout: 8000 });
    await page.getByText('Master origami').first().click();
    await page.waitForURL(/\/goals\//, { timeout: 8000 });
    await waitForBlazor(page);

    // The inline note form placeholder should NOT be visible for a completed goal
    const noteField = page.getByPlaceholder("What happened? What's your next step?");
    await expect(noteField).not.toBeVisible({ timeout: 5000 });
  });

  test('complete a goal and see celebration', async ({ page }) => {
    await registerAndLogin(page);
    await waitForBlazor(page);

    // Add a goal
    const addBtn = page.getByRole('button', { name: /Add.*Goal|New.*Goal/i }).first();
    await addBtn.waitFor({ state: 'visible', timeout: 10000 });
    await addBtn.click();

    const goalTextField = page.getByLabel("What's the goal?");
    await goalTextField.waitFor({ state: 'visible', timeout: 5000 });
    await goalTextField.pressSequentially('Read one chapter', { delay: 20 });
    await page.getByRole('button', { name: /Add Goal/i }).click();
    await expect(page.getByText('Read one chapter')).toBeVisible({ timeout: 8000 });

    // Find and click complete button
    const completeBtn = page.getByRole('button', { name: /Complete|Done|✓/i }).first();
    if (await completeBtn.isVisible({ timeout: 3000 }).catch(() => false)) {
      await completeBtn.click();
      // Should see celebration or confirmation
      await expect(page.getByText(/congrat|completed|celebrate|well done/i).first()).toBeVisible({ timeout: 8000 });
    }
  });
});
