import { defineConfig, globalIgnores } from "eslint/config";
import nextVitals from "eslint-config-next/core-web-vitals";
import nextTs from "eslint-config-next/typescript";

const eslintConfig = defineConfig([
  ...nextVitals,
  ...nextTs,
  // Override default ignores of eslint-config-next.
  globalIgnores([
    // Default ignores of eslint-config-next:
    ".next/**",
    "out/**",
    "build/**",
    "next-env.d.ts",
    // The .NET backend isn't JS/TS — and its build output now bundles
    // Playwright's own minified vendor JS (trace viewer assets), which
    // crashes ESLint's formatter on files well past its expected line length.
    "backend/**",
  ]),
]);

export default eslintConfig;
