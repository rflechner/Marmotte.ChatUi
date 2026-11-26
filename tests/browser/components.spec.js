import { test, expect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

const widget = page => page.getByRole('region', { name: 'Your thinking space', exact: true });
const composer = page => widget(page).getByRole('textbox', { name: 'Message', exact: true });
const send = page => widget(page).getByRole('button', { name: 'Send message', exact: true });
const status = page => widget(page).getByRole('status');

async function open(page) {
    await page.goto('/');
    await expect(composer(page)).toBeEnabled();
    await expect(page.getByRole('img', { name: 'Turquoise marmot with a cream muzzle and red scarf' })).toHaveJSProperty('naturalWidth', 1254);
}

async function accessibility(page) {
    const result = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze();
    expect(result.violations).toEqual([]);
}

test('keyboard submission, safe content, streamed completion and stable focus', async ({ page }) => {
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await open(page);
    await expect(send(page)).toBeDisabled();
    await composer(page).fill('<script>window.unsafe = true</script>');
    await composer(page).press('Enter');
    await expect(composer(page)).toHaveValue('<script>window.unsafe = true</script>\n');
    await composer(page).press('Tab');
    await expect(send(page)).toBeFocused();
    await expect(send(page)).toHaveCSS('outline-style', 'solid');
    await page.keyboard.press('Enter');
    await expect(status(page)).toContainText('Sending');
    await expect(status(page)).toContainText('progress');
    await expect(composer(page)).toBeFocused();
    await expect(composer(page)).toHaveValue('');
    await expect(status(page)).toContainText('complete', { timeout: 15000 });
    await expect(widget(page).locator('.mcui-message')).toHaveCount(2);
    expect(await page.evaluate(() => window.unsafe)).toBeUndefined();
    await accessibility(page);
    expect(errors).toEqual([]);
});

test('stop retains the partial response and allows another draft', async ({ page }) => {
    await open(page);
    await composer(page).fill('Please start');
    await send(page).click();
    await expect(status(page)).toContainText('progress');
    await expect(widget(page).locator('[data-role="assistant"] .mcui-message-content')).not.toBeEmpty();
    await widget(page).getByRole('button', { name: 'Stop response' }).click();
    await expect(status(page)).toContainText('stopped');
    await expect(widget(page).locator('[data-role="assistant"] .mcui-message-content')).not.toBeEmpty();
    await composer(page).fill('Another thought');
    await expect(send(page)).toBeEnabled();
});

test('failed acceptance retains draft and stream failure retains partial text', async ({ page }) => {
    await open(page);
    await page.getByLabel('Response scenario').selectOption('send-error');
    await composer(page).fill('Retain this');
    await send(page).click();
    await expect(widget(page).getByRole('alert')).toContainText('Storage is unavailable');
    await expect(composer(page)).toHaveValue('Retain this');
    await expect(widget(page).locator('.mcui-message')).toHaveCount(0);
    await page.getByLabel('Response scenario').selectOption('partial-error');
    await send(page).click();
    await expect(status(page)).toContainText('failed');
    await expect(widget(page).locator('[data-role="assistant"] .mcui-message-content')).not.toBeEmpty();
    await expect(widget(page).getByRole('alert').filter({ hasText: 'could not be generated' })).toBeVisible();
    await accessibility(page);
});

test('every pending attachment blocks sending until explicitly removed', async ({ page }) => {
    await open(page);
    await composer(page).fill('Text with attachment');
    for (const state of ['uploaded', 'preparing', 'ready', 'failed']) {
        await page.getByRole('button', { name: `Add ${state}`, exact: true }).click();
        await expect(send(page)).toBeDisabled();
        await expect(widget(page).getByText('This text composer cannot send attachments.', { exact: false })).toBeVisible();
        await widget(page).getByRole('button', { name: `Remove field-notes-${state}.png` }).click();
        await expect(send(page)).toBeEnabled();
    }
});

test('system theme follows OS changes while instance and inherited tokens remain independent', async ({ page }) => {
    await page.emulateMedia({ colorScheme: 'dark' });
    await open(page);
    await expect(widget(page)).toHaveCSS('background-color', 'rgb(23, 39, 37)');
    const second = page.getByRole('region', { name: 'Field notes', exact: true });
    await expect(second).toHaveCSS('background-color', 'rgb(255, 255, 255)');
    await expect(second.getByRole('button', { name: 'Send message' })).toHaveCSS('background-color', 'rgb(100, 64, 160)');
    await page.emulateMedia({ colorScheme: 'light' });
    await expect(widget(page)).toHaveCSS('background-color', 'rgb(255, 255, 255)');
    await widget(page).getByRole('combobox', { name: 'Theme', exact: true }).selectOption('Dark');
    await expect(widget(page)).toHaveCSS('background-color', 'rgb(23, 39, 37)');
    await page.evaluate(() => document.body.style.setProperty('--mcui-accent', '#884411'));
    await expect(send(page)).toHaveCSS('background-color', 'rgb(136, 68, 17)');
    await expect(second.getByRole('button', { name: 'Send message' })).toHaveCSS('background-color', 'rgb(100, 64, 160)');
});

for (const theme of ['Light', 'Dark', 'System']) {
    for (const width of [360, 1280]) {
        test(`${theme} theme at ${width}px has no horizontal overflow or accessibility violations`, async ({ page }, testInfo) => {
            await page.setViewportSize({ width, height: 960 });
            await page.emulateMedia({ colorScheme: 'dark' });
            await open(page);
            await widget(page).getByRole('combobox', { name: 'Theme', exact: true }).selectOption(theme);
            await composer(page).fill('A keyboard-accessible draft');
            await page.getByRole('button', { name: 'Add preparing', exact: true }).click();
            expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
            await accessibility(page);
            await page.screenshot({ path: testInfo.outputPath(`${theme.toLowerCase()}-${width}.png`), fullPage: true });
        });
    }
}
