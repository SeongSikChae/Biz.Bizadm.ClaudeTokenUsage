/// <reference types="vitest/config" />
import path from "node:path";
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: { alias: { "@": path.resolve(__dirname, "./src") } },
  server: {
    // Biz.Bizadm.ClaudeTokenUsage.Server.Spa의 SpaProxyServerUrl과 맞춘다. 점유 시 다른 포트로 뜨지 않게 한다.
    port: 5173,
    strictPort: true,
    proxy: {
      // 서버의 조회 API 경로
      "/api": { target: process.env.SERVER_URL ?? "http://localhost:5000", changeOrigin: true },
    },
  },
  test: { environment: "jsdom", globals: true, setupFiles: "./src/test/setup.ts" },
});
