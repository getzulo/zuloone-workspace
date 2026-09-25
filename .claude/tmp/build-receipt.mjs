import { mkdirSync, writeFileSync } from 'node:fs';

const modelId = '1d269769-c682-4655-be04-4e00fd39eb08';
const formId = '6e8a1c40-2b57-4f91-9d33-7c5e0a1b4d82';
const scriptId = '9f3c2d71-5e84-4a06-b158-2d7f6c8e0a19';
const docType = '34a1af4c-aeaf-48d1-8626-9a0a13b2d5c3';
const W = 72;
const font = 'Courier New';

function svgData(svg) {
  return 'data:image/svg+xml;base64,' + Buffer.from(svg).toString('base64');
}

const checkbox = svgData(`<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 160 28">
  <rect x="1" y="3" width="20" height="20" fill="none" stroke="#111" stroke-width="2"/>
  <path d="M5 14 l4 4 l8-9" fill="none" stroke="#111" stroke-width="2.2"/>
  <text x="28" y="21" font-family="Arial" font-weight="700" font-size="18" fill="#111">checkbox</text>
</svg>`);

let n = 0;
const nid = (p) => `${p}${++n}`;

function cell(weight, text, extra = {}) {
  return {
    id: nid('c'), weight, text,
    borders: '-', wordWrap: true, multiline: true,
    ...extra,
  };
}

function row(heightMm, cells) {
  return { id: nid('r'), heightMm, cells };
}

function label(name, y, h, text, extra = {}) {
  return {
    id: name, type: 'XRLabel', xMm: 0, yMm: y, wMm: W, hMm: h,
    text, visible: true, fontFamily: font, fontSizePt: 8,
    hAlign: 'Center', wordWrap: true, ...extra,
  };
}

function itemTable(y, qty, code, name, amount, nameHead) {
  const rows = [
    row(5, [cell(72, qty, { hAlign: 'Left' })]),
    row(5, [cell(72, code, { hAlign: 'Left' })]),
  ];
  if (nameHead) rows.push(row(5, [cell(72, nameHead, { hAlign: 'Left' })]));
  rows.push(row(5, [
    cell(50, name, { hAlign: 'Left' }),
    cell(22, amount, { hAlign: 'Right' }),
  ]));
  const h = rows.reduce((sum, r) => sum + r.heightMm, 0) + 1;
  return {
    id: nid('it'), type: 'XRTable', xMm: 0, yMm: y, wMm: W, hMm: h,
    fontFamily: font, fontSizePt: 8, visible: true, rows,
  };
}

const items = [
  ['15.000 x 47.00', 'Код УКТЗЕД: 3814000000', 'Хімтрейд / (1 / 15)', '705.00', 'Сольвент / 0,9л / 0,56кг /'],
  ['3.000 x 219.00', 'Код УКТЗЕД: 3814000000', 'Сольвент / 4л / 2,91кг', '657.00'],
  ['5.000 x 30.00', 'Код УКТЗЕД: 3214109090', 'Шпаклівка SP-11 біла 0.35 кг', '150.00'],
  ['2.000 x 32.00', 'Код УКТЗЕД: 3214109090', 'Шпаклівка SP-11 горіх 0.35 кг', '64.00'],
  ['3.000 x 32.00', 'Код УКТЗЕД: 3214109090', 'Шпаклівка SP-11 дуб 0.35 кг', '96.00'],
];

const controls = [
  label('seller', 0, 8, 'ФОП МАКУШОВ ВІТАЛІЙ ВІКТОРОВИЧ', { bold: true, fontSizePt: 8 }),
  label('outlet', 8, 4, 'КВАРТИРА Торгова точка', { bold: true }),
  label('addr', 12, 16, 'Одеська область, м. Одеса,\nСуворовський район, М. ОДЕСА\nСУВОРОВСЬКИЙ Р-Н ПР-Т\nДОБРОВОЛЬСЬКОГО, БУД. 139, КВ.\n178'),
  label('taxid', 28, 5, 'ІД 2985508616', { bold: true }),
];

let y = 36;
for (const [qty, code, name, amount, nameHead] of items) {
  const table = itemTable(y, qty, code, name, amount, nameHead);
  controls.push(table);
  y += table.hMm + 1.2;
}

controls.push({
  id: 'cash', type: 'XRTable', xMm: 0, yMm: y, wMm: W, hMm: 6,
  fontFamily: font, fontSizePt: 8, visible: true,
  rows: [row(6, [
    cell(40, 'Готівка', { hAlign: 'Left' }),
    cell(32, '1672.00 ГРН', { hAlign: 'Right' }),
  ])],
});
y += 7;
controls.push({
  id: 'sum', type: 'XRTable', xMm: 0, yMm: y, wMm: W, hMm: 8,
  fontFamily: font, fontSizePt: 12, visible: true,
  rows: [row(8, [
    cell(28, 'СУМА', { bold: true, hAlign: 'Left' }),
    cell(44, '1672.00 ГРН', { bold: true, hAlign: 'Right' }),
  ])],
});
y += 10;
controls.push(label('chk', y, 4, 'Чек № 1_X00Sv_nTs', { hAlign: 'Center', fontSizePt: 8 }));
y += 4;
controls.push(label('when', y, 4, '25.03.2026      11-26-11', { hAlign: 'Center' }));
y += 6;
controls.push({
  id: 'qr', type: 'XRBarCode', xMm: 22, yMm: y, wMm: 28, hMm: 28, visible: true,
  text: '1_X00Sv_nTs',
  barCode: { symbology: 'QRCode', showText: false },
});
y += 30;
controls.push(label('online', y, 4, 'ОНЛАЙН', { bold: true }));
y += 5;
controls.push({
  id: 'fn', type: 'XRTable', xMm: 0, yMm: y, wMm: W, hMm: 5,
  fontFamily: font, fontSizePt: 8, visible: true,
  rows: [row(5, [
    cell(36, 'ФН ПРРО', { hAlign: 'Left' }),
    cell(36, '4001019411', { hAlign: 'Right' }),
  ])],
});
y += 6;
controls.push(label('fiscal', y, 5, 'ФІСКАЛЬНИЙ ЧЕК', { bold: true, fontSizePt: 10 }));
y += 6;
controls.push({
  id: 'logo', type: 'XRPictureBox', xMm: 16, yMm: y, wMm: 40, hMm: 8,
  visible: true, sourceUrl: checkbox, sizing: 'Zoom',
});
y += 10;

const layout = {
  schemaVersion: 1,
  paper: { kind: 'Custom', orientation: 'Portrait', widthMm: 80, heightMm: Math.ceil(y + 16) },
  margins: { left: 4, top: 3, right: 4, bottom: 3 },
  guides: { x: [], y: [] },
  fontFamily: font,
  bands: [
    { id: 'tm', kind: 'TopMargin', heightMm: 3, controls: [] },
    { id: 'rh', kind: 'ReportHeader', heightMm: y + 2, controls },
    { id: 'det', kind: 'Detail', heightMm: 1, controls: [] },
    { id: 'bm', kind: 'BottomMargin', heightMm: 3, controls: [] },
  ],
};

const form = {
  kind: 'PrintForm',
  object: {
    caption: {
      en: 'Fiscal receipt sample (80 mm)',
      ru: 'Фискальный чек, образец 80 мм',
      uk: 'Фіскальний чек, зразок 80 мм',
    },
    documentTypeMetaId: docType,
    engine: 'Xr',
    scriptMetaId: scriptId,
    layoutJson: JSON.stringify(layout),
    displayOrder: 20,
    metaId: formId,
    name: 'UaFiscalReceiptSample',
    modelId,
  },
};

const root = 'd:/Sources/zuloone-workspace/LocalizationUkraine/Documents/SalesRealization/PrintForms';
mkdirSync(root, { recursive: true });
writeFileSync(`${root}/UaFiscalReceiptSample.printform.json`, JSON.stringify(form, null, 2));
console.log('headerMm', y + 2, 'bytes', form.object.layoutJson.length);
