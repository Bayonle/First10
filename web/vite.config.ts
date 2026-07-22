import tailwindcss from '@tailwindcss/vite';
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

// Under Aspire, the API address arrives as services__api__http__0 and the port as PORT.
// Standalone (`npm run dev` with `dotnet run` alongside), the fallbacks apply.
const apiTarget = process.env.services__api__http__0 ?? 'http://localhost:5170';

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: process.env.PORT ? Number(process.env.PORT) : 5173,
    proxy: {
      '/api': { target: apiTarget, changeOrigin: true },
      '/hubs': { target: apiTarget, changeOrigin: true, ws: true },
    },
  },
});
