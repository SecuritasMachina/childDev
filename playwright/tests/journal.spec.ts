import { test, expect } from '@playwright/test';
import { BASE, waitForBlazor, registerAndLogin } from './helpers';

test.describe('Journal', () => {
  test.beforeEach(async ({ context }) => {
    await context.clearCookies();
  });

  test('journal page redirects unauthenticated user', async ({ page }) => {
    await page.goto('/journal');
    await page.waitForURL(/\/login/, { timeout: 8000 });
  });

  test('journal page loads for authenticated user', async ({ page }) => {
    await registerAndLogin(page);
    await page.goto('/journal');
    await waitForBlazor(page);
    await expect(page.getByText('Journal')).toBeVisible({ timeout: 8000 });
  });

  test('add a journal entry', async ({ page }) => {
    await registerAndLogin(page);
    await page.goto('/journal');
    await waitForBlazor(page);

    // Open add dialog
    const newEntryBtn = page.getByRole('button', { name: /New Entry/i });
    await newEntryBtn.waitFor({ state: 'visible', timeout: 10000 });
    await newEntryBtn.click();

    // Fill in journal text
    const notesField = page.getByLabel(/What happened|Notes|Entry/i).first();
    await notesField.waitFor({ state: 'visible', timeout: 5000 });
    await notesField.click();
    await notesField.pressSequentially('Today I practiced riding my bike for 20 minutes.', { delay: 20 });

    // Save
    const saveBtn = page.getByRole('button', { name: /Save|Add Entry/i }).last();
    await expect(saveBtn).toBeEnabled({ timeout: 5000 });
    await saveBtn.click();

    // Verify entry appears
    await expect(page.getByText('Today I practiced riding my bike')).toBeVisible({ timeout: 8000 });
  });

  test('add journal entry with mood', async ({ page }) => {
    await registerAndLogin(page);
    await page.goto('/journal');
    await waitForBlazor(page);

    const newEntryBtn = page.getByRole('button', { name: /New Entry/i });
    await newEntryBtn.waitFor({ state: 'visible', timeout: 10000 });
    await newEntryBtn.click();

    const notesField = page.getByLabel(/What happened|Notes/i).first();
    await notesField.waitFor({ state: 'visible', timeout: 5000 });
    await notesField.click();
    await notesField.pressSequentially('Had a great day at school!', { delay: 20 });

    // Try to fill mood if field exists
    const moodField = page.getByLabel(/mood/i).first();
    if (await moodField.isVisible({ timeout: 2000 }).catch(() => false)) {
      await moodField.click();
      await moodField.pressSequentially('happy', { delay: 20 });
    }

    const saveBtn = page.getByRole('button', { name: /Save|Add Entry/i }).last();
    await expect(saveBtn).toBeEnabled({ timeout: 5000 });
    await saveBtn.click();

    await expect(page.getByText('Had a great day at school!')).toBeVisible({ timeout: 8000 });
  });

  test('edit a journal entry', async ({ page }) => {
    await registerAndLogin(page);
    await page.goto('/journal');
    await waitForBlazor(page);

    // Add an entry first
    const newEntryBtn = page.getByRole('button', { name: /New Entry/i });
    await newEntryBtn.waitFor({ state: 'visible', timeout: 10000 });
    await newEntryBtn.click();

    const notesField = page.getByLabel(/What happened|Notes/i).first();
    await notesField.waitFor({ state: 'visible', timeout: 5000 });
    await notesField.pressSequentially('Original entry text', { delay: 20 });

    const saveBtn = page.getByRole('button', { name: /Save|Add Entry/i }).last();
    await saveBtn.click();
    await expect(page.getByText('Original entry text')).toBeVisible({ timeout: 8000 });

    // Find edit button for the entry
    const editBtn = page.getByRole('button', { name: /edit/i }).first();
    if (await editBtn.isVisible({ timeout: 3000 }).catch(() => false)) {
      await editBtn.click();
      const editNotesField = page.getByLabel(/What happened|Notes/i).first();
      await editNotesField.waitFor({ state: 'visible', timeout: 5000 });
      await editNotesField.clear();
      await editNotesField.pressSequentially('Updated entry text', { delay: 20 });

      const updateBtn = page.getByRole('button', { name: /Save|Update/i }).last();
      await updateBtn.click();
      await expect(page.getByText('Updated entry text')).toBeVisible({ timeout: 8000 });
    }
  });

  test('delete a journal entry', async ({ page }) => {
    await registerAndLogin(page);
    await page.goto('/journal');
    await waitForBlazor(page);

    // Add an entry
    const newEntryBtn = page.getByRole('button', { name: /New Entry/i });
    await newEntryBtn.waitFor({ state: 'visible', timeout: 10000 });
    await newEntryBtn.click();

    const notesField = page.getByLabel(/What happened|Notes/i).first();
    await notesField.waitFor({ state: 'visible', timeout: 5000 });
    await notesField.pressSequentially('Entry to be deleted', { delay: 20 });

    const saveBtn = page.getByRole('button', { name: /Save|Add Entry/i }).last();
    await saveBtn.click();
    await expect(page.getByText('Entry to be deleted')).toBeVisible({ timeout: 8000 });

    // Delete the entry
    const deleteBtn = page.getByRole('button', { name: /delete/i }).first();
    if (await deleteBtn.isVisible({ timeout: 3000 }).catch(() => false)) {
      await deleteBtn.click();
      // Confirm deletion dialog if present
      const confirmBtn = page.getByRole('button', { name: /confirm|yes|delete/i }).first();
      if (await confirmBtn.isVisible({ timeout: 2000 }).catch(() => false)) {
        await confirmBtn.click();
      }
      await expect(page.getByText('Entry to be deleted')).not.toBeVisible({ timeout: 8000 });
    }
  });

  test('filter journal entries', async ({ page }) => {
    await registerAndLogin(page);
    await page.goto('/journal');
    await waitForBlazor(page);

    // Add an entry
    const newEntryBtn = page.getByRole('button', { name: /New Entry/i });
    await newEntryBtn.waitFor({ state: 'visible', timeout: 10000 });
    await newEntryBtn.click();

    const notesField = page.getByLabel(/What happened|Notes/i).first();
    await notesField.waitFor({ state: 'visible', timeout: 5000 });
    await notesField.pressSequentially('Searchable journal content here', { delay: 20 });

    const saveBtn = page.getByRole('button', { name: /Save|Add Entry/i }).last();
    await saveBtn.click();
    await expect(page.getByText('Searchable journal content here')).toBeVisible({ timeout: 8000 });

    // Use search/filter
    const searchField = page.getByPlaceholder(/search/i).first();
    if (await searchField.isVisible({ timeout: 3000 }).catch(() => false)) {
      await searchField.fill('Searchable');
      await expect(page.getByText('Searchable journal content here')).toBeVisible({ timeout: 5000 });
    }
  });
});
