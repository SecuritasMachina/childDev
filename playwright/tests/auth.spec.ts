import { test, expect, Page } from '@playwright/test';

const BASE = 'http://childdev.homeserver.havranek.com';
const PIN = '1234test';

async function waitForBlazor(page: Page) {
  // Wait for Blazor's interactive circuit to render the component
  // The .blazor-ready class is set in OnAfterRenderAsync(firstRender=true),
  // which only fires after the SignalR circuit is established and the component
  // has done its first interactive render with event handlers attached.
  await page.waitForSelector('.blazor-ready', { timeout: 15000 });
}

async function fillRegisterForm(page: Page, nick: string, pin: string) {
  const nicknameInput = page.getByLabel('Nickname', { exact: true });
  await nicknameInput.click();
  await nicknameInput.pressSequentially(nick, { delay: 20 });

  // Use first() to disambiguate "PIN" from "Confirm PIN"
  const pinInput = page.getByLabel('PIN', { exact: true });
  await pinInput.click();
  await pinInput.pressSequentially(pin, { delay: 20 });

  const confirmPinInput = page.getByLabel('Confirm PIN', { exact: true });
  await confirmPinInput.click();
  await confirmPinInput.pressSequentially(pin, { delay: 20 });
}

async function fillLoginForm(page: Page, nick: string, pin: string) {
  const nicknameInput = page.getByLabel('Nickname', { exact: true });
  await nicknameInput.click();
  await nicknameInput.pressSequentially(nick, { delay: 20 });

  const pinInput = page.getByLabel('PIN', { exact: true });
  await pinInput.click();
  await pinInput.pressSequentially(pin, { delay: 20 });
}

test.describe('Auth flow', () => {
  test.beforeEach(async ({ context }) => {
    await context.clearCookies();
  });

  test('register page loads', async ({ page }) => {
    await page.goto('/register');
    await expect(page).toHaveTitle(/ChildDev/);
    await expect(page.getByText('Start Achieving Goals')).toBeVisible({ timeout: 10000 });
  });

  test('register with valid credentials redirects to home', async ({ page }) => {
    const nick = `testuser_${Date.now()}`;
    await page.goto('/register');
    await waitForBlazor(page);

    await fillRegisterForm(page, nick, PIN);

    const registerBtn = page.getByRole('button', { name: /Create Account/i });
    await expect(registerBtn).toBeEnabled({ timeout: 8000 });
    await registerBtn.click();

    await expect(page).toHaveURL(`${BASE}/`, { timeout: 10000 });
  });

  test('logout redirects to login', async ({ page }) => {
    const nick = `logout_${Date.now()}`;
    await page.goto('/register');
    await waitForBlazor(page);

    await fillRegisterForm(page, nick, PIN);

    const registerBtn = page.getByRole('button', { name: /Create Account/i });
    await expect(registerBtn).toBeEnabled({ timeout: 8000 });
    await registerBtn.click();
    await expect(page).toHaveURL(`${BASE}/`, { timeout: 10000 });

    await page.goto('/logout');
    await expect(page).toHaveURL(/\/login/, { timeout: 10000 });
  });

  test('login with valid credentials redirects to home', async ({ page }) => {
    const nick = `logintest_${Date.now()}`;

    // Register
    await page.goto('/register');
    await waitForBlazor(page);
    await fillRegisterForm(page, nick, PIN);

    let registerBtn = page.getByRole('button', { name: /Create Account/i });
    await expect(registerBtn).toBeEnabled({ timeout: 8000 });
    await registerBtn.click();
    await expect(page).toHaveURL(`${BASE}/`, { timeout: 10000 });

    // Logout
    await page.goto('/logout');
    await expect(page).toHaveURL(/\/login/, { timeout: 10000 });

    // Login
    await page.goto('/login');
    await waitForBlazor(page);
    await fillLoginForm(page, nick, PIN);

    const loginBtn = page.getByRole('button', { name: /^Login$/i });
    await expect(loginBtn).toBeEnabled({ timeout: 8000 });
    await loginBtn.click();

    await expect(page).toHaveURL(`${BASE}/`, { timeout: 10000 });
  });

  test('login with wrong PIN shows error', async ({ page }) => {
    await page.goto('/login');
    await waitForBlazor(page);

    await fillLoginForm(page, 'nonexistentuser_xyz', 'wrongpin');

    const loginBtn = page.getByRole('button', { name: /^Login$/i });
    await expect(loginBtn).toBeEnabled({ timeout: 8000 });
    await loginBtn.click();

    await expect(page.getByText('Invalid nickname or PIN')).toBeVisible({ timeout: 5000 });
  });

  test('register with duplicate nickname shows error', async ({ page }) => {
    const nick = `duptest_${Date.now()}`;

    // First registration
    await page.goto('/register');
    await waitForBlazor(page);
    await fillRegisterForm(page, nick, PIN);

    let registerBtn = page.getByRole('button', { name: /Create Account/i });
    await expect(registerBtn).toBeEnabled({ timeout: 8000 });
    await registerBtn.click();
    await expect(page).toHaveURL(`${BASE}/`, { timeout: 10000 });

    await page.goto('/logout');
    await expect(page).toHaveURL(/\/login/, { timeout: 5000 });

    // Second registration with same nick
    await page.goto('/register');
    await waitForBlazor(page);
    await fillRegisterForm(page, nick, PIN);

    registerBtn = page.getByRole('button', { name: /Create Account/i });
    await expect(registerBtn).toBeEnabled({ timeout: 8000 });
    await registerBtn.click();

    await expect(page.getByText('Nickname already taken')).toBeVisible({ timeout: 5000 });
  });
});
