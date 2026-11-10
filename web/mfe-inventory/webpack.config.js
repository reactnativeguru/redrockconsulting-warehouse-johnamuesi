const createConfig = require('../webpack.common');

module.exports = createConfig({
  dir: __dirname,
  name: 'inventory',
  port: 3002,
  title: 'Inventory (standalone)',
  exposes: { './App': './src/App' },
});
