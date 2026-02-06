import { defineConfig, devices } from '@playwright/test';
import path from 'node:path';

const httpsPort = process.env.WEBAPP_HTTPS_PORT ?? '5173';
const httpPort = process.env.WEBAPP_HTTP_PORT ?? '5172';

const baseURL =
  process.env.PLAYWRIGHT_BASE_URL ?? `https://127.0.0.1:${httpsPort}`;

export default defineConfig({
  testDir: './tests',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: process.env.CI ? 2 : undefined,
  reporter: [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL,
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
    ignoreHTTPSErrors: true
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] }
    }
  ],
  webServer: {
    cwd: path.resolve(__dirname, '..'),
    command: `dotnet run --project WebApp/WebApp.csproj --urls \"https://127.0.0.1:${httpsPort};http://127.0.0.1:${httpPort}\"`,
    url: baseURL,
    ignoreHTTPSErrors: true,
    reuseExistingServer: !process.env.CI,
    timeout: 120_000
  }
});
