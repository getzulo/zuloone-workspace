/**
 * Stamp Fluent Color icons on workspace objects and menu items.
 * Skips Core/. Also PATCHes the live API (sync is held).
 */
import { readFileSync, writeFileSync, readdirSync } from 'node:fs';
import { join } from 'node:path';

const ROOT = 'D:/Sources/zuloone-workspace';
const API = 'http://localhost:5257';
const P = 'fluent-color:';

const ICONS = {
  Accounting: 'book-48',
  Common: 'globe-24',
  Costing: 'gauge-32',
  CRM: 'people-community-48',
  HR: 'people-team-48',
  Inventory: 'building-multiple-24',
  SaudiArabia: 'building-government-32',
  Organization: 'org-48',
  Production: 'wrench-screwdriver-32',
  Purchasing: 'briefcase-48',
  Sales: 'building-store-24',
  Tax: 'certificate-32',
  Okrasheno: 'paint-brush-32',
  Documents: 'document-48',
  Dictionaries: 'database-48',
  Registers: 'table-48',
  Settings: 'settings-48',

  AccountingSettings: 'book-database-32',
  ChartOfAccounts: 'book-open-48',
  FiscalPeriod: 'calendar-clock-24',
  FiscalYear: 'calendar-48',
  JournalEntry: 'document-text-48',
  GL: 'chart-multiple-32',

  Address: 'location-ripple-24',
  City: 'building-home-32',
  CommonSettings: 'settings-48',
  Country: 'globe-24',
  Currency: 'coin-multiple-48',
  Gender: 'diversity-48',
  PaymentTerm: 'clock-48',
  Region: 'planet-32',
  UnitClass: 'apps-list-32',
  UnitOfMeasure: 'gauge-32',

  CostingSettings: 'data-pie-32',
  InventoryValue: 'vault-24',
  ItemCostFifo: 'data-trending-48',

  CRMSettings: 'people-chat-48',
  LoyaltyTier: 'trophy-48',
  LoyaltyRedemption: 'gift-24',
  LoyaltyPoints: 'star-48',

  Employee: 'person-48',
  HRSettings: 'people-team-48',
  Position: 'briefcase-48',
  PayrollAccrual: 'document-text-48',
  PayrollPayment: 'savings-32',
  SocialInsuranceAccrual: 'shield-48',
  SocialInsurancePayment: 'shield-checkmark-48',
  TimeSheet: 'shifts-32',
  Payroll: 'coin-multiple-48',
  PayrollLiability: 'data-area-32',
  SocialInsurance: 'lock-shield-48',

  Brand: 'ribbon-star-32',
  Customer: 'people-48',
  CustomerContact: 'contact-card-48',
  InventorySettings: 'toolbox-32',
  Item: 'apps-list-detail-32',
  ItemGroup: 'document-folder-24',
  ItemUnit: 'number-symbol-square-32',
  PriceListItem: 'premium-32',
  PriceType: 'bookmark-32',
  Store: 'building-store-24',
  StoreCell: 'apps-48',
  StoreCellType: 'options-48',
  StoreZone: 'board-24',
  Supplier: 'people-list-32',
  GoodsIssue: 'send-48',
  PickTask: 'clipboard-task-24',
  PutAwayTask: 'document-add-48',
  StockAdjustment: 'edit-32',
  StockCount: 'checkbox-24',
  StockTransfer: 'arrow-sync-24',
  Stock: 'building-multiple-24',

  LocalizationSaudiArabiaSettings: 'building-government-32',
  VatPayable: 'receipt-32',

  Division: 'building-people-24',
  DivisionType: 'org-48',
  LegalEntity: 'building-48',
  OrganizationSettings: 'building-48',

  BillOfMaterials: 'text-bullet-list-square-48',
  BomComponent: 'puzzle-piece-48',
  ProductionSettings: 'wrench-screwdriver-32',
  ProductionOrder: 'clipboard-text-edit-32',

  PurchasingSettings: 'briefcase-48',
  PurchaseOrder: 'clipboard-48',
  VendorPayment: 'savings-32',
  Payable: 'data-bar-vertical-ascending-24',

  DeliveryRoute: 'location-ripple-24',
  SalesPerson: 'person-available-24',
  SalesSettings: 'megaphone-loud-32',
  CustomerPayment: 'savings-32',
  DeliveryTrip: 'send-clock-32',
  SalesInvoice: 'receipt-32',
  SalesOrder: 'clipboard-task-24',
  SalesReturn: 'arrow-square-down-32',
  Receivable: 'data-trending-48',
  ReservedStock: 'lock-closed-48',
  Revenue: 'coin-multiple-48',

  TaxAuthority: 'building-government-32',
  TaxAuthorityConnection: 'link-32',
  TaxCategory: 'bookmark-32',
  TaxCode: 'number-symbol-square-32',
  TaxDirection: 'arrow-trending-lines-24',
  TaxJurisdiction: 'globe-shield-48',
  TaxRate: 'data-pie-32',
  TaxRule: 'checkbox-24',
  TaxRuleCondition: 'options-48',
  TaxSettings: 'building-government-search-32',
  TaxCalculation: 'gauge-32',
  TaxPayment: 'savings-32',
  TaxReturn: 'form-48',
  TaxLedger: 'table-48',

  TBItem: 'apps-48',
  TBWarehouse: 'building-multiple-24',
  TBStockDoc: 'document-48',
  TBFifo: 'data-trending-48',
  TBFifoDriven: 'data-line-32',
  TBStock: 'table-48',
};

function iconOf(name, kind) {
  if (ICONS[name]) return P + ICONS[name];
  if (kind === 'Document') return P + 'document-48';
  if (kind === 'Register') return P + 'table-48';
  if (kind === 'Dictionary') return P + 'database-48';
  if (kind === 'Group') return P + 'document-folder-24';
  return P + 'apps-48';
}

function walk(dir, acc = []) {
  if (dir.includes('.generated') || dir.includes('node_modules') || /(?:^|[\\/])Core[\\/]/.test(dir)) return acc;
  for (const e of readdirSync(dir, { withFileTypes: true })) {
    const p = join(dir, e.name);
    if (e.isDirectory()) walk(p, acc);
    else acc.push(p);
  }
  return acc;
}

function insertAfterName(text, name, extra, indent) {
  const needle = `${indent}"name": ${JSON.stringify(name)},`;
  const idx = text.indexOf(needle);
  if (idx < 0) return null;
  const after = idx + needle.length;
  const rest = text.slice(after);
  const replaceRe = new RegExp(
    `^\\n${indent}"(?:icon|iconName|largeIconName)": "[^"]*",`,
  );
  let out = text;
  let cursor = after;
  let chunk = rest;
  while (true) {
    const m = chunk.match(replaceRe);
    if (!m) break;
    out = out.slice(0, cursor) + chunk.slice(m[0].length);
    chunk = out.slice(cursor);
  }
  return out.slice(0, cursor) + extra + out.slice(cursor);
}

const stamped = [];
let objects = 0;
let menus = 0;
const missing = [];

for (const f of walk(ROOT).filter((p) => p.endsWith('.object.json'))) {
  const raw = readFileSync(f, 'utf8');
  const j = JSON.parse(raw);
  const name = j.object?.name;
  if (!name) continue;
  const icon = iconOf(name, j.kind);
  if (!ICONS[name]) missing.push(`${j.kind}\t${name}`);
  stamped.push({ kind: j.kind, name, metaId: j.object.metaId, icon });
  const extra = `\n    "iconName": ${JSON.stringify(icon)},\n    "largeIconName": ${JSON.stringify(icon)},`;
  const next = insertAfterName(raw, name, extra, '    ');
  if (!next) {
    console.error('no name line', f);
    continue;
  }
  if (next !== raw) {
    writeFileSync(f, next, 'utf8');
    objects += 1;
  }
}

for (const f of walk(ROOT).filter((p) => p.replaceAll('\\', '/').endsWith('Menu/menu.json'))) {
  let raw = readFileSync(f, 'utf8');
  const j = JSON.parse(raw);
  for (const it of j.items ?? []) {
    const icon = iconOf(it.name, it.targetType);
    if (!ICONS[it.name]) missing.push(`Menu\t${it.name}`);
    stamped.push({ kind: 'Menu', name: it.name, metaId: it.metaId, icon });
    const extra = `\n      "icon": ${JSON.stringify(icon)},`;
    const next = insertAfterName(raw, it.name, extra, '      ');
    if (!next) {
      console.error('no menu name', f, it.name);
      continue;
    }
    if (next !== raw) {
      raw = next;
      menus += 1;
    }
  }
  writeFileSync(f, raw, 'utf8');
}

console.log(`files: objects ${objects}, menu items ${menus}`);
if (missing.length) console.log('fallback:\n' + [...new Set(missing)].join('\n'));

async function pushApi() {
  const loginRes = await fetch(`${API}/api/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ name: 'reacttest', password: 'Test1234!' }),
  });
  if (!loginRes.ok) {
    throw new Error(`login ${loginRes.status} ${await loginRes.text()}`);
  }
  const { token } = await loginRes.json();
  const headers = {
    Authorization: `Bearer ${token}`,
    'Content-Type': 'application/json',
  };

  async function getJson(path) {
    const r = await fetch(`${API}${path}`, { headers });
    if (!r.ok) throw new Error(`GET ${path} ${r.status} ${await r.text()}`);
    return r.json();
  }

  async function putJson(path, body) {
    const r = await fetch(`${API}${path}`, {
      method: 'PUT',
      headers,
      body: JSON.stringify(body),
    });
    if (!r.ok) {
      const t = await r.text();
      throw new Error(`PUT ${path} ${r.status} ${t.slice(0, 400)}`);
    }
    return r.json().catch(() => null);
  }

  const byId = new Map(stamped.map((s) => [s.metaId, s]));
  let updated = 0;
  let skipped = 0;
  let failed = 0;

  const collections = [
    { path: '/api/metadata/dictionaries', put: (row) => `/api/metadata/dictionaries/${row.metaId}`, fields: ['iconName', 'largeIconName'] },
    { path: '/api/metadata/documenttypes', put: (row) => `/api/metadata/documenttypes/${row.metaId}`, fields: ['iconName', 'largeIconName'] },
    { path: '/api/metadata/registers', put: (row) => `/api/metadata/registers/${row.metaId}`, fields: ['iconName', 'largeIconName'] },
    { path: '/api/metadata/menu', put: (row) => `/api/metadata/menu/${row.metaId}`, fields: ['icon'] },
  ];

  for (const col of collections) {
    const rows = await getJson(col.path);
    for (const row of rows) {
      const want = byId.get(row.metaId);
      if (!want) {
        skipped += 1;
        continue;
      }
      const next = { ...row };
      for (const field of col.fields) next[field] = want.icon;
      const same = col.fields.every((field) => row[field] === want.icon);
      if (same) continue;
      try {
        await putJson(col.put(row), next);
        updated += 1;
        console.log('api', row.name, want.icon);
      } catch (err) {
        failed += 1;
        console.error('api fail', row.name, err.message);
      }
    }
  }

  console.log(`api: updated ${updated}, skipped-other ${skipped}, failed ${failed}`);
}

await pushApi();
