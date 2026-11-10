/** Shared Jest config: jsdom environment + jest-dom matchers + Babel transform. */
module.exports = {
  testEnvironment: 'jsdom',
  transform: { '\\.[jt]sx?$': ['babel-jest', { rootMode: 'upward' }] },
  setupFilesAfterEnv: ['@testing-library/jest-dom'],
};
