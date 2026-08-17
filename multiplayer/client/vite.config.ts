import { defineConfig, loadEnv } from 'vite';
import vue from '@vitejs/plugin-vue';

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, '.', '');
  const serverOrigin = env.VITE_SERVER_ORIGIN || 'http://localhost:5000';

  return {
    plugins: [vue()],
    server: {
      proxy: {
        '/api': { target: serverOrigin, changeOrigin: true },
        '/hubs': { target: serverOrigin, changeOrigin: true, ws: true },
      },
    },
  };
});
