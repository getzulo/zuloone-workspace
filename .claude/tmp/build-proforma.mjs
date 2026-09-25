import { writeFileSync } from 'node:fs';

const shade = '#d4e2f0';
const modelId = '47861dd2-1009-4926-9ea3-c506cae5118d';
const formId = '7c4e9a21-6b18-4d50-9f33-1a8e5d2c4b70';
const scriptId = '8d5f0b32-7c29-4e61-a044-2b9f6e3d5c81';
const docType = '34a1af4c-aeaf-48d1-8626-9a0a13b2d5c3';

function svgData(svg) {
  return 'data:image/svg+xml;base64,' + Buffer.from(svg).toString('base64');
}

const jk = svgData(`<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 90 56">
  <text x="0" y="42" font-family="Arial" font-weight="700" font-size="48" fill="#1d4e9f">J</text>
  <text x="24" y="42" font-family="Arial" font-weight="700" font-size="48" fill="#f0a202">K</text>
  <path d="M4 50 C 28 58, 62 52, 88 38" fill="none" stroke="#1d4e9f" stroke-width="3.5" stroke-linecap="round"/>
</svg>`);

const kleemann = svgData(`<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 250 48">
  <g fill="none" stroke="#5c6b78" stroke-width="2.2" stroke-linecap="round">
    <path d="M6 38 A18 18 0 1 1 30 10"/>
    <path d="M12 34 A12 12 0 1 1 28 14"/>
    <path d="M17 30 A7 7 0 1 1 26 18"/>
  </g>
  <text x="40" y="32" font-family="Arial" font-weight="700" font-size="20" fill="#4a5966" letter-spacing="1.4">KLEEMANN</text>
</svg>`);

let n = 0;
const id = (p) => `${p}${++n}`;

function cell(weight, text, extra = {}) {
  return {
    id: id('c'),
    weight,
    text,
    borders: 'LRTB',
    wordWrap: true,
    multiline: true,
    ...extra,
  };
}

function row(heightMm, cells) {
  return { id: id('r'), heightMm, cells };
}

const sellerAddr = [
  'Building Number 8530',
  'Postal Code',
  'Street Name JCAC8530 - Al',
  'Madinah Al',
  'Munawarah Branch Rd',
  'City Jeddah',
  'District Al Andalus Dist.',
  'Country Code SAU',
  '',
  '8530 رقم المبنى',
  'الرمز البريدي',
  'اسم الشارع طريق المدينة المنورة',
  'المدينة جدة',
  'الحي',
  'رمز الدولة المملكة العربية السعودية',
].join('\n');

const buyerAddr = [
  'Building Number',
  'Postal Code',
  'Street Name Qurban District',
  'Madinah',
  'City Medina',
  'District Qurban',
  'Country Code SAU',
  '',
  'رقم المبنى',
  'الرمز البريدي',
  'اسم الشارع المدينة المنورة',
  'المدينة المدينة المنورة',
  'الحي',
  'رمز الدولة المملكة العربية السعودية',
].join('\n');

const sellerName = 'Khaled Ahmad Juffali\nElevators & Escalators\nCo\nشركة خالد أحمد الجفالي للمصاعد\nوالسلالم المحدودة';
const buyerName = 'Foundation of the\nEndowment of the Ribat of\nSayyidna Uthman ibn Affan';

const parties = {
  id: 'parties',
  type: 'XRTable',
  xMm: 0,
  yMm: 40,
  wMm: 190,
  hMm: 98,
  fontSizePt: 7,
  fontFamily: 'Arial',
  visible: true,
  rows: [
    row(7, [
      cell(16, 'Seller', { bold: true, back: shade, borders: 'LTB' }),
      cell(40, '', { back: shade, borders: 'TB' }),
      cell(14, 'المورد', { bold: true, back: shade, hAlign: 'Center' }),
      cell(22, 'Buyer', { bold: true, back: shade, borders: 'LTB' }),
      cell(36, '', { back: shade, borders: 'TB' }),
      cell(16, 'العميل', { bold: true, back: shade, hAlign: 'Right' }),
    ]),
    row(16, [
      cell(16, 'From', { bold: true }),
      cell(40, sellerName),
      cell(14, 'من', { hAlign: 'Center' }),
      cell(22, 'Customer Name', { bold: true }),
      cell(36, buyerName),
      cell(16, 'اسم العميل', { hAlign: 'Right' }),
    ]),
    row(32, [
      cell(16, 'Address', { bold: true }),
      cell(40, sellerAddr),
      cell(14, 'عنوان', { hAlign: 'Center' }),
      cell(22, 'Customer Address', { bold: true }),
      cell(36, buyerAddr),
      cell(16, 'عنوان العميل', { hAlign: 'Right' }),
    ]),
    row(6, [
      cell(16, 'Issue Date', { bold: true }),
      cell(40, '12/08/2026 11:45'),
      cell(14, 'تاريخ الاصدار', { hAlign: 'Center' }),
      cell(22, 'Customer Mobile', { bold: true }),
      cell(36, '0553931851'),
      cell(16, 'رقم جوال العميل', { hAlign: 'Right' }),
    ]),
    row(6, [
      cell(16, 'CR No.', { bold: true }),
      cell(40, '4030280934'),
      cell(14, 'رقم السجل التجاري', { hAlign: 'Center' }),
      cell(22, 'Customer CR No', { bold: true }),
      cell(36, '7033605184'),
      cell(16, 'الرقم السجل للعميل', { hAlign: 'Right' }),
    ]),
    row(6, [
      cell(16, 'VAT No.', { bold: true }),
      cell(40, '301261663400003'),
      cell(14, 'الرقم الضريبي', { hAlign: 'Center' }),
      cell(22, 'Customer VAT No.', { bold: true }),
      cell(36, '311157299900003'),
      cell(16, 'الرقم الضريبي للعميل', { hAlign: 'Right' }),
    ]),
    row(6, [
      cell(16, 'Supply Date', { bold: true }),
      cell(40, ''),
      cell(14, 'تاريخ التوريد', { hAlign: 'Center' }),
      cell(22, 'Project ID', { bold: true }),
      cell(36, 'PR-24-0039'),
      cell(16, 'كود المشروع', { hAlign: 'Right' }),
    ]),
    row(7, [
      cell(16, 'Currency', { bold: true }),
      cell(40, 'SAR'),
      cell(14, 'العملة', { hAlign: 'Center' }),
      cell(22, 'Project Name', { bold: true }),
      cell(36, 'Marina Palace Hotel - Madinah'),
      cell(16, 'اسم المشروع', { hAlign: 'Right' }),
    ]),
  ],
};

const colHead = (en, ar, weight, align) => cell(weight, `${en}\n${ar}`, {
  bold: true, back: shade, hAlign: align || 'Center',
});

const linesHead = {
  id: 'lines-head',
  type: 'XRTable',
  xMm: 0, yMm: 0, wMm: 190, hMm: 10,
  fontSizePt: 6.5, fontFamily: 'Arial', visible: true,
  rows: [row(10, [
    colHead('Item Description', 'وصف الصنف', 70, 'Center'),
    colHead('Qty', 'الكمية', 16),
    colHead('Unit price', 'سعر قبل الضريبة', 22),
    colHead('Discount', '', 16),
    colHead('Taxable Amount', 'المبلغ الخاضع للضريبة', 24),
    colHead('VAT Amount', 'قيمة الضريبة', 20),
    colHead('Total Price\n(SAR)', 'الإجمالي', 22),
  ])],
};

const line = {
  id: 'line',
  type: 'XRTable',
  xMm: 0, yMm: 0, wMm: 190, hMm: 14,
  fontSizePt: 7, fontFamily: 'Arial', visible: true,
  rows: [row(14, [
    cell(70, 'INV Against 20% of the installation, testing & commissioning (T&C) of one elevator'),
    cell(16, '1.00', { hAlign: 'Right' }),
    cell(22, '28,500.00', { hAlign: 'Right' }),
    cell(16, '0.00', { hAlign: 'Right' }),
    cell(24, '28,500.00', { hAlign: 'Right' }),
    cell(20, '4,275.00', { hAlign: 'Right' }),
    cell(22, '32,775.00', { hAlign: 'Right' }),
  ])],
};

function totalRow(label, amount, strong) {
  const back = strong ? '#c5d7ea' : shade;
  return row(7, [
    cell(168, label, { back, hAlign: 'Right', bold: !!strong, borders: 'LTB' }),
    cell(22, amount, { hAlign: 'Right', bold: true, borders: 'LRTB' }),
  ]);
}

const totals = {
  id: 'totals',
  type: 'XRTable',
  xMm: 0, yMm: 0, wMm: 190, hMm: 32,
  fontSizePt: 7, fontFamily: 'Arial', visible: true,
  rows: [
    totalRow('Total (Excluding VAT)    الاجمالي غير شامل ضريبة القيمة المضافة', '28,500.00', false),
    totalRow('Total VAT 15% Tax Amount    إجمالي ضريبة القيمة المضافة', '4,275.00', false),
    totalRow('Grand Total Including VAT    الإجمالي شامل القيمة المضافة', '32,775.00', true),
    row(7, [
      cell(190, 'فقط اثنان و ثلاثون ألف و سبعمائة و خمسة و سبعون ريال لا غير', { hAlign: 'Center' }),
    ]),
  ],
};

function label(idName, x, y, w, h, text, extra = {}) {
  return {
    id: idName, type: 'XRLabel', xMm: x, yMm: y, wMm: w, hMm: h,
    text, visible: true, fontFamily: 'Arial', fontSizePt: 8, wordWrap: true, ...extra,
  };
}

const layout = {
  schemaVersion: 1,
  paper: { kind: 'A4', orientation: 'Portrait', widthMm: 210, heightMm: 297 },
  margins: { left: 10, top: 8, right: 10, bottom: 8 },
  guides: { x: [], y: [] },
  fontFamily: 'Arial',
  bands: [
    { id: 'tm', kind: 'TopMargin', heightMm: 8, controls: [] },
    {
      id: 'rh', kind: 'ReportHeader', heightMm: 142, controls: [
        { id: 'logo-jk', type: 'XRPictureBox', xMm: 0, yMm: 0, wMm: 22, hMm: 16, visible: true, sourceUrl: jk, sizing: 'Zoom' },
        label('co', 24, 0, 100, 8, 'Khaled Ahmad Juffali Elevators & Escalators Co.', { fontSizePt: 9, bold: true }),
        label('co-ar', 24, 8, 100, 8, 'شركة خالد أحمد الجفالي للمصاعد والسلالم المحدودة', { fontSizePt: 7 }),
        { id: 'logo-kl', type: 'XRPictureBox', xMm: 128, yMm: 0, wMm: 42, hMm: 14, visible: true, sourceUrl: kleemann, sizing: 'Zoom' },
        {
          id: 'qr', type: 'XRBarCode', xMm: 172, yMm: 0, wMm: 18, hMm: 18, visible: true, text: 'P_8587',
          barCode: { symbology: 'QRCode', showText: false },
        },
        label('title', 24, 18, 140, 7, 'Pro Forma Invoice', { fontSizePt: 16, bold: true, hAlign: 'Center' }),
        label('title-ar', 24, 25, 140, 6, 'فاتورة مبدئية', { fontSizePt: 12, bold: true, hAlign: 'Center' }),
        label('invno', 0, 33, 80, 5, 'Invoice Number: P_8587', { fontSizePt: 8, bold: true }),
        parties,
      ],
    },
    { id: 'ph', kind: 'PageHeader', heightMm: 10, controls: [linesHead] },
    { id: 'det', kind: 'Detail', heightMm: 14, controls: [line] },
    {
      id: 'after', kind: 'DetailReport', heightMm: 64, controls: [
        totals,
        label('due-l', 0, 34, 28, 5, 'Payment Due:', { bold: true }),
        label('due-v', 28, 34, 50, 5, '12 August, 2026'),
        label('bank-h', 0, 42, 80, 5, 'Our Bank Information:', { bold: true, fontSizePt: 9 }),
        label('bank', 0, 48, 28, 4, 'Bank:', { bold: true, fontSizePt: 8 }),
        label('bank-v', 28, 48, 120, 4, 'Saudi National Bank SNB-7502-SR'),
        label('acc', 0, 53, 28, 4, 'Account:', { bold: true }),
        label('acc-v', 28, 53, 80, 4, '65900000347502'),
        label('iban', 0, 58, 28, 4, 'IBAN:', { bold: true }),
        label('iban-v', 28, 58, 90, 4, 'SA8210000065900000347502'),
      ],
    },
    {
      id: 'pf', kind: 'PageFooter', heightMm: 22, controls: [
        label('br1', 0, 1, 60, 18, 'Jeddah Branch - Head Office\nJuffali Building, 3rd Floor, JCAC8530\nPostal Code: 23325\nTel. +966 12 667 2222\nMob. +966 56 629 2055', { fontSizePt: 6.5 }),
        label('br2', 64, 1, 62, 18, 'Riyadh Branch\nJuffali Building, 1st Floor, RHDA8981\nPostal Code: 12214\nTel. +966 11 217 0031\nMob. +966 50 349 1151', { fontSizePt: 6.5 }),
        label('br3', 128, 1, 62, 18, 'Dammam Branch\nJuffali Building, EADA3241\nPostal code: 34227\nTel. +966 13 8588435\nMob. +966 50 349 1151', { fontSizePt: 6.5 }),
      ],
    },
    {
      id: 'bm', kind: 'BottomMargin', heightMm: 8, controls: [
        label('legal', 0, 1, 190, 5, 'C.R. 4030280934 - www.Kjesc.sa - Email: KJEE@juffali.com - Emergency Line: 920003748', { fontSizePt: 6, hAlign: 'Center' }),
      ],
    },
  ],
};

const form = {
  kind: 'PrintForm',
  object: {
    caption: { en: 'Pro forma Juffali', ar: 'فاتورة مبدئية', ru: 'Проформа Juffali' },
    documentTypeMetaId: docType,
    engine: 'Xr',
    scriptMetaId: scriptId,
    layoutJson: JSON.stringify(layout),
    displayOrder: 2,
    metaId: formId,
    name: 'ProFormaJuffaliXr',
    modelId,
  },
};

const root = 'd:/Sources/zuloone-workspace/Sales/Documents/SalesRealization/PrintForms';
writeFileSync(`${root}/ProFormaJuffaliXr.printform.json`, JSON.stringify(form, null, 2));
console.log('layout bytes', form.object.layoutJson.length);
console.log('parties rows', parties.rows.length, 'head', linesHead.rows[0].cells.length);
