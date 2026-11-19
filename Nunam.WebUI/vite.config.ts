import path from 'node:path';
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';

// The dev server proxies /api to the API so the browser sees one origin, as it will behind the
// production reverse proxy.
export default defineConfig({
    plugins: [react(), tailwindcss()],
    resolve: {
        alias: { '@': path.resolve(__dirname, './src') },
    },
    server: {
        port: 54211,
        proxy: {
            '/api': process.env.NUNAM_API_URL ?? 'http://localhost:5033',
        },
    },
});
