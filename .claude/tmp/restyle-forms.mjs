import { readFileSync, writeFileSync, readdirSync, statSync } from 'node:fs';
import { join } from 'node:path';

const skip = new Set(['ProFormaJuffaliXr', 'UaFiscalReceiptSample']);
const header = '#d4e2f0';
const total = '#c5d7ea';
const line = '#6b7c8a';

function walk(dir, out = []) {
  for (const name of readdirSync(dir)) {
    const p = join(dir, name);
    if (name === 'node_modules' || name === '.git' || name === '.generated') continue;
    if (statSync(p).isDirectory()) walk(p, out);
    else if (name.endsWith('.printform.json')) out.push(p);
  }
  return out;
}

function frame(c, fill) {
  c.borders = 'LRTB';
  c.borderColor = line;
  c.borderWidthMm = 0.2;
  if (fill) c.back = fill;
}

let n = 0;
for (const file of walk('d:/Sources/zuloone-workspace')) {
  const j = JSON.parse(readFileSync(file, 'utf8'));
  if (skip.has(j.object?.name)) continue;
  const layout = JSON.parse(j.object.layoutJson);
  for (const band of layout.bands ?? []) {
    for (const c of band.controls ?? []) {
      if (c.type !== 'XRLabel') continue;
      if (band.kind === 'PageHeader') frame(c, header);
      else if (band.kind === 'Detail') frame(c);
      else if (band.kind === 'ReportHeader') {
        if ((c.wMm ?? 0) >= 100) continue;
        frame(c, c.bold ? header : undefined);
      } else if (band.kind === 'ReportFooter') {
        if (c.bold) frame(c, total);
        else if ((c.wMm ?? 0) < 80) frame(c);
      }
    }
  }
  j.object.layoutJson = JSON.stringify(layout);
  writeFileSync(file, JSON.stringify(j, null, 2) + '\n');
  n++;
  console.log(j.object.name);
}
console.log('forms', n);
