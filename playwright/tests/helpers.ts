import { Page } from '@playwright/test';

export const BASE = 'http://childdev.homeserver.havranek.com';
export const PIN = '1234test';

export async function waitForBlazor(page: Page) {
  await page.waitForSelector('.blazor-ready', { timeout: 15000 });
}

export async function fillRegisterForm(page: Page, nick: string, pin: string) {
  const nicknameInput = page.getByLabel('Nickname', { exact: true });
  await nicknameInput.click();
  await nicknameInput.pressSequentially(nick, { delay: 20 });

  const pinInput = page.getByLabel('PIN', { exact: true });
  await pinInput.click();
  await pinInput.pressSequentially(pin, { delay: 20 });

  const confirmPinInput = page.getByLabel('Confirm PIN', { exact: true });
  await confirmPinInput.click();
  await confirmPinInput.pressSequentially(pin, { delay: 20 });
}

export async function registerAndLogin(page: Page): Promise<string> {
  const nick = `testuser_${Date.now()}`;
  await page.goto('/register');
  await waitForBlazor(page);
  await fillRegisterForm(page, nick, PIN);

  const registerBtn = page.getByRole('button', { name: /Create Account/i });
  await registerBtn.waitFor({ state: 'visible' });
  await registerBtn.click();
  await page.waitForURL(`${BASE}/`, { timeout: 10000 });
  return nick;
}
