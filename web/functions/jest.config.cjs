/**
 * Jest 설정 — ts-jest로 순수 로직 단위 테스트를 실행한다.
 * 테스트는 Admin SDK/네트워크에 의존하지 않는 순수 모듈(domain/*)만 대상으로 한다.
 */
module.exports = {
  preset: "ts-jest",
  testEnvironment: "node",
  roots: ["<rootDir>/src"],
  testMatch: ["**/__tests__/**/*.test.ts"],
  transform: {
    "^.+\\.ts$": [
      "ts-jest",
      {
        tsconfig: "<rootDir>/tsconfig.test.json"
      }
    ]
  },
  clearMocks: true
};
