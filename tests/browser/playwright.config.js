import { defineConfig } from '@playwright/test';

export default defineConfig({
    testDir: '.',
    testMatch: '*.spec.js',
    fullyParallel: true,
    workers: 2,
    timeout: 30000,
    use: {
        baseURL: 'http://127.0.0.1:5190',
        browserName: 'chromium',
        channel: process.env.MARMOTTE_BROWSER_CHANNEL || undefined,
        viewport: { width: 1280, height: 960 },
        trace: 'retain-on-failure'
    },
    webServer: {
        command: 'dotnet run --project ../../samples/Marmotte.ChatUi.Sample --configuration Release --no-build --no-launch-profile --urls http://127.0.0.1:5190',
        url: 'http://127.0.0.1:5190',
        env: { ASPNETCORE_ENVIRONMENT: 'Development', Logging__LogLevel__Default: 'Warning' },
        reuseExistingServer: !process.env.CI,
        timeout: 30000
    }
});
