/**
 * ESLint 설정 — TypeScript 소스 품질 게이트.
 * lib(빌드 산출물)·테스트 러너 설정 파일은 제외한다.
 */
module.exports = {
  root: true,
  env: {
    node: true,
    es2021: true
  },
  parser: "@typescript-eslint/parser",
  parserOptions: {
    ecmaVersion: 2021,
    sourceType: "module"
  },
  plugins: ["@typescript-eslint"],
  extends: [
    "eslint:recommended",
    "plugin:@typescript-eslint/recommended"
  ],
  ignorePatterns: ["lib/**", "node_modules/**", "*.cjs", "jest.config.cjs"],
  rules: {
    "@typescript-eslint/no-explicit-any": "error",
    "@typescript-eslint/explicit-function-return-type": "off",
    "no-console": "off",
    eqeqeq: ["error", "always"]
  }
};
