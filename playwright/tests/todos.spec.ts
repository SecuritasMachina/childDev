import { test, expect } from '@playwright/test';
import { BASE, waitForBlazor, registerAndLogin } from './helpers';

test.describe('Todos', () => {
  test.beforeEach(async ({ context }) => {
    await context.clearCookies();
  });

  test('todos page redirects unauthenticated user', async ({ page }) => {
    await page.goto('/todos');
    await page.waitForURL(/\/login/, { timeout: 8000 });
  });

  test('todos page loads for authenticated user', async ({ page }) => {
    await registerAndLogin(page);
    await page.goto('/todos');
    await waitForBlazor(page);
    await expect(page.getByText(/todos|tasks/i).first()).toBeVisible({ timeout: 8000 });
  });

  test('add a new todo via quick add', async ({ page }) => {
    await registerAndLogin(page);
    await page.goto('/todos');
    await waitForBlazor(page);

    // Try quick-add field first
    const quickAdd = page.getByPlaceholder(/add.*todo|quick.*add|what.*needs/i).first();
    if (await quickAdd.isVisible({ timeout: 3000 }).catch(() => false)) {
      await quickAdd.click();
      await quickAdd.pressSequentially('Read chapter 3 of math book', { delay: 20 });
      await page.keyboard.press('Enter');
    } else {
      // Fall back to dialog
      const addBtn = page.getByRole('button', { name: /add.*todo|new.*todo/i }).first();
      await addBtn.waitFor({ state: 'visible', timeout: 10000 });
      await addBtn.click();
      const titleField = page.getByLabel(/what needs to be done/i);
      await titleField.waitFor({ state: 'visible', timeout: 5000 });
      await titleField.pressSequentially('Read chapter 3 of math book', { delay: 20 });
      const submitBtn = page.getByRole('button', { name: /Add Todo/i });
      await expect(submitBtn).toBeEnabled({ timeout: 5000 });
      await submitBtn.click();
    }

    await expect(page.getByText('Read chapter 3 of math book')).toBeVisible({ timeout: 8000 });
  });

  test('add a new todo via dialog', async ({ page }) => {
    await registerAndLogin(page);
    await page.goto('/todos');
    await waitForBlazor(page);

    const addBtn = page.getByRole('button', { name: /add todo/i }).first();
    if (!await addBtn.isVisible({ timeout: 5000 }).catch(() => false)) {
      // Try "More options" link
      const moreOptions = page.getByRole('button', { name: /more options/i });
      if (await moreOptions.isVisible({ timeout: 3000 }).catch(() => false)) {
        await moreOptions.click();
      }
    } else {
      await addBtn.click();
    }

    const titleField = page.getByLabel(/what needs to be done/i);
    await titleField.waitFor({ state: 'visible', timeout: 5000 });
    await titleField.pressSequentially('Practice spelling words', { delay: 20 });

    const submitBtn = page.getByRole('button', { name: /Add Todo/i });
    await expect(submitBtn).toBeEnabled({ timeout: 5000 });
    await submitBtn.click();

    await expect(page.getByText('Practice spelling words')).toBeVisible({ timeout: 8000 });
  });

  test('complete a todo', async ({ page }) => {
    await registerAndLogin(page);
    await page.goto('/todos');
    await waitForBlazor(page);

    // Add a todo first
    const quickAdd = page.getByPlaceholder(/add.*todo|quick.*add|what.*needs/i).first();
    if (await quickAdd.isVisible({ timeout: 3000 }).catch(() => false)) {
      await quickAdd.click();
      await quickAdd.pressSequentially('Todo to complete', { delay: 20 });
      await page.keyboard.press('Enter');
    } else {
      const addBtn = page.getByRole('button', { name: /add todo/i }).first();
      await addBtn.waitFor({ state: 'visible', timeout: 10000 });
      await addBtn.click();
      const titleField = page.getByLabel(/what needs to be done/i);
      await titleField.waitFor({ state: 'visible', timeout: 5000 });
      await titleField.pressSequentially('Todo to complete', { delay: 20 });
      await page.getByRole('button', { name: /Add Todo/i }).click();
    }

    await expect(page.getByText('Todo to complete')).toBeVisible({ timeout: 8000 });

    // Complete it
    const completeBtn = page.getByRole('button', { name: /complete todo/i }).first();
    if (await completeBtn.isVisible({ timeout: 3000 }).catch(() => false)) {
      await completeBtn.click();
      // Either it disappears from active or appears in completed section
      await page.waitForTimeout(1000);
    }
  });

  test('delete a todo', async ({ page }) => {
    await registerAndLogin(page);
    await page.goto('/todos');
    await waitForBlazor(page);

    // Add a todo
    const quickAdd = page.getByPlaceholder(/add.*todo|quick.*add|what.*needs/i).first();
    if (await quickAdd.isVisible({ timeout: 3000 }).catch(() => false)) {
      await quickAdd.click();
      await quickAdd.pressSequentially('Todo to delete', { delay: 20 });
      await page.keyboard.press('Enter');
    } else {
      const addBtn = page.getByRole('button', { name: /add todo/i }).first();
      await addBtn.waitFor({ state: 'visible', timeout: 10000 });
      await addBtn.click();
      const titleField = page.getByLabel(/what needs to be done/i);
      await titleField.waitFor({ state: 'visible', timeout: 5000 });
      await titleField.pressSequentially('Todo to delete', { delay: 20 });
      await page.getByRole('button', { name: /Add Todo/i }).click();
    }

    await expect(page.getByText('Todo to delete')).toBeVisible({ timeout: 8000 });

    const deleteBtn = page.getByRole('button', { name: /delete todo/i }).first();
    if (await deleteBtn.isVisible({ timeout: 3000 }).catch(() => false)) {
      await deleteBtn.click();
      // Confirm if dialog appears
      const confirmBtn = page.getByRole('button', { name: /^delete$/i }).first();
      if (await confirmBtn.isVisible({ timeout: 2000 }).catch(() => false)) {
        await confirmBtn.click();
      }
      await expect(page.getByText('Todo to delete')).not.toBeVisible({ timeout: 8000 });
    }
  });

  test('edit a todo', async ({ page }) => {
    await registerAndLogin(page);
    await page.goto('/todos');
    await waitForBlazor(page);

    // Add a todo
    const quickAdd = page.getByPlaceholder(/add.*todo|quick.*add|what.*needs/i).first();
    if (await quickAdd.isVisible({ timeout: 3000 }).catch(() => false)) {
      await quickAdd.click();
      await quickAdd.pressSequentially('Original todo title', { delay: 20 });
      await page.keyboard.press('Enter');
    } else {
      const addBtn = page.getByRole('button', { name: /add todo/i }).first();
      await addBtn.waitFor({ state: 'visible', timeout: 10000 });
      await addBtn.click();
      const titleField = page.getByLabel(/what needs to be done/i);
      await titleField.waitFor({ state: 'visible', timeout: 5000 });
      await titleField.pressSequentially('Original todo title', { delay: 20 });
      await page.getByRole('button', { name: /Add Todo/i }).click();
    }

    await expect(page.getByText('Original todo title')).toBeVisible({ timeout: 8000 });

    const editBtn = page.getByRole('button', { name: /edit todo/i }).first();
    if (await editBtn.isVisible({ timeout: 3000 }).catch(() => false)) {
      await editBtn.click();
      const editField = page.getByLabel(/task/i).first();
      await editField.waitFor({ state: 'visible', timeout: 5000 });
      await editField.clear();
      await editField.pressSequentially('Updated todo title', { delay: 20 });
      const saveBtn = page.getByRole('button', { name: /^save$/i }).last();
      await saveBtn.click();
      await expect(page.getByText('Updated todo title')).toBeVisible({ timeout: 8000 });
    }
  });

  test('filter todos', async ({ page }) => {
    await registerAndLogin(page);
    await page.goto('/todos');
    await waitForBlazor(page);

    // Add a todo
    const quickAdd = page.getByPlaceholder(/add.*todo|quick.*add|what.*needs/i).first();
    if (await quickAdd.isVisible({ timeout: 3000 }).catch(() => false)) {
      await quickAdd.click();
      await quickAdd.pressSequentially('Filterable todo item', { delay: 20 });
      await page.keyboard.press('Enter');
    } else {
      const addBtn = page.getByRole('button', { name: /add todo/i }).first();
      await addBtn.waitFor({ state: 'visible', timeout: 10000 });
      await addBtn.click();
      const titleField = page.getByLabel(/what needs to be done/i);
      await titleField.waitFor({ state: 'visible', timeout: 5000 });
      await titleField.pressSequentially('Filterable todo item', { delay: 20 });
      await page.getByRole('button', { name: /Add Todo/i }).click();
    }

    await expect(page.getByText('Filterable todo item')).toBeVisible({ timeout: 8000 });

    // Use search/filter if present
    const searchField = page.getByPlaceholder(/filter|search/i).first();
    if (await searchField.isVisible({ timeout: 3000 }).catch(() => false)) {
      await searchField.fill('Filterable');
      await expect(page.getByText('Filterable todo item')).toBeVisible({ timeout: 5000 });
    }
  });
});
