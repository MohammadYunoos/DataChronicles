import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// Dev server proxies API + SignalR calls to the .NET backend on :5279,
// so the frontend can call relative paths (/api, /progressHub).
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': { target: 'http://localhost:5279', changeOrigin: true },
      '/progressHub': { target: 'http://localhost:5279', ws: true, changeOrigin: true },
    },
  },
});
