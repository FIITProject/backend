import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// В разработке API — http://localhost:5080 (docker compose или dotnet run), в проде nginx проксирует /api на тот же origin
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: { '/api': process.env.API_URL ?? 'http://localhost:5080' },
  },
});
