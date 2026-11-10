const createConfig = require('../webpack.common');

// Where each remote's remoteEntry.js lives. Env vars let the SAME shell code point at
// localhost in dev, /catalog in the nginx container, or a CDN in production.
const CATALOG_URL = process.env.CATALOG_REMOTE_URL ?? 'http://localhost:3001';
const INVENTORY_URL = process.env.INVENTORY_REMOTE_URL ?? 'http://localhost:3002';

// HOST: consumes remotes. 'catalog/App' in code -> catalog@<url>/remoteEntry.js
module.exports = createConfig({
  dir: __dirname,
  name: 'shell',
  port: 3000,
  title: 'Basecamp Store Admin',
  remotes: {
    catalog: `catalog@${CATALOG_URL}/remoteEntry.js`,
    inventory: `inventory@${INVENTORY_URL}/remoteEntry.js`,
  },
});
