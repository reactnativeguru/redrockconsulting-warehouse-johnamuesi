/**
 * Shared webpack factory for the shell and every remote.
 *
 * Module Federation in one paragraph: each app is built and deployed
 * INDEPENDENTLY. A remote emits `remoteEntry.js` - a small manifest that says
 * "I expose ./App, and I need react@^19 (singleton)". The shell loads that file
 * at runtime and calls `import('catalog/App')` as if it were a local module.
 * Shared libraries (React) are negotiated so only ONE copy is loaded.
 */
const path = require('path');
const webpack = require('webpack');
const HtmlWebpackPlugin = require('html-webpack-plugin');

const { ModuleFederationPlugin } = webpack.container;

module.exports = function createConfig({ dir, name, port, title, exposes = {}, remotes = {} }) {
  const pkg = require(path.join(dir, 'package.json'));

  return (_env, argv) => ({
    entry: './src/index.ts',
    mode: argv.mode ?? 'development',
    devtool: argv.mode === 'production' ? 'source-map' : 'eval-cheap-module-source-map',
    output: {
      path: path.join(dir, 'dist'),
      // 'auto' = work out the URL from where remoteEntry.js was loaded, so the same
      // build works at http://localhost:3001/ or behind nginx at /catalog/.
      publicPath: 'auto',
      uniqueName: name,
      clean: true,
    },
    resolve: { extensions: ['.tsx', '.ts', '.js'] },
    module: {
      rules: [
        {
          test: /\.[jt]sx?$/,
          exclude: /node_modules/,
          use: { loader: 'babel-loader', options: { rootMode: 'upward' } },
        },
      ],
    },
    devServer: {
      port,
      historyApiFallback: true,
      // The shell (another origin) must be allowed to fetch our remoteEntry.js.
      headers: { 'Access-Control-Allow-Origin': '*' },
    },
    plugins: [
      new ModuleFederationPlugin({
        name,
        filename: 'remoteEntry.js',
        exposes,
        remotes,
        shared: {
          // singleton: hooks break if two Reacts are on the page.
          react: { singleton: true, requiredVersion: pkg.dependencies.react },
          'react-dom': { singleton: true, requiredVersion: pkg.dependencies['react-dom'] },
        },
      }),
      new webpack.DefinePlugin({
        'process.env.API_BASE_URL': JSON.stringify(process.env.API_BASE_URL ?? 'http://localhost:5050'),
      }),
      new HtmlWebpackPlugin({ title, templateContent: `<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>${title}</title></head><body style="margin:0;font-family:system-ui,sans-serif;background:#f7f7f5"><div id="root"></div></body></html>` }),
    ],
  });
};
