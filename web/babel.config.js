// Shared Babel config for every micro-frontend (found via rootMode: 'upward').
// Babel strips TypeScript types; `tsc -b` does the actual type checking.
//
// `development` must match the React build in the bundle: the dev JSX transform
// emits jsxDEV(), which production React does not export (blank page,
// "jsxDEV is not a function"). webpack passes envName = its build mode, so
// `npm run build` compiles production JSX even when NODE_ENV isn't set.
module.exports = (api) => ({
  presets: [
    ['@babel/preset-env', { targets: 'defaults' }],
    ['@babel/preset-react', { runtime: 'automatic', development: api.env('development') }],
    '@babel/preset-typescript',
  ],
});
