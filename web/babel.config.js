// Shared Babel config for every micro-frontend (found via rootMode: 'upward').
// Babel strips TypeScript types; `tsc -b` does the actual type checking.
module.exports = {
  presets: [
    ['@babel/preset-env', { targets: 'defaults' }],
    ['@babel/preset-react', { runtime: 'automatic' }],
    '@babel/preset-typescript',
  ],
};
