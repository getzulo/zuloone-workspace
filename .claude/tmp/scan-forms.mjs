import { readFileSync, readdirSync, statSync } from 'node:fs';
import { join } from 'node:path';

function walk(dir, out = []) {
  for (const name of readdirSync(dir)) {
    const p = join(dir, name);
    if (statSync(p).isDirectory()) walk(p, out);
    else if (name.endsWith('.printform.json')) out.push(p);
  }
  return out;
}

const root = 'd:/Sources/zuloone-workspace';
for (const file of walk(root)) {
  const j = JSON.parse(readFileSync(file, 'utf8'));
  const layout = JSON.parse(j.object.layoutJson);
  const bands = layout.bands ?? [];
  const counts = {};
  let backs = 0;
  let tables = 0;
  for (const b of bands) {
    for (const c of b.controls ?? []) {
      counts[c.type] = (counts[c.type] ?? 0) + 1;
      if (c.back) backs++;
      if (c.type === 'XRTable') {
        tables++;
        for (const row of c.rows ?? []) for (const cell of row.cells ?? []) if (cell.back) backs++;
      }
    }
  }
  const kinds = bands.map((b) => b.kind).join(',');
  console.log(`${j.object.name}\t${kinds}\t${JSON.stringify(counts)}\tbacks ${backs} tables ${tables}`);
}
