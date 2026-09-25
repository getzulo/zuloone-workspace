import { readFileSync } from 'node:fs';
const j = JSON.parse(readFileSync('d:/Sources/zuloone-workspace/Sales/Documents/SalesRealization/PrintForms/InvoiceXr.printform.json', 'utf8'));
const layout = JSON.parse(j.object.layoutJson);
for (const b of layout.bands) {
  console.log('\n==', b.kind, b.heightMm);
  for (const c of b.controls ?? []) {
    console.log([c.type, c.id, c.xMm, c.yMm, c.wMm, c.hMm, c.bold ? 'B' : '', c.borders ?? '', c.hAlign ?? '', JSON.stringify(c.text ?? c.binding ?? '')].join('\t'));
  }
}
