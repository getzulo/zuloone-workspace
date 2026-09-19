import json
from pathlib import Path

ROOT = Path(r"d:\Sources\zuloone-workspace")
HR = "f61f0a5e-85ec-4b3e-81b4-7bb2e36186a8"


def lbl(cid, x, y, w, h, text, *, size=9, bold=False, align=None, borders=None):
    c = {"id": cid, "type": "XRLabel", "xMm": x, "yMm": y, "wMm": w, "hMm": h,
         "fontSizePt": size, "visible": True, "text": text}
    if bold: c["bold"] = True
    if align: c["hAlign"] = align
    if borders: c["borders"] = borders
    return c


def field(cid, x, y, w, h, binding, *, size=9, align=None, borders=None, fmt=None, bold=False):
    c = {"id": cid, "type": "XRLabel", "xMm": x, "yMm": y, "wMm": w, "hMm": h,
         "fontSizePt": size, "visible": True, "binding": binding, "text": f"[{binding}]", "wordWrap": True}
    if align: c["hAlign"] = align
    if borders: c["borders"] = borders
    if fmt: c["format"] = fmt
    if bold: c["bold"] = True
    return c


def header_row(idx, label, binding, y):
    return [lbl(f"lbl{idx}", 0, y, 34, 5, label, bold=True), field(f"f{idx}", 34, y, 146, 5, binding)]


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
                "pageInfo": "NumberOfTotal", "hAlign": "Right", "fontSizePt": 8, "visible": True, "wordWrap": True}]},
            {"id": "bm", "kind": "BottomMargin", "heightMm": 10, "controls": []},
        ],
        "paper": {"kind": "A4", "orientation": "Portrait", "widthMm": 210, "heightMm": 297},
        "schemaVersion": 1,
    }


def dump(path, caption, type_id, script_id, layout, meta_id, name):
    obj = {"kind": "PrintForm", "object": {
        "caption": caption, "documentTypeMetaId": type_id, "engine": "Xr",
        "scriptMetaId": script_id, "layoutJson": json.dumps(layout, separators=(",", ":")),
        "displayOrder": 1, "metaId": meta_id, "name": name, "modelId": HR}}
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(obj, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("wrote", path)


ts = shell(
    "TIME SHEET",
    header_row(0, "No.", "Number", 12) + header_row(1, "Date", "DocumentDate", 18)
    + header_row(2, "Division", "Seller", 24) + header_row(3, "Period", "Notes", 30),
    38,
    [lbl("th-no", 0, 1, 12, 6, "#", size=8, bold=True, borders="B"),
     lbl("th-emp", 12, 1, 140, 6, "Employee", size=8, bold=True, borders="B"),
     lbl("th-hrs", 152, 1, 28, 6, "Hours", size=8, bold=True, align="Right", borders="B")],
    [field("c-no", 0, 0.5, 12, 6, "LineNo", size=8, borders="B"),
     field("c-emp", 12, 0.5, 140, 6, "LineParty", size=8, borders="B"),
     field("c-hrs", 152, 0.5, 28, 6, "Quantity", size=8, align="Right", fmt="{0:n2}", borders="B")],
    [lbl("lbl-Total", 110, 2, 40, 6, "Hours", size=11, bold=True, align="Right"),
     field("total", 150, 2, 30, 6, "Total", size=11, align="Right", fmt="{0:n2}", bold=True)],
)
dump(ROOT / "HR/Documents/TimeSheet/PrintForms/TimeSheetXr.printform.json",
     {"en": "Time sheet", "ar": "سجل الدوام", "ru": "Табель", "uk": "Табель"},
     "0f1a2b3c-4d5e-4f6a-8b7c-9d0e1f2a3b4f", "8abbdac3-bcd4-4247-85c9-5dae96712e3a",
     ts, "6e8511bc-9407-4818-8259-bf2e453d4f03", "TimeSheetXr")

si_ph = [
    lbl("th-no", 0, 1, 10, 6, "#", size=8, bold=True, borders="B"),
    lbl("th-emp", 10, 1, 62, 6, "Employee", size=8, bold=True, borders="B"),
    lbl("th-base", 72, 1, 28, 6, "Base", size=8, bold=True, align="Right", borders="B"),
    lbl("th-ee", 100, 1, 26, 6, "Employee", size=8, bold=True, align="Right", borders="B"),
    lbl("th-er", 126, 1, 26, 6, "Employer", size=8, bold=True, align="Right", borders="B"),
    lbl("th-amt", 152, 1, 28, 6, "Total", size=8, bold=True, align="Right", borders="B"),
]
si_det = [
    field("c-no", 0, 0.5, 10, 6, "LineNo", size=8, borders="B"),
    field("c-emp", 10, 0.5, 62, 6, "LineParty", size=8, borders="B"),
    field("c-base", 72, 0.5, 28, 6, "Quantity", size=8, align="Right", fmt="{0:n2}", borders="B"),
    field("c-ee", 100, 0.5, 26, 6, "Debit", size=8, align="Right", fmt="{0:n2}", borders="B"),
    field("c-er", 126, 0.5, 26, 6, "Credit", size=8, align="Right", fmt="{0:n2}", borders="B"),
    field("c-amt", 152, 0.5, 28, 6, "Amount", size=8, align="Right", fmt="{0:n2}", borders="B"),
]
si_foot = [
    lbl("lbl-Total", 110, 2, 40, 6, "Total", size=11, bold=True, align="Right"),
    field("total", 150, 2, 30, 6, "Total", size=11, align="Right", fmt="{0:n2}", bold=True),
]
header_div = header_row(0, "No.", "Number", 12) + header_row(1, "Date", "DocumentDate", 18) + header_row(2, "Division", "Seller", 24)

acc = shell("SOCIAL INSURANCE ACCRUAL", header_div, 32, si_ph, si_det, si_foot)
dump(ROOT / "HR/Documents/SocialInsuranceAccrual/PrintForms/SocialInsuranceAccrualXr.printform.json",
     {"en": "Social insurance accrual", "ar": "استحقاق التأمينات", "ru": "Начисление соцстраха", "uk": "Нарахування соцстраху"},
     "a0d03063-af77-4fd0-886b-223a9731f105", "b7b04b57-e261-49d5-b4a7-91c52a4cb281",
     acc, "c889b698-f06f-48b0-ad4d-bb91d0761c1a", "SocialInsuranceAccrualXr")

pay_ph = [
    lbl("th-no", 0, 1, 12, 6, "#", size=8, bold=True, borders="B"),
    lbl("th-emp", 12, 1, 88, 6, "Employee", size=8, bold=True, borders="B"),
    lbl("th-ee", 100, 1, 26, 6, "Employee", size=8, bold=True, align="Right", borders="B"),
    lbl("th-er", 126, 1, 26, 6, "Employer", size=8, bold=True, align="Right", borders="B"),
    lbl("th-amt", 152, 1, 28, 6, "Total", size=8, bold=True, align="Right", borders="B"),
]
pay_det = [
    field("c-no", 0, 0.5, 12, 6, "LineNo", size=8, borders="B"),
    field("c-emp", 12, 0.5, 88, 6, "LineParty", size=8, borders="B"),
    field("c-ee", 100, 0.5, 26, 6, "Debit", size=8, align="Right", fmt="{0:n2}", borders="B"),
    field("c-er", 126, 0.5, 26, 6, "Credit", size=8, align="Right", fmt="{0:n2}", borders="B"),
    field("c-amt", 152, 0.5, 28, 6, "Amount", size=8, align="Right", fmt="{0:n2}", borders="B"),
]
pay = shell("SOCIAL INSURANCE PAYMENT", header_div, 32, pay_ph, pay_det, si_foot)
dump(ROOT / "HR/Documents/SocialInsurancePayment/PrintForms/SocialInsurancePaymentXr.printform.json",
     {"en": "Social insurance payment", "ar": "سداد التأمينات", "ru": "Платёж в фонд", "uk": "Платіж у фонд"},
     "aa4abe6c-0f27-42f0-9284-5f084e6b7274", "846f22ae-0192-427f-9ed0-d82b9fedde82",
     pay, "87e08fd0-d7e1-4587-8a30-5627ecdced3a", "SocialInsurancePaymentXr")
