import { createServer } from 'node:http';
import { mkdir } from 'node:fs/promises';
import { chromium, expect as baseExpect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

// Invoked by the isolated Aspire test. Never points at a developer's identity or database.
const origin = process.env.MARMOTTE_TEST_WEB_URL;
const password = process.env.MARMOTTE_TEST_PASSWORD;
const phase = process.env.MARMOTTE_TEST_PHASE;
if (!origin || !password || !phase) throw new Error('Missing isolated browser-test configuration.');
const expect = baseExpect.configure({ timeout: 15000 });
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
const requests = [];
const server = createServer(async (req, res) => {
    let body = '';
    for await (const part of req) body += part;
    if (req.url === '/v1/models') {
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({ object: 'list', data: [{ id: 'protected-model', object: 'model', owned_by: 'test', created: 0 }] }));
        return;
    }
    const data = JSON.parse(body);
    const prompt = data.messages.at(-1).content;
    requests.push({ prompt, stream: data.stream });
    const status = req.headers.authorization !== 'Bearer test-only-model-key' ? 401
        : prompt === 'context failure' ? 400 : prompt === 'server failure' ? 503 : prompt === 'missing model' ? 404 : 200;
    if (status !== 200) {
        res.writeHead(status, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({ error: { message: 'private-provider-detail', code: prompt === 'context failure' ? 'context_length_exceeded' : 'test_error' } }));
        return;
    }
    const text = 'Controlled response';
    if (!data.stream) {
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({ id: 'test', object: 'chat.completion', model: data.model, created: 0,
            choices: [{ index: 0, message: { role: 'assistant', content: text }, finish_reason: 'stop' }] }));
        return;
    }
    res.writeHead(200, { 'Content-Type': 'text/event-stream' });
    const chunk = (content, finish = null) => res.write(`data: ${JSON.stringify({ id: 'test', object: 'chat.completion.chunk',
        model: data.model, created: 0, choices: [{ index: 0, delta: { content }, finish_reason: finish }] })}\n\n`);
    chunk('Controlled ');
    if (prompt === 'drop connection') { await delay(350); res.destroy(); return; }
    if (prompt === 'cancel response') {
        for (let index = 0; index < 100 && !res.destroyed; index++) { await delay(100); chunk('partial '); }
    } else { await delay(350); chunk('response'); }
    if (!res.destroyed) { chunk('', 'stop'); res.end('data: [DONE]\n\n'); }
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
const endpoint = `http://127.0.0.1:${server.address().port}/v1`;
const browser = await chromium.launch({ channel: process.env.MARMOTTE_BROWSER_CHANNEL || undefined });
const errors = [];
const contexts = [];
let page;
const resultDir = new URL('./test-results/', import.meta.url).pathname;
await mkdir(resultDir, { recursive: true });

async function signIn(username) {
    const context = await browser.newContext({ viewport: { width: 1280, height: 1100 } });
    contexts.push(context);
    const tab = await context.newPage();
    tab.on('pageerror', () => errors.push('Browser script error'));
    await tab.goto(`${origin}/account/login`);
    await tab.locator('#username').fill(username);
    await tab.locator('#password').fill(password);
    await tab.locator('#kc-login').click();
    await expect(tab.getByRole('button', { name: 'New conversation', exact: true })).toBeEnabled();
    return tab;
}
async function settings(tab) {
    const details = tab.locator('details');
    if (!await details.getAttribute('open')) {
        // An open details element has an empty-string attribute.
        if (!await details.evaluate(element => element.open)) await details.locator('summary').click();
    }
    return details;
}
async function saveProfile(tab, name, api, model, key, fresh = true) {
    const panel = await settings(tab);
    if (fresh) {
        await panel.getByRole('button', { name: 'New connection', exact: true }).click();
        await expect(panel.getByRole('group', { name: 'New connection profile', exact: true })).toBeVisible();
        await expect(panel.getByLabel('Profile name', { exact: true })).toHaveValue('');
    }
    await panel.getByLabel('Profile name', { exact: true }).fill(name);
    await panel.getByLabel('API base URL', { exact: true }).fill(api);
    await panel.getByLabel('Model', { exact: true }).fill(model);
    if (key) {
        await panel.getByRole('combobox', { name: 'Model-server authentication', exact: true }).selectOption('replace');
        await panel.getByLabel('New API key', { exact: true }).fill(key);
    }
    await panel.getByRole('button', { name: 'Save connection', exact: true }).click();
    await expect(panel.getByRole('status')).toHaveText('Connection saved. You can now test it or send a message.');
    await expect(tab.getByRole('combobox', { name: 'Connection profile', exact: true }).locator('option:checked')).toContainText(name);
}
async function createConversation(tab, title) {
    await tab.getByLabel('Conversation title', { exact: true }).fill(title);
    await tab.getByRole('button', { name: 'New conversation', exact: true }).click();
    await expect(tab.getByRole('heading', { name: title, exact: true })).toBeVisible();
}
async function send(tab, text) {
    await tab.getByLabel('Message', { exact: true }).fill(text);
    await tab.getByRole('button', { name: 'Send message', exact: true }).click();
}
async function completed(tab) {
    await expect(tab.locator('.mcui-generation [role="status"]')).toHaveText('Response complete.', { timeout: 90000 });
}
async function chooseTheme(tab, theme) {
    await tab.getByRole('combobox', { name: 'Theme', exact: true }).selectOption(theme);
    await expect(tab.locator('.workspace-page > .mcui-theme')).toHaveAttribute('data-theme', theme.toLowerCase());
}
async function probeStreaming(tab) {
    const panel = await settings(tab);
    await panel.getByRole('button', { name: 'Test streaming', exact: true }).click();
    await expect(panel.getByRole('status')).toHaveText('Streaming verified. Stream response is enabled for this connection.', { timeout: 90000 });
    await expect(tab.getByLabel('Stream response', { exact: true })).toBeChecked();
}

try {
    page = await signIn('m4-alice');
    if (phase === 'initial') {
        await saveProfile(page, 'M8 Controlled', endpoint, 'protected-model', 'test-only-model-key');
        const panel = await settings(page);
        await panel.getByRole('button', { name: 'Discover models', exact: true }).click();
        await expect(panel.getByRole('status')).toContainText('Found 1 models');
        await panel.getByRole('button', { name: 'Test text connection', exact: true }).click();
        await expect(panel.getByRole('status')).toHaveText('Text connection verified.');
        await createConversation(page, 'M8 Alice history');
        await send(page, 'First question');
        await completed(page);
        await probeStreaming(page);
        await send(page, 'Second question');
        await completed(page);
        await expect(page.locator('.mcui-message[data-role="assistant"]')).toHaveCount(2);
        await send(page, 'cancel response');
        await expect(page.locator('.mcui-message[data-role="assistant"]').last()).toContainText('Controlled');
        await page.getByRole('button', { name: 'Stop response', exact: true }).click();
        await expect(page.locator('.mcui-generation [role="status"]')).toContainText('Response stopped.');
        await expect(page.locator('.mcui-message[data-role="assistant"]').last()).toContainText('Controlled');
        for (const [prompt, feedback] of [['context failure', 'context or output limit'], ['server failure', 'unavailable or timed out'],
            ['missing model', 'Check the API key, model name'], ['drop connection', 'response was interrupted']]) {
            await send(page, prompt);
            await expect(page.locator('.mcui-generation [role="alert"]')).toContainText(feedback);
            await expect(page.locator('body')).not.toContainText('private-provider-detail');
        }
        await send(page, 'After recovery');
        await completed(page);
        await chooseTheme(page, 'Dark');
        await page.reload();
        await expect(page.getByRole('button', { name: 'M8 Alice history', exact: true })).toBeEnabled();
        await page.getByRole('button', { name: 'M8 Alice history', exact: true }).click();
        await expect(page.locator('.mcui-message[data-role="assistant"]')).toHaveCount(8);
        await expect(page.getByRole('combobox', { name: 'Theme', exact: true })).toHaveValue('Dark');
        // Create enough history to exercise both directions of owner-scoped paging.
        for (let index = 0; index < 20; index++) await createConversation(page, `M8 page ${index}`);
        await page.getByRole('button', { name: 'Next conversations', exact: true }).click();
        await expect(page.getByRole('button', { name: 'M8 page 19', exact: true })).toBeVisible();
        await page.getByRole('button', { name: 'Previous conversations', exact: true }).click();
        await page.getByRole('button', { name: 'M8 Alice history', exact: true }).click();
        await page.getByRole('button', { name: 'Reload conversation', exact: true }).click();
        await expect(page.locator('.mcui-message[data-role="assistant"]')).toHaveCount(8);

        const bob = await signIn('m4-bob');
        await expect(bob.getByRole('button', { name: 'M8 Alice history', exact: true })).toHaveCount(0);
        await expect(bob.getByRole('combobox', { name: 'Connection profile', exact: true }).locator('option')).toHaveCount(1);
        await saveProfile(bob, 'M8 Bob model', endpoint, 'protected-model', null);
        const bobSettings = await settings(bob);
        await bobSettings.getByRole('button', { name: 'Test text connection', exact: true }).click();
        await expect(bobSettings.getByRole('alert')).toContainText('Check the API key, model name');
        await saveProfile(bob, 'M8 Bob model', endpoint, 'protected-model', 'test-only-model-key', false);
        await probeStreaming(bob);
        await createConversation(bob, 'M8 Bob history');
        await send(bob, 'Bob question');
        await completed(bob);
        await bob.getByRole('button', { name: 'Delete conversation', exact: true }).click();
        await bob.getByRole('button', { name: 'Confirm deletion', exact: true }).click();
        await expect(bob.getByRole('button', { name: 'M8 Bob history', exact: true })).toHaveCount(0);
        await bobSettings.getByRole('button', { name: 'Delete connection', exact: true }).click();
        await bobSettings.getByRole('button', { name: 'Confirm connection deletion', exact: true }).click();
        await expect(bobSettings.getByRole('status')).toHaveText('Connection and saved key deleted.');
        await expect(bob.getByRole('combobox', { name: 'Connection profile', exact: true })).toHaveValue('');
    } else {
        // Full AppHost restart retains history, preferences and the protected model credential.
        await expect(page.getByRole('combobox', { name: 'Theme', exact: true })).toHaveValue('Dark');
        const select = page.getByRole('combobox', { name: 'Connection profile', exact: true });
        const value = await select.locator('option').filter({ hasText: 'M8 Controlled' }).getAttribute('value');
        await select.selectOption(value);
        await page.getByRole('button', { name: 'M8 Alice history', exact: true }).click();
        await expect(page.locator('.mcui-message[data-role="assistant"]')).toHaveCount(8);
        await saveProfile(page, 'M8 Controlled', endpoint, 'protected-model', null, false);
        await probeStreaming(page);
        await send(page, 'After restart');
        await completed(page);
    }

    // Real-model flows use the same two identities and protected persistence as the controlled scenarios.
    const liveServers = [
        ['LM Studio', process.env.MARMOTTE_MODEL_ENDPOINT, process.env.MARMOTTE_MODEL_ID, process.env.MARMOTTE_MODEL_KEY],
        ['Unsloth', process.env.MARMOTTE_UNSLOTH_ENDPOINT, process.env.MARMOTTE_UNSLOTH_MODEL, process.env.MARMOTTE_UNSLOTH_KEY]
    ].filter(([, api, model]) => api && model);
    const liveBob = liveServers.length ? await signIn('m4-bob') : null;
    for (const [name, api, model, key] of liveServers) {
        for (const [tab, owner] of [[page, 'Alice'], [liveBob, 'Bob']]) {
            const title = `M8 ${owner} ${name} history`;
            if (phase === 'initial') {
                await saveProfile(tab, `M8 ${name}`, api, model, key);
                const panel = await settings(tab);
                await panel.getByRole('button', { name: 'Test text connection', exact: true }).click();
                await expect(panel.getByRole('status')).toHaveText('Text connection verified.', { timeout: 90000 });
                await createConversation(tab, title);
            } else {
                const select = tab.getByRole('combobox', { name: 'Connection profile', exact: true });
                await select.selectOption(await select.locator('option').filter({ hasText: `M8 ${name}` }).getAttribute('value'));
                if (!await tab.getByRole('button', { name: title, exact: true }).count()) {
                    await tab.getByRole('button', { name: 'Next conversations', exact: true }).click();
                }
                await tab.getByRole('button', { name: title, exact: true }).click();
                await expect(tab.locator('.mcui-message[data-role="assistant"]')).toHaveCount(2);
            }
            await probeStreaming(tab);
            await send(tab, phase === 'initial' ? 'Reply briefly: what is a marmot?' : 'Reply briefly: name its habitat.');
            await completed(tab);
            if (phase === 'initial') {
                await send(tab, 'Count from 1 to 1000. Output only the numbers, one number per line.');
                await expect(tab.locator('.mcui-generation [role="status"]')).toHaveText('Response in progress…', { timeout: 90000 });
                await tab.getByRole('button', { name: 'Stop response', exact: true }).click();
                await expect(tab.locator('.mcui-generation [role="status"]')).toContainText('Response stopped.');
                await tab.getByRole('button', { name: 'Reload conversation', exact: true }).click();
                await expect(tab.locator('.mcui-message[data-role="assistant"]')).toHaveCount(2);
            }
        }
    }

    await page.locator('details').evaluate(element => { element.open = false; });
    for (const theme of ['Light', 'Dark', 'System']) {
        await chooseTheme(page, theme);
        const accessibility = await new AxeBuilder({ page }).include('.workspace').withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze();
        expect(accessibility.violations).toEqual([]);
    }
    await chooseTheme(page, 'Dark');
    await page.screenshot({ path: `${resultDir}workspace-${phase}-desktop.png`, fullPage: true });
    await page.setViewportSize({ width: 360, height: 900 });
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: `${resultDir}workspace-${phase}-mobile.png`, fullPage: true });
    expect(errors).toEqual([]);
    console.log(`Workspace browser ${phase}: passed (two-user isolation, settings, chat, recovery, themes and responsive layout).`);
} catch (error) {
    if (page) await page.screenshot({ path: `${resultDir}workspace-${phase}-failure.png`, fullPage: true }).catch(() => {});
    throw error;
} finally {
    await Promise.all(contexts.map(context => context.close()));
    await browser.close();
    server.closeAllConnections();
    await new Promise(resolve => server.close(resolve));
}
