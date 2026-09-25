import { readFileSync, readdirSync, statSync, writeFileSync } from 'node:fs';
import { join, relative } from 'node:path';

const root = 'd:/Sources/zuloone-workspace';
function walk(dir, out = []) {
  for (const name of readdirSync(dir)) {
    const p = join(dir, name);
    if (name === 'node_modules' || name === '.git' || name === '.generated' || name === '.claude') continue;
    if (statSync(p).isDirectory()) walk(p, out);
    else if (name.endsWith('.printform.json') || name === 'model.json') out.push(relative(root, p).replaceAll('\\', '/'));
  }
  return out;
}
const files = walk(root).filter((p) =>
  p.endsWith('.printform.json')
  || ['Accounting/model.json','CRM/model.json','HR/model.json','Inventory/model.json','LocalizationSaudiArabia/model.json','Production/model.json','Purchasing/model.json','Tax/model.json','Sales/model.json','LocalizationUkraine/model.json'].includes(p),
);
writeFileSync('d:/Sources/zuloone-workspace/.claude/tmp/apply-forms.json', JSON.stringify({ files, force: true }));
console.log(files.length);
