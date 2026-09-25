import { readFileSync } from 'node:fs';
const h = readFileSync(new URL('./receipt.html', import.meta.url), 'utf8');
const pages = h.split('class="xr-page"').length - 1;
console.log('pages', pages);
const heads = [...h.matchAll(/data-xr-id="([^"]+)"/g)].map(m => m[1]);
console.log('controls', heads.join(','));
