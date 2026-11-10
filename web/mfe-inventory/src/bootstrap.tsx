import { createRoot } from 'react-dom/client';
import App from './App';

createRoot(document.getElementById('root')!).render(
  <div style={{ padding: 24 }}>
    <App apiBaseUrl={process.env.API_BASE_URL!} storeId="STORE-001" />
  </div>,
);
