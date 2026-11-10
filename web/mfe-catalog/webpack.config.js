const createConfig = require('../webpack.common');

// REMOTE: exposes its App for the shell to load at runtime.
module.exports = createConfig({
  dir: __dirname,
  name: 'catalog',
  port: 3001,
  title: 'Catalog (standalone)',
  exposes: { './App': './src/App' },
});
