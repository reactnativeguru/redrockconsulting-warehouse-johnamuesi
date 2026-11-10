// TypeScript can't see inside a remote at build time, so we declare the contract.
// The props type comes from the shared contracts package - if a remote's App
// stops accepting RemoteAppProps, that's a breaking change for every host.
declare module 'catalog/App' {
  import type { ComponentType } from 'react';
  import type { RemoteAppProps } from '@basecamp/mfe-contracts';
  const App: ComponentType<RemoteAppProps>;
  export default App;
}

declare module 'inventory/App' {
  import type { ComponentType } from 'react';
  import type { RemoteAppProps } from '@basecamp/mfe-contracts';
  const App: ComponentType<RemoteAppProps>;
  export default App;
}
