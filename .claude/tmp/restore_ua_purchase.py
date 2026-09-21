import json, io, os, collections

UA = "1d269769-c682-4655-be04-4e00fd39eb08"
REG = "a1d44a6a-f4f8-49f8-8cf5-b9179ea9e736"
CRED = "43d58d23-0db9-43b9-8548-421cb88db32e"
DRV = "eda44436-5b33-4353-beac-8ffb535aaeec"
LE_EDT = "4ddbfca6-2464-452f-b913-443463c0094f"
SUP_EDT = "a0362f8d-88dc-4b26-bec9-cf9847d074bb"
AMT = "8aaf0600-87ce-4f9d-b103-cc1379103167"


def cap(en, ar, ru, uk):
    return collections.OrderedDict([("en", en), ("ar", ar), ("ru", ru), ("uk", uk)])


def w(p, o):
    os.makedirs(os.path.dirname(p), exist_ok=True)
    io.open(p, 'w', encoding='utf-8', newline='\n').write(
        json.dumps(o, ensure_ascii=False, indent=2) + '\n')
    print('wrote', p)


def dim(reg, field, metaId, edt, order, capt, desc=None):
    d = collections.OrderedDict([
        ("registerMetaId", reg), ("fieldName", field), ("caption", capt),
        ("edtMetaId", edt), ("isOperational", True), ("isInformational", False),
        ("isPivotDimension", False), ("isTranslatable", False),
        ("displayOrder", order), ("metaId", metaId), ("name", field), ("modelId", UA)])
    if desc:
        d["description"] = desc
    return d


def res(reg, field, metaId, order, capt, desc=None):
    d = collections.OrderedDict([
        ("registerMetaId", reg), ("fieldName", field), ("caption", capt),
        ("isOperational", True), ("edtMetaId", AMT), ("displayOrder", order),
        ("metaId", metaId), ("name", field), ("modelId", UA)])
    if desc:
        d["description"] = desc
    return d


w('LocalizationUkraine/Registers/UaPurchaseFirstEvent/UaPurchaseFirstEvent.object.json',
  collections.OrderedDict([
      ("kind", "Register"),
      ("object", collections.OrderedDict([
          ("caption", cap("Ukraine input VAT first event",
                          "الحدث الأول لضريبة المدخلات (أوكرانيا)",
                          "Податковий кредит: перша подія",
                          "Податковий кредит: перша подія")),
          ("description", "Накопления по поставщику, из которых выводится первое событие на ВХОДЕ (ПКУ 198.2): кредит возникает на дату того, что раньше — списание денег поставщику или получение товара. Зеркало UaVatFirstEvent, но ключ без договора: договоров поставки в Purchasing нет."),
          ("isObsolete", False), ("iconName", "fluent-color:clock-32"),
          ("largeIconName", "fluent-color:clock-32"), ("registerEngineType", "Standard"),
          ("isDoubleEntry", False), ("allowNegativeBalance", True),
          ("useBalanceTable", True), ("isExtension", False), ("metaId", REG),
          ("name", "UaPurchaseFirstEvent"), ("modelId", UA), ("totalDriverMetaId", DRV)])),
      ("dimensions", [
          dim(REG, "LegalEntity", "70a76006-4287-415c-a122-19f9585e0c69", LE_EDT, 1,
              cap("Legal entity", "الكيان القانوني", "Юрлицо", "Юрособа"),
              "Покупатель, чьим налоговым кредитом станет движение. Режим налогообложения — свойство юрлица, а драйвер читает только координаты."),
          dim(REG, "Supplier", "b1e952a1-025f-40ec-bffe-1275cd212fbe", SUP_EDT, 2,
              cap("Supplier", "المورد", "Поставщик", "Постачальник"))]),
      ("resources", [
          res(REG, "Received", "d9a61550-030e-4bbe-acf7-d272b00fefa4", 1,
              cap("Received base", "أساس الاستلام", "Получено (база)", "Отримано (база)"),
              "База БЕЗ налога. Сравнивать напрямую с PaidOut нельзя: там деньги С налогом."),
          res(REG, "PaidOut", "4d8f7696-e8dc-4996-812d-c98297f08d18", 2,
              cap("Paid out", "المدفوع", "Уплачено поставщику", "Сплачено постачальнику"),
              "Деньги С налогом. К базе их приводит драйвер: для этого нужна ставка, а она резолвится асинхронно."),
          res(REG, "Credited", "17dfde0e-56a7-4a09-8796-bbba42be59a1", 3,
              cap("Base already credited", "الأساس المخصوم بالفعل",
                  "База, уже зачтённая", "База, вже зарахована"),
              "Сколько базы уже дало налоговый кредит. Разница с max(Received, PaidOut) и есть то, что зачитывается сейчас.")])]))

w('LocalizationUkraine/Registers/UaVatCredit/UaVatCredit.object.json',
  collections.OrderedDict([
      ("kind", "Register"),
      ("object", collections.OrderedDict([
          ("caption", cap("Ukraine input VAT credit", "رصيد ضريبة المدخلات (أوكرانيا)",
                          "Податковий кредит з ПДВ", "Податковий кредит з ПДВ")),
          ("description", "Сам налоговый кредит по первому событию на входе. Зеркало UaVatPayable: тот держит обязательство, этот — право на зачёт. Ключ измерениями, а не аналитиками: измерения драйвер пишет напрямую координатами."),
          ("isObsolete", False), ("iconName", "fluent-color:receipt-32"),
          ("largeIconName", "fluent-color:receipt-32"), ("registerEngineType", "Standard"),
          ("isDoubleEntry", False), ("allowNegativeBalance", True),
          ("useBalanceTable", True), ("isExtension", False), ("metaId", CRED),
          ("name", "UaVatCredit"), ("modelId", UA)])),
      ("dimensions", [
          dim(CRED, "LegalEntity", "bde57085-db8c-488b-86f1-04185e5b7011", LE_EDT, 1,
              cap("Legal entity", "الكيان القانوني", "Юрлицо", "Юрособа")),
          dim(CRED, "Supplier", "7b32ce3a-f97b-4a06-9cf7-34a990bea7e3", SUP_EDT, 2,
              cap("Supplier", "المورد", "Поставщик", "Постачальник"))]),
      ("resources", [
          res(CRED, "Amount", "8215fd3e-c6d0-402c-ade6-6532db9f93a7", 1,
              cap("Amount", "المبلغ", "Сумма", "Сума"))])]))

w('LocalizationUkraine/TotalDrivers/UaPurchaseFirstEvent/UaPurchaseFirstEvent.driver.json',
  collections.OrderedDict([
      ("kind", "TotalDriver"),
      ("object", collections.OrderedDict([
          ("caption", cap("Ukraine input-VAT first-event driver",
                          "محرك الحدث الأول لضريبة المدخلات (أوكرانيا)",
                          "Драйвер податкового кредиту першої події",
                          "Драйвер податкового кредиту першої події")),
          ("baseClassName", "DefaultTotalDriver"), ("baseEngine", "Standard"),
          ("isKernel", False),
          ("description", "Драйвер регистра UaPurchaseFirstEvent: после записи движений берёт max(Received, PaidOut), сравнивает с уже зачтённой базой и даёт кредит только на прирост. Неплательщик ПДВ кредита не получает."),
          ("metaId", DRV), ("name", "UaPurchaseFirstEvent"), ("modelId", UA)]))]))

w('LocalizationUkraine/TotalDrivers/UaPurchaseFirstEvent/UaPurchaseFirstEventTotalDriver.script.json',
  collections.OrderedDict([
      ("kind", "Script"),
      ("object", collections.OrderedDict([
          ("scriptType", "TotalDriver"), ("objectType", "TotalDriver"),
          ("objectName", "UaPurchaseFirstEvent"), ("objectMetaId", DRV),
          ("executionOrder", 0), ("metaId", "d4f1a1e4-8cb4-4861-9f58-fd40f73ceaf7"),
          ("name", "UaPurchaseFirstEventTotalDriver"), ("modelId", UA)]))]))

for name, doc, ext, desc in [
        ("PurchaseOrder", "6935af7d-5f73-45d5-ad4c-d4a21dbe0b67",
         "0e14dbd9-a864-4532-8c92-59f4c4492c3f",
         "Перша подія на входе: получение товара двигает базу налогового кредита"),
        ("VendorPayment", "6eb069ee-3312-4f26-8823-5fcfdadc0750",
         "e0500b30-88db-4261-9e9f-b5ef6806f002",
         "Перша подія на входе: оплата поставщику двигает базу налогового кредита"),
        ("PurchaseCreditNote", "10f6b734-1288-40f2-aa0f-3717a4e3995f",
         "092a6d75-f8c0-4961-8153-73f5b890aa90",
         "Возврат поставщику уменьшает базу налогового кредита")]:
    w('LocalizationUkraine/DocumentExtensions/%s.LocalizationUkraine/%s.LocalizationUkraine.extension.json' % (name, name),
      collections.OrderedDict([
          ("kind", "DocumentExtension"),
          ("object", collections.OrderedDict([
              ("targetDocumentTypeMetaId", doc), ("description", desc),
              ("metaId", ext), ("name", name + ".LocalizationUkraine"), ("modelId", UA)]))]))

for cls, docname, subtype, ext, meta in [
        ("UaPurchaseReceiptTx", "PurchaseOrder", "9bbf8cdb-8913-4286-8f9c-31809cca4231",
         "0e14dbd9-a864-4532-8c92-59f4c4492c3f", "cb0e1e4c-663e-4f62-992d-d6bf796093fc"),
        ("UaVendorPaymentTx", "VendorPayment", "e1e26bfb-8bba-44cb-9428-3f40f8b4df26",
         "e0500b30-88db-4261-9e9f-b5ef6806f002", "7ece3e64-ea31-48e7-a2ba-c9c8be037b77"),
        ("UaPurchaseCreditTx", "PurchaseCreditNote", "c817dae1-d658-4f61-928e-d327bd3b8a08",
         "092a6d75-f8c0-4961-8153-73f5b890aa90", "6d726072-de99-4e24-bd78-2b5f12aebbd3")]:
    w('LocalizationUkraine/DocumentExtensions/%s.LocalizationUkraine/%s.script.json' % (docname, cls),
      collections.OrderedDict([
          ("kind", "Script"),
          ("object", collections.OrderedDict([
              ("extensionMetaId", ext), ("scriptType", "TransactionScript"),
              ("objectType", "Document"), ("objectName", docname),
              ("objectMetaId", subtype), ("executionOrder", 7),
              ("metaId", meta), ("name", cls), ("modelId", UA)]))]))

# The binding that actually makes a tx script run lives in the TARGET document's
# own file, with OUR modelId — the sanctioned extension-direction pointer.
for path, subtype, script, meta in [
        ('Purchasing/Documents/PurchaseOrder/PurchaseOrder.object.json',
         "9bbf8cdb-8913-4286-8f9c-31809cca4231", "cb0e1e4c-663e-4f62-992d-d6bf796093fc",
         "a4b60954-273d-40cd-9c6a-0729f9956711"),
        ('Purchasing/Documents/VendorPayment/VendorPayment.object.json',
         "e1e26bfb-8bba-44cb-9428-3f40f8b4df26", "7ece3e64-ea31-48e7-a2ba-c9c8be037b77",
         "f9780f5b-d72d-4d15-8699-c5a315d4eea6"),
        ('Purchasing/Documents/PurchaseCreditNote/PurchaseCreditNote.object.json',
         "c817dae1-d658-4f61-928e-d327bd3b8a08", "6d726072-de99-4e24-bd78-2b5f12aebbd3",
         "e5b3fed6-6052-456a-926e-9ac8f892f9f3")]:
    d = json.load(open(path, encoding='utf-8'), object_pairs_hook=collections.OrderedDict)
    lst = d.setdefault('subtypeTransactionScripts', [])
    if not any(x.get('scriptMetaId') == script for x in lst):
        lst.append(collections.OrderedDict([
            ("subtypeMetaId", subtype), ("scriptMetaId", script),
            ("executionOrder", 7), ("metaId", meta), ("name", ""), ("modelId", UA)]))
        io.open(path, 'w', encoding='utf-8', newline='\n').write(
            json.dumps(d, ensure_ascii=False, indent=2) + '\n')
        print('linked', path.split('/')[-1])
