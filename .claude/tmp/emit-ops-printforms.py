# One-shot layout emitter for ProductionOrderXr / DeliveryTripXr / TaxReturnXr / TaxPaymentXr.
import json
from pathlib import Path

ROOT = Path(r"d:\Sources\zuloone-workspace")


def lbl(cid, x, y, w, h, text, *, size=9, bold=False, align=None, borders=None):
    c = {
        "id": cid,
        "type": "XRLabel",
        "xMm": x,
        "yMm": y,
        "wMm": w,
        "hMm": h,
        "fontSizePt": size,
        "visible": True,
        "text": text,
    }
    if bold:
        c["bold"] = True
    if align:
        c["hAlign"] = align
    if borders:
        c["borders"] = borders
    return c


def field(cid, x, y, w, h, binding, *, size=9, align=None, borders=None, fmt=None, bold=False):
    c = {
        "id": cid,
        "type": "XRLabel",
        "xMm": x,
        "yMm": y,
        "wMm": w,
        "hMm": h,
        "fontSizePt": size,
        "visible": True,
        "binding": binding,
        "text": f"[{binding}]",
        "wordWrap": True,
    }
    if align:
        c["hAlign"] = align
    if borders:
        c["borders"] = borders
    if fmt:
        c["format"] = fmt
    if bold:
        c["bold"] = True
    return c


def header_row(idx, label, binding, y):
    return [
        lbl(f"lbl{idx}", 0, y, 34, 5, label, bold=True),
        field(f"f{idx}", 34, y, 146, 5, binding),
    ]


def qty_cols():
    return {
        "ph": [
            lbl("th-no", 0, 1, 14, 6, "#", size=8, bold=True, borders="B"),
            lbl("th-item", 14, 1, 110, 6, "Item", size=8, bold=True, borders="B"),
            lbl("th-qty", 124, 1, 28, 6, "Qty", size=8, bold=True, align="Right", borders="B"),
            lbl("th-unit", 152, 1, 28, 6, "Unit", size=8, bold=True, borders="B"),
        ],
        "det": [
            field("c-no", 0, 0.5, 14, 6, "LineNo", size=8, borders="B"),
            field("c-item", 14, 0.5, 110, 6, "Item", size=8, borders="B"),
            field("c-qty", 124, 0.5, 28, 6, "Quantity", size=8, align="Right", borders="B"),
            field("c-unit", 152, 0.5, 28, 6, "Unit", size=8, borders="B"),
        ],
    }


def shell(title, header_controls, header_h, ph, det, footer_controls=None, footer_h=12):
    return {
        "margins": {"left": 15, "top": 10, "right": 15, "bottom": 10},
        "guides": {"x": [], "y": []},
        "bands": [
            {"id": "tm", "kind": "TopMargin", "heightMm": 10, "controls": []},
            {
                "id": "rh",
                "kind": "ReportHeader",
                "heightMm": header_h,
                "controls": [
                    lbl("title", 0, 1, 180, 9, title, size=16, bold=True),
                    *header_controls,
                ],
            },
            {"id": "ph", "kind": "PageHeader", "heightMm": 8, "controls": ph},
            {"id": "det", "kind": "Detail", "heightMm": 7, "controls": det},
            {"id": "rf", "kind": "ReportFooter", "heightMm": footer_h, "controls": footer_controls or []},
            {
                "id": "pf",
                "kind": "PageFooter",
                "heightMm": 10,
                "controls": [
                    {
                        "id": "pg",
                        "type": "XRPageInfo",
                        "xMm": 140,
                        "yMm": 2,
                        "wMm": 40,
                        "hMm": 5,
                        "pageInfo": "NumberOfTotal",
                        "hAlign": "Right",
                        "fontSizePt": 8,
                        "visible": True,
                        "wordWrap": True,
                    }
                ],
            },
            {"id": "bm", "kind": "BottomMargin", "heightMm": 10, "controls": []},
        ],
        "paper": {"kind": "A4", "orientation": "Portrait", "widthMm": 210, "heightMm": 297},
        "schemaVersion": 1,
    }


def dump(path, obj):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(obj, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("wrote", path)


qty = qty_cols()

prod = {
    "kind": "PrintForm",
    "object": {
        "caption": {
            "en": "Production order",
            "ar": "أمر الإنتاج",
            "ru": "Наряд",
            "uk": "Наряд",
        },
        "documentTypeMetaId": "3a0b2c8f-5e9d-4f1a-8b2c-2d6e7f3a8b10",
        "engine": "Xr",
        "scriptMetaId": "caa45c2d-95c3-456f-a212-87f201b20e1d",
        "layoutJson": json.dumps(
            shell(
                "PRODUCTION ORDER",
                header_row(0, "No.", "Number", 12)
                + header_row(1, "Date", "DocumentDate", 18)
                + header_row(2, "Cell", "Location", 24)
                + header_row(3, "Product", "Notes", 30),
                38,
                qty["ph"],
                qty["det"],
            ),
            separators=(",", ":"),
        ),
        "displayOrder": 1,
        "metaId": "8456ae65-d95e-4c10-ad82-789f94e21489",
        "name": "ProductionOrderXr",
        "modelId": "7d3e9c14-2a5b-4f8e-9c1d-3b6a7e2f5d80",
    },
}
dump(ROOT / "Production/Documents/ProductionOrder/PrintForms/ProductionOrderXr.printform.json", prod)

trip_ph = [
    lbl("th-no", 0, 1, 12, 6, "#", size=8, bold=True, borders="B"),
    lbl("th-ord", 12, 1, 48, 6, "Order", size=8, bold=True, borders="B"),
    lbl("th-out", 60, 1, 62, 6, "Outlet", size=8, bold=True, borders="B"),
    lbl("th-qty", 122, 1, 22, 6, "Qty", size=8, bold=True, align="Right", borders="B"),
    lbl("th-outc", 144, 1, 36, 6, "Outcome", size=8, bold=True, borders="B"),
]
trip_det = [
    field("c-no", 0, 0.5, 12, 6, "LineNo", size=8, borders="B"),
    field("c-ord", 12, 0.5, 48, 6, "Item", size=8, borders="B"),
    field("c-out", 60, 0.5, 62, 6, "Outlet", size=8, borders="B"),
    field("c-qty", 122, 0.5, 22, 6, "Quantity", size=8, align="Right", borders="B"),
    field("c-outc", 144, 0.5, 36, 6, "LineNote", size=8, borders="B"),
]
trip = {
    "kind": "PrintForm",
    "object": {
        "caption": {
            "en": "Delivery trip",
            "ar": "رحلة التوصيل",
            "ru": "Лист рейса",
            "uk": "Лист рейсу",
        },
        "documentTypeMetaId": "4e480847-88a3-4af8-8604-6325d12fe7b9",
        "engine": "Xr",
        "scriptMetaId": "b9272259-54ea-46a3-a36e-973282675e37",
        "layoutJson": json.dumps(
            shell(
                "DELIVERY TRIP",
                header_row(0, "No.", "Number", 12)
                + header_row(1, "Date", "DocumentDate", 18)
                + header_row(2, "Driver", "Seller", 24)
                + header_row(3, "Route", "Contract", 30)
                + header_row(4, "Vehicle", "Notes", 36)
                + header_row(5, "Depot", "Location", 42),
                50,
                trip_ph,
                trip_det,
            ),
            separators=(",", ":"),
        ),
        "displayOrder": 1,
        "metaId": "ee1eca69-307a-491b-84c0-23b9910ea346",
        "name": "DeliveryTripXr",
        "modelId": "47861dd2-1009-4926-9ea3-c506cae5118d",
    },
}
dump(ROOT / "Sales/Documents/DeliveryTrip/PrintForms/DeliveryTripXr.printform.json", trip)

ret_ph = [
    lbl("th-no", 0, 1, 12, 6, "#", size=8, bold=True, borders="B"),
    lbl("th-code", 12, 1, 70, 6, "Tax code", size=8, bold=True, borders="B"),
    lbl("th-dir", 82, 1, 34, 6, "Direction", size=8, bold=True, borders="B"),
    lbl("th-base", 116, 1, 32, 6, "Base", size=8, bold=True, align="Right", borders="B"),
    lbl("th-tax", 148, 1, 32, 6, "Tax", size=8, bold=True, align="Right", borders="B"),
]
ret_det = [
    field("c-no", 0, 0.5, 12, 6, "LineNo", size=8, borders="B"),
    field("c-code", 12, 0.5, 70, 6, "Item", size=8, borders="B"),
    field("c-dir", 82, 0.5, 34, 6, "LineNote", size=8, borders="B"),
    field("c-base", 116, 0.5, 32, 6, "Quantity", size=8, align="Right", fmt="{0:n2}", borders="B"),
    field("c-tax", 148, 0.5, 32, 6, "Amount", size=8, align="Right", fmt="{0:n2}", borders="B"),
]
ret_footer = [
    lbl("lbl-out", 90, 2, 50, 5, "Output tax", size=9, align="Right"),
    field("out", 140, 2, 40, 5, "SubTotal", size=9, align="Right", fmt="{0:n2}"),
    lbl("lbl-in", 90, 8, 50, 5, "Input tax", size=9, align="Right"),
    field("inp", 140, 8, 40, 5, "TaxAmount", size=9, align="Right", fmt="{0:n2}"),
    lbl("lbl-net", 90, 14, 50, 6, "Net payable", size=11, bold=True, align="Right"),
    field("net", 140, 14, 40, 6, "Total", size=11, align="Right", fmt="{0:n2}", bold=True),
]
ret = {
    "kind": "PrintForm",
    "object": {
        "caption": {
            "en": "Tax return",
            "ar": "الإقرار الضريبي",
            "ru": "Декларация",
            "uk": "Декларація",
        },
        "documentTypeMetaId": "fdba6c82-e480-4aea-8ca3-1cb91e04c6df",
        "engine": "Xr",
        "scriptMetaId": "c363b42c-5f2d-457f-9063-76e8e0f662bb",
        "layoutJson": json.dumps(
            shell(
                "TAX RETURN",
                header_row(0, "No.", "Number", 12)
                + header_row(1, "Date", "DocumentDate", 18)
                + header_row(2, "Legal entity", "Seller", 24)
                + header_row(3, "Period", "Notes", 30),
                38,
                ret_ph,
                ret_det,
                ret_footer,
                22,
            ),
            separators=(",", ":"),
        ),
        "displayOrder": 1,
        "metaId": "865d7685-c0d8-42e5-9229-c36161ecfab9",
        "name": "TaxReturnXr",
        "modelId": "39489aa3-094f-411d-a0a0-d5089980790a",
    },
}
dump(ROOT / "Tax/Documents/TaxReturn/PrintForms/TaxReturnXr.printform.json", ret)

pay_ph = [
    lbl("th-no", 0, 1, 12, 6, "#", size=8, bold=True, borders="B"),
    lbl("th-code", 12, 1, 140, 6, "Tax code", size=8, bold=True, borders="B"),
    lbl("th-amt", 152, 1, 28, 6, "Amount", size=8, bold=True, align="Right", borders="B"),
]
pay_det = [
    field("c-no", 0, 0.5, 12, 6, "LineNo", size=8, borders="B"),
    field("c-code", 12, 0.5, 140, 6, "LineParty", size=8, borders="B"),
    field("c-amt", 152, 0.5, 28, 6, "Amount", size=8, align="Right", fmt="{0:n2}", borders="B"),
]
pay_footer = [
    lbl("lbl-Total", 110, 2, 40, 6, "Total", size=11, bold=True, align="Right"),
    field("total", 150, 2, 30, 6, "Total", size=11, align="Right", fmt="{0:n2}", bold=True),
]
pay = {
    "kind": "PrintForm",
    "object": {
        "caption": {
            "en": "Tax payment",
            "ar": "سداد الضريبة",
            "ru": "Оплата налога",
            "uk": "Сплата податку",
        },
        "documentTypeMetaId": "8f3c1a67-2d90-4e45-b8c1-5a7e9d0f2b34",
        "engine": "Xr",
        "scriptMetaId": "a980787b-0cd3-4553-9a38-1c0556a365a3",
        "layoutJson": json.dumps(
            shell(
                "TAX PAYMENT",
                header_row(0, "No.", "Number", 12)
                + header_row(1, "Date", "DocumentDate", 18)
                + header_row(2, "Legal entity", "Seller", 24),
                32,
                pay_ph,
                pay_det,
                pay_footer,
                12,
            ),
            separators=(",", ":"),
        ),
        "displayOrder": 1,
        "metaId": "516bdc37-3ee6-4b6b-a838-988847cbe49b",
        "name": "TaxPaymentXr",
        "modelId": "39489aa3-094f-411d-a0a0-d5089980790a",
    },
}
dump(ROOT / "Tax/Documents/TaxPayment/PrintForms/TaxPaymentXr.printform.json", pay)
