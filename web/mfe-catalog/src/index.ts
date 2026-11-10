// The async boundary: Module Federation needs a tick to negotiate shared modules
// (which React to use) BEFORE any app code imports React. Without this dynamic
// import you get "Shared module is not available for eager consumption".
import('./bootstrap');
