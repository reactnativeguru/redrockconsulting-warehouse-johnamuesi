import { createRoot } from 'react-dom/client';
import App from './App';

// Standalone mode: the team can develop/test this micro-frontend on its own at
// http://localhost:3001 without running the shell.
createRoot(document.getElementById('root')!).render(
  <div style={{ padding: 24 }}>
    <App apiBaseUrl={process.env.API_BASE_URL!} storeId="STORE-001" />
  </div>,
);
