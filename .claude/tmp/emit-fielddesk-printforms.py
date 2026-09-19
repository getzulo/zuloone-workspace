# Layouts for SalesLeadXr, LoyaltyRedemptionXr, VisitRouteXr, AgentVisitXr.
import json
from pathlib import Path

ROOT = Path(r"d:\Sources\zuloone-workspace")


def lbl(cid, x, y, w, h, text, *, size=9, bold=False, align=None, borders=None):
    c = {
        "id": cid, "type": "XRLabel", "xMm": x, "yMm": y, "wMm": w, "hMm": h,
        "fontSizePt": size, "visible": True, "text": text,
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
        "id": cid, "type": "XRLabel", "xMm": x, "yMm": y, "wMm": w, "hMm": h,
        "fontSizePt": size, "visible": True, "binding": binding,
        "text": f"[{binding}]", "wordWrap": True,
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


def shell(title, header_controls, header_h, ph, det, footer_controls=None, footer_h=12):
    return {
        "margins": {"left": 15, "top": 10, "right": 15, "bottom": 10},
        "guides": {"x": [], "y": []},
        "bands": [
            {"id": "tm", "kind": "TopMargin", "heightMm": 10, "controls": []},
            {"id": "rh", "kind": "ReportHeader", "heightMm": header_h,
             "controls": [lbl("title", 0, 1, 180, 9, title, size=16, bold=True), *header_controls]},
            {"id": "ph", "kind": "PageHeader", "heightMm": 8, "controls": ph},
            {"id": "det", "kind": "Detail", "heightMm": 7, "controls": det},
            {"id": "rf", "kind": "ReportFooter", "heightMm": footer_h, "controls": footer_controls or []},
            {"id": "pf", "kind": "PageFooter", "heightMm": 10, "controls": [{
                "id": "pg", "type": "XRPageInfo", "xMm": 140, "yMm": 2, "wMm": 40, "hMm": 5,
                "pageInfo": "NumberOfTotal", "hAlign": "Right", "fontSizePt": 8, "visible": True, "wordWrap": True,
            }]},
            {"id": "bm", "kind": "BottomMargin", "heightMm": 10, "controls": []},
        ],
        "paper": {"kind": "A4", "orientation": "Portrait", "widthMm": 210, "heightMm": 297},
        "schemaVersion": 1,
    }


def dump(path, obj):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(obj, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("wrote", path)


def form(caption, type_id, script_id, layout, meta_id, name, model_id, order=1):
    return {
        "kind": "PrintForm",
        "object": {
            "caption": caption,
            "documentTypeMetaId": type_id,
            "engine": "Xr",
            "scriptMetaId": script_id,
            "layoutJson": json.dumps(layout, separators=(",", ":")),
            "displayOrder": order,
            "metaId": meta_id,
            "name": name,
            "modelId": model_id,
        },
    }


CRM = "5f8a3c21-7b6d-4e9f-8a1c-2d5e6f3a9b40"
SALES = "47861dd2-1009-4926-9ea3-c506cae5118d"

lead = form(
    {"en": "Sales lead", "ar": "العميل المحتمل", "ru": "Лид", "uk": "Лід"},
    "64daae1f-09b3-47f7-b5b5-c5544a102d98",
    "c14a2f8d-d47f-4e8c-b739-1fa6372b012b",
    shell(
        "SALES LEAD",
        header_row(0, "No.", "Number", 12)
        + header_row(1, "Date", "DocumentDate", 18)
        + header_row(2, "Subject", "Notes", 24)
        + header_row(3, "Customer", "Customer", 30)
        + header_row(4, "Outlet", "Outlet", 36)
        + header_row(5, "Agent", "Seller", 42)
        + header_row(6, "Contact", "Item", 48)
        + header_row(7, "Notes", "LineNote", 54),
        62,
        [],
        [field("c-note", 0, 0.5, 180, 6, "LineNote", size=8)],
        [],
        8,
    ),
    "511e4b78-f47b-4951-8470-673a80ebf191",
    "SalesLeadXr",
    CRM,
)
dump(ROOT / "CRM/Documents/SalesLead/PrintForms/SalesLeadXr.printform.json", lead)

loy = form(
    {"en": "Loyalty redemption", "ar": "استرجاع الولاء", "ru": "Списание баллов", "uk": "Списання балів"},
    "2a5b0d98-4c3e-4f7a-9b8d-9e2f3a0b6c10",
    "a7ac41eb-418a-45b5-90d9-bb1b8fb03ebf",
    shell(
        "LOYALTY REDEMPTION",
        header_row(0, "No.", "Number", 12)
        + header_row(1, "Date", "DocumentDate", 18)
        + header_row(2, "Customer", "Customer", 24),
        32,
        [
            lbl("th-pts", 0, 1, 180, 6, "Points", size=8, bold=True, align="Right", borders="B"),
        ],
        [field("c-pts", 0, 0.5, 180, 6, "Quantity", size=8, align="Right", fmt="{0:n2}", borders="B")],
        [
            lbl("lbl-Total", 110, 2, 40, 6, "Points", size=11, bold=True, align="Right"),
            field("total", 150, 2, 30, 6, "Total", size=11, align="Right", fmt="{0:n2}", bold=True),
        ],
        12,
    ),
    "49c952c6-4f65-47c7-909b-f4714eb4531d",
    "LoyaltyRedemptionXr",
    CRM,
)
dump(ROOT / "CRM/Documents/LoyaltyRedemption/PrintForms/LoyaltyRedemptionXr.printform.json", loy)

route = form(
    {"en": "Visit route", "ar": "مسار الزيارات", "ru": "Маршрут визитов", "uk": "Маршрут візитів"},
    "8f3a1c20-6d4b-4e91-a2c7-1b9e0d5f4a31",
    "c147e05d-1d2d-414e-8399-58aae465dc94",
    shell(
        "VISIT ROUTE",
        header_row(0, "No.", "Number", 12)
        + header_row(1, "Date", "DocumentDate", 18)
        + header_row(2, "Agent", "Seller", 24)
        + header_row(3, "Template", "Contract", 30),
        38,
        [
            lbl("th-no", 0, 1, 12, 6, "#", size=8, bold=True, borders="B"),
            lbl("th-cust", 12, 1, 58, 6, "Customer", size=8, bold=True, borders="B"),
            lbl("th-out", 70, 1, 50, 6, "Outlet", size=8, bold=True, borders="B"),
            lbl("th-win", 120, 1, 32, 6, "Window", size=8, bold=True, borders="B"),
            lbl("th-st", 152, 1, 28, 6, "Status", size=8, bold=True, borders="B"),
        ],
        [
            field("c-no", 0, 0.5, 12, 6, "LineNo", size=8, borders="B"),
            field("c-cust", 12, 0.5, 58, 6, "Customer", size=8, borders="B"),
            field("c-out", 70, 0.5, 50, 6, "Outlet", size=8, borders="B"),
            field("c-win", 120, 0.5, 32, 6, "Item", size=8, borders="B"),
            field("c-st", 152, 0.5, 28, 6, "LineNote", size=8, borders="B"),
        ],
    ),
    "928245fa-0f73-44ea-bc33-b80b101d6fef",
    "VisitRouteXr",
    SALES,
)
dump(ROOT / "Sales/Documents/VisitRoute/PrintForms/VisitRouteXr.printform.json", route)

visit = form(
    {"en": "Agent visit", "ar": "زيارة الوكيل", "ru": "Визит агента", "uk": "Візит агента"},
    "9a4b2d32-7e5c-4f13-b3d8-2c0f1e7b5b43",
    "72194e53-c168-4fee-9ce6-4c81f10a73c3",
    shell(
        "AGENT VISIT",
        header_row(0, "No.", "Number", 12)
        + header_row(1, "Date", "DocumentDate", 18)
        + header_row(2, "Customer", "Customer", 24)
        + header_row(3, "Outlet", "Outlet", 30)
        + header_row(4, "Route", "Contract", 36)
        + header_row(5, "Check-in", "Item", 42)
        + header_row(6, "Skip", "Notes", 48)
        + header_row(7, "Geo", "LineParty", 54),
        62,
        [],
        [field("c-when", 0, 0.5, 180, 6, "Item", size=8)],
        [],
        8,
    ),
    "6f2b57f7-c5b5-4a8b-879d-c2b4fe2fe38f",
    "AgentVisitXr",
    SALES,
)
dump(ROOT / "Sales/Documents/AgentVisit/PrintForms/AgentVisitXr.printform.json", visit)
