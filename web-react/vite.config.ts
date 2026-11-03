import react from '@vitejs/plugin-react';
import { fileURLToPath } from 'node:url';
import { defineConfig, type Plugin } from 'vitest/config';

// Where the API runs while developing. The dev server forwards /v1 to it, so the page and the API share one origin
// and the refresh cookie (HttpOnly, Secure, Path=/v1/auth) works exactly as it does when the API serves the page.
const apiTarget = process.env.VITE_API_TARGET ?? 'http://localhost:5080';

// The page allows scripts and styles from its own origin only. The development server needs an inline script for hot
// reloading, so the policy is added to the built page only; a test checks it.
const policy = [
  "default-src 'self'",
  "script-src 'self'",
  "style-src 'self'",
  "img-src 'self' data:",
  "connect-src 'self'",
  "object-src 'none'",
  "base-uri 'none'",
  "form-action 'self'",
].join('; ');

function contentSecurityPolicy(): Plugin {
  return {
    name: 'content-security-policy',
    apply: 'build',
    transformIndexHtml: () => [{ tag: 'meta', attrs: { 'http-equiv': 'Content-Security-Policy', content: policy }, injectTo: 'head-prepend' }],
  };
}

export default defineConfig({
  plugins: [react(), contentSecurityPolicy()],
  resolve: { alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) } },
  server: {
    port: 5173,
    strictPort: true,
    proxy: { '/v1': { target: apiTarget } },
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    include: ['src/**/*.test.{ts,tsx}'],
  },
});
