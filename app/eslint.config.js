// ESLint for the app: typed rules from typescript-eslint, the React Compiler-era hooks rules, jsx-a11y's rules (the
// maintained fork, jsx-a11y-x, as the original stops at ESLint 9), TanStack Query's rules and Storybook's. Prettier
// owns formatting, so its config turns the stylistic rules off. The spike, the design work and the shell are not
// app code and are never linted here.
import js from '@eslint/js';
import pluginQuery from '@tanstack/eslint-plugin-query';
import prettier from 'eslint-config-prettier';
import { defineConfig, globalIgnores } from 'eslint/config';
import jsxA11y from 'eslint-plugin-jsx-a11y-x';
import reactHooks from 'eslint-plugin-react-hooks';
import reactRefresh from 'eslint-plugin-react-refresh';
import storybook from 'eslint-plugin-storybook';
import globals from 'globals';
import tseslint from 'typescript-eslint';

export default defineConfig([
  globalIgnores([
    'dist/**',
    'dist-*/**',
    'storybook-static/**',
    'coverage/**',
    'playwright-report/**',
    'test-results/**',
    'spike/**',
    'design/**',
    'src-tauri/**',
    '.sidecar-build/**',
    '.engine-build/**',
    'node_modules/**',
  ]),
  {
    files: ['**/*.{ts,tsx}'],
    extends: [
      js.configs.recommended,
      tseslint.configs.strictTypeChecked,
      tseslint.configs.stylisticTypeChecked,
      reactHooks.configs.flat['recommended-latest'],
      pluginQuery.configs['flat/recommended'],
    ],
    languageOptions: {
      ecmaVersion: 2023,
      globals: { ...globals.browser },
      parserOptions: { projectService: true, tsconfigRootDir: import.meta.dirname },
    },
    rules: {
      '@typescript-eslint/no-unused-vars': [
        'error',
        { argsIgnorePattern: '^_', varsIgnorePattern: '^_', ignoreRestSiblings: true },
      ],
      '@typescript-eslint/restrict-template-expressions': ['error', { allowNumber: true }],
      '@typescript-eslint/no-confusing-void-expression': ['error', { ignoreArrowShorthand: true }],
      '@typescript-eslint/consistent-type-imports': ['error', { fixStyle: 'inline-type-imports' }],
      // Promises are handled or marked with `void` on purpose: a dropped rejection is a bug.
      '@typescript-eslint/no-floating-promises': 'error',
      '@typescript-eslint/no-misused-promises': ['error', { checksVoidReturn: { attributes: false } }],
      'no-console': ['error', { allow: ['error', 'warn'] }],
    },
  },
  {
    files: ['src/**/*.tsx'],
    extends: [jsxA11y.configs.recommended],
    plugins: { 'react-refresh': reactRefresh },
    rules: {
      // The router's createLink makes a typed link out of a component: the ui/ links (TextLink, NavItem, StatLink).
      'react-refresh/only-export-components': ['error', { allowConstantExport: true, extraHOCs: ['createLink'] }],
      // docs/v2-ui-architecture.md, "Rules every slice follows": crawled text renders as text, never HTML (6), and
      // components use tokens only, never arbitrary values such as w-[37px] or [mask:x] (2).
      'no-restricted-syntax': [
        'error',
        {
          selector: "JSXAttribute[name.name='dangerouslySetInnerHTML']",
          message: 'Render text as text: crawled text is the site’s, and is never HTML here.',
        },
        {
          selector: String.raw`Literal[value=/(^|\s)[\w:!-]*-\[[^\]]*\]/]`,
          message: 'No arbitrary values: use a token, as w-(--sidebar-width) does.',
        },
        {
          selector: String.raw`TemplateElement[value.raw=/(^|\s)[\w:!-]*-\[[^\]]*\]/]`,
          message: 'No arbitrary values: use a token, as w-(--sidebar-width) does.',
        },
        {
          selector: String.raw`Literal[value=/(^|\s)!?\[[a-z-]+:[^\]]+\]/]`,
          message: 'No arbitrary properties: add a token or a class in styles/ instead.',
        },
        {
          selector: String.raw`TemplateElement[value.raw=/(^|\s)!?\[[a-z-]+:[^\]]+\]/]`,
          message: 'No arbitrary properties: add a token or a class in styles/ instead.',
        },
      ],
    },
  },
  {
    // Tests, stories and tooling: node globals, and the looser patterns test code needs.
    files: ['**/*.test.{ts,tsx}', 'src/test/**', 'e2e/**', '**/*.stories.tsx', '.storybook/**', '*.config.ts'],
    languageOptions: { globals: { ...globals.node } },
    rules: {
      '@typescript-eslint/no-non-null-assertion': 'off',
      'react-refresh/only-export-components': 'off',
    },
  },
  {
    files: ['scripts/**/*.mjs', '*.config.js'],
    extends: [js.configs.recommended],
    languageOptions: { ecmaVersion: 2023, sourceType: 'module', globals: { ...globals.node } },
  },
  storybook.configs['flat/recommended'],
  prettier,
]);
