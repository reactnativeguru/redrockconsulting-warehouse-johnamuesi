import { lazy } from 'react';
import { createRoot } from 'react-dom/client';
import { Shell, type RemoteRegistry } from './Shell';

// React.lazy + dynamic import of a FEDERATED module: webpack fetches the remote's
// remoteEntry.js and the App chunk only when the user opens that section.
const remotes: RemoteRegistry = {
  catalog: { label: 'Catalogue', component: lazy(() => import('catalog/App')) },
  inventory: { label: 'Stock', component: lazy(() => import('inventory/App')) },
};

createRoot(document.getElementById('root')!).render(
  <Shell remotes={remotes} apiBaseUrl={process.env.API_BASE_URL!} />,
);
