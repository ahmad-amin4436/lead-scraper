import type { NextConfig } from 'next';

const nextConfig: NextConfig = {
  // ExcelJS and Cheerio rely on Node built-ins; keep them out of the server bundle
  // so they are resolved with native `require` at runtime.
  serverExternalPackages: ['exceljs', 'cheerio'],
  experimental: {
    optimizePackageImports: ['lucide-react'],
  },
  async headers() {
    return [
      {
        source: '/:path*',
        headers: [
          { key: 'X-Content-Type-Options', value: 'nosniff' },
          { key: 'X-Frame-Options', value: 'DENY' },
          { key: 'Referrer-Policy', value: 'strict-origin-when-cross-origin' },
        ],
      },
    ];
  },
};

export default nextConfig;
