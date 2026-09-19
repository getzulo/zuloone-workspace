import json
from pathlib import Path

src = Path(r"D:\Sources\zuloone-workspace\LocalizationSaudiArabia\Documents\SalesRealization\PrintForms\ZatcaInvoiceXr.printform.json")
data = json.loads(src.read_text(encoding="utf-8"))
layout = data["object"]["layoutJson"].replace("Invoice no. / رقم الفاتورة", "No. / الرقم")
model = "8d2f5a41-9c6b-4e3f-8a7d-1b4c6e2f9a50"


def emit(path, caption, name, form_id, script_id, doc_type, bind_id, subtype):
    obj = {
        "kind": "PrintForm",
        "object": {
            "caption": caption,
            "documentTypeMetaId": doc_type,
            "engine": "Xr",
            "scriptMetaId": script_id,
            "layoutJson": layout,
            "displayOrder": 2,
            "metaId": form_id,
            "name": name,
            "modelId": model,
        },
        "bindings": [
            {
                "metaId": bind_id,
                "printFormMetaId": form_id,
                "targetKind": "DocumentSubtype",
                "targetMetaId": subtype,
                "scope": "Record",
            }
        ],
    }
    Path(path).write_text(json.dumps(obj, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


emit(
    r"D:\Sources\zuloone-workspace\LocalizationSaudiArabia\Documents\SalesCreditNote\PrintForms\ZatcaCreditNoteXr.printform.json",
    {
        "en": "Tax credit note (ZATCA)",
        "ar": "إشعار دائن ضريبي (هيئة الزكاة والضريبة والجمارك)",
        "ru": "Кредит-нота (ZATCA)",
        "uk": "Кредит-нота (ZATCA)",
    },
    "ZatcaCreditNoteXr",
    "3e7c1a90-6b24-4d8f-9e51-2a0c8d4b7f13",
    "8f2d5c61-4a93-4b7e-81c0-9d6e3f1a5b28",
    "f53f1b8a-8458-4f27-ab5e-b75f7228d263",
    "1a9b4e70-2c85-4f16-a3d8-7e5b0c9f4a62",
    "615d7027-6869-4f85-a4f4-5ebea5ae8aac",
)
emit(
    r"D:\Sources\zuloone-workspace\LocalizationSaudiArabia\Documents\SalesDebitNote\PrintForms\ZatcaDebitNoteXr.printform.json",
    {
        "en": "Tax debit note (ZATCA)",
        "ar": "إشعار مدين ضريبي (هيئة الزكاة والضريبة والجمارك)",
        "ru": "Дебет-нота (ZATCA)",
        "uk": "Дебет-нота (ZATCA)",
    },
    "ZatcaDebitNoteXr",
    "6d4f8b21-9c37-4e50-b2a1-5f8e3c7d0a94",
    "c0e7a352-1d68-4f9b-8c24-3a7e6b1d5f80",
    "465e8b08-5939-4c30-99ea-fef5a4fbc44a",
    "9b3e6d14-5a82-4c70-91f6-2d8c4a7e0b35",
    "b82f0cf0-6b83-4a80-a31a-36d81d0ae057",
)
print("ok")
