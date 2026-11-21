import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

export default defineConfig({
  plugins: [react()],
  css: {
    transformer: 'lightningcss',
  },
  build: {
    cssMinify: 'lightningcss',
  },
  server: {
    // The docs pages render the Markdown in the repository's docs folder.
    fs: { allow: ['..'] },
  },
});
