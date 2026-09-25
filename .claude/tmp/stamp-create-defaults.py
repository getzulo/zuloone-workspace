# One-shot: seed OnBeforeCreate from IRecordDefaults. Idempotent via marker.
import re
from pathlib import Path

ROOT = Path(r"d:\Sources\zuloone-workspace")
MARKER = "// RecordDefaults"

# path relative to workspace -> (metadata object name, fields)
# guid fields and date fields are distinguished by name.
DATES = {
    "EffectiveFrom", "DateFrom", "HireDate", "CountDate",
    "DeliveryDate", "TaxPointDate", "PeriodFrom", "DueDate",
}

TARGETS = {
    "Purchasing/Documents/PurchaseOrder/Events/PurchaseOrderEventHandler.cs": ("PurchaseOrder", ["LegalEntity", "PaymentTerm", "DueDate"]),
    "Purchasing/Documents/PurchaseCreditNote/Events/PurchaseCreditNoteEventHandler.cs": ("PurchaseCreditNote", ["LegalEntity"]),
    "Purchasing/Documents/VendorPayment/Events/VendorPaymentEventHandler.cs": ("VendorPayment", ["LegalEntity"]),
    "Accounting/Documents/JournalEntry/Events/JournalEntryEventHandler.cs": ("JournalEntry", ["Currency", "LegalEntity"]),
    "Sales/Documents/DeliveryTrip/Events/DeliveryTripEventHandler.cs": ("DeliveryTrip", ["DeliveryDate"]),
    "CRM/Documents/SalesQuotation/Events/SalesQuotationEventHandler.cs": ("SalesQuotation", ["DeliveryDate", "PaymentTerm"]),
    "CRM/Documents/SalesLead/Events/SalesLeadEventHandler.cs": ("SalesLead", ["DeliveryDate"]),
    "Sales/Documents/SalesDebitNote/Events/SalesDebitNoteEventHandler.cs": ("SalesDebitNote", ["LegalEntity"]),
    "Sales/Documents/SalesRealization/Events/SalesInvoiceEventHandler.cs": ("SalesRealization", ["LegalEntity", "PaymentTerm", "DueDate"]),
    "Sales/Documents/CustomerPayment/Events/CustomerPaymentEventHandler.cs": ("CustomerPayment", ["LegalEntity"]),
    "Tax/Documents/TaxCalculation/Events/TaxCalculationEventHandler.cs": ("TaxCalculation", ["Currency", "LegalEntity", "TaxPointDate"]),
    "Tax/Documents/TaxReturn/Events/TaxReturnEventHandler.cs": ("TaxReturn", ["LegalEntity", "PeriodFrom"]),
    "Inventory/Documents/StockCount/Events/StockCountEventHandler.cs": ("StockCount", ["CountDate"]),
    "Sales/Documents/SalesCreditNote/Events/SalesCreditNoteEventHandler.cs": ("SalesCreditNote", ["LegalEntity"]),
    "Tax/Documents/TaxDocument/Events/TaxDocumentEventHandler.cs": ("TaxDocument", ["LegalEntity"]),
    "Tax/Documents/TaxPayment/Events/TaxPaymentEventHandler.cs": ("TaxPayment", ["LegalEntity"]),
    "Sales/Documents/SalesOrder/Events/SalesOrderEventHandler.cs": ("SalesOrder", ["DeliveryDate", "PaymentTerm"]),
    "LocalizationUkraine/Documents/UaTaxRemittance/Events/UaTaxRemittanceEventHandler.cs": ("UaTaxRemittance", ["LegalEntity"]),
    "LocalizationUkraine/Documents/UaVatRefund/Events/UaVatRefundEventHandler.cs": ("UaVatRefund", ["LegalEntity"]),
    "LocalizationUkraine/Documents/UaFopEsvAccrual/Events/UaFopEsvAccrualEventHandler.cs": ("UaFopEsvAccrual", ["LegalEntity"]),
    "Organization/Dictionaries/LegalEntity/Events/LegalEntityEventHandler.cs": ("LegalEntity", ["Currency"]),
    "Common/Dictionaries/Country/Events/CountryEventHandler.cs": ("Country", ["Currency"]),
    "Tax/Dictionaries/TaxRate/Events/TaxRateEventHandler.cs": ("TaxRate", ["EffectiveFrom"]),
    "Tax/Dictionaries/TaxReportMapping/Events/TaxReportMappingEventHandler.cs": ("TaxReportMapping", ["EffectiveFrom"]),
    "CRM/Dictionaries/LoyaltyCampaign/Events/LoyaltyCampaignEventHandler.cs": ("LoyaltyCampaign", ["EffectiveFrom"]),
    "Tax/Dictionaries/TaxProfile/Events/TaxProfileEventHandler.cs": ("TaxProfile", ["EffectiveFrom"]),
    "Inventory/Dictionaries/PriceListItem/Events/PriceListItemEventHandler.cs": ("PriceListItem", ["EffectiveFrom"]),
    "Costing/Dictionaries/StandardCost/Events/StandardCostEventHandler.cs": ("StandardCost", ["EffectiveFrom"]),
    "Tax/Dictionaries/TaxCode/Events/TaxCodeEventHandler.cs": ("TaxCode", ["EffectiveFrom"]),
    "Tax/Dictionaries/TaxRule/Events/TaxRuleEventHandler.cs": ("TaxRule", ["EffectiveFrom"]),
    "Tax/Dictionaries/Tax/Events/TaxEventHandler.cs": ("Tax", ["EffectiveFrom"]),
    "Tax/Dictionaries/TaxExemptionReason/Events/TaxExemptionReasonEventHandler.cs": ("TaxExemptionReason", ["EffectiveFrom"]),
    "Tax/Dictionaries/TaxMapping/Events/TaxMappingEventHandler.cs": ("TaxMapping", ["EffectiveFrom"]),
    "LocalizationUkraine/Dictionaries/UaRegimeChange/Events/UaRegimeChangeEventHandler.cs": ("UaRegimeChange", ["EffectiveFrom", "LegalEntity"]),
    "LocalizationUkraine/Dictionaries/UaSingleTaxLimit/Events/UaSingleTaxLimitEventHandler.cs": ("UaSingleTaxLimit", ["EffectiveFrom"]),
    "Common/Dictionaries/ExchangeRate/Events/ExchangeRateEventHandler.cs": ("ExchangeRate", ["Currency", "EffectiveFrom"]),
    "Sales/Dictionaries/SalesContract/Events/SalesContractEventHandler.cs": ("SalesContract", ["Currency", "EffectiveFrom", "LegalEntity", "PaymentTerm"]),
    "HR/Dictionaries/TimeOff/Events/TimeOffEventHandler.cs": ("TimeOff", ["DateFrom"]),
    "Inventory/Dictionaries/Customer/Events/CustomerEventHandler.cs": ("Customer", ["PaymentTerm"]),
    "Accounting/Dictionaries/ChartOfAccounts/Events/ChartOfAccountsEventHandler.cs": ("ChartOfAccounts", ["Currency"]),
    "Organization/Dictionaries/Division/Events/DivisionEventHandler.cs": ("Division", ["LegalEntity"]),
    "HR/Dictionaries/Employee/Events/EmployeeEventHandler.cs": ("Employee", ["HireDate"]),
    "Tax/Dictionaries/TaxAuthorityConnection/Events/TaxAuthorityConnectionEventHandler.cs": ("TaxAuthorityConnection", ["LegalEntity"]),
    "Tax/Dictionaries/TaxPeriod/Events/TaxPeriodEventHandler.cs": ("TaxPeriod", ["LegalEntity"]),
    "LocalizationUkraine/Dictionaries/UaLandPlot/Events/UaLandPlotEventHandler.cs": ("UaLandPlot", ["LegalEntity"]),
    "LocalizationUkraine/Dictionaries/UaBankConnection/Events/UaBankConnectionEventHandler.cs": ("UaBankConnection", ["LegalEntity"]),
    "LocalizationUkraine/Dictionaries/UaTaxFilingExport/Events/UaTaxFilingExportEventHandler.cs": ("UaTaxFilingExport", ["LegalEntity", "PeriodFrom"]),
    "Tax/Dictionaries/TaxSubmission/Events/TaxSubmissionEventHandler.cs": ("TaxSubmission", ["LegalEntity"]),
}

VERSIONS = {
    "Purchasing/model.json": "1.14.1",
    "Accounting/model.json": "1.11.1",
    "Sales/model.json": "1.53.3",
    "CRM/model.json": "1.11.1",
    "Tax/model.json": "1.23.2",
    "Inventory/model.json": "1.18.1",
    "HR/model.json": "1.10.1",
    "Organization/model.json": "1.6.1",
    "LocalizationUkraine/model.json": "1.42.2",
    "Costing/model.json": "1.6.1",
    "Common/model.json": None,  # already 1.10.0
}


def stamp_body(var: str, object_name: str, fields: list[str]) -> str:
    lines = [
        f"        {MARKER}: валюта, юрлицо, ячейка, срок и даты начала — из настроек, пока поле пустое.",
        "        var createDefaults = context.GetService<IRecordDefaults>();",
        f'        var createSeed = await createDefaults.SeedAsync("{object_name}");',
    ]
    for field in fields:
        if field in DATES:
            lines += [
                f"        if ({var}.{field}.Year < 1902)",
                "        {",
                f'            var createDay = createDefaults.PickDay(createSeed, "{field}");',
                f"            if (createDay.Year >= 1902) {var}.{field} = createDay;",
                "        }",
            ]
        else:
            lines += [
                f"        if ({var}.{field} == Guid.Empty)",
                "        {",
                f'            var createId = createDefaults.Pick(createSeed, "{field}");',
                f"            if (createId != Guid.Empty) {var}.{field} = createId;",
                "        }",
            ]
    return "\n".join(lines) + "\n"


def ensure_using(text: str) -> str:
    if "ZuloOne.Services.Contracts" in text:
        return text
    if "using " in text:
        lines = text.splitlines(keepends=True)
        last = 0
        for i, line in enumerate(lines):
            if line.startswith("using "):
                last = i
        lines.insert(last + 1, "using ZuloOne.Services.Contracts;\n")
        return "".join(lines)
    return "using ZuloOne.Services.Contracts;\n" + text


def ensure_system(text: str) -> str:
    if re.search(r"^using System;\s*$", text, re.M):
        return text
    return "using System;\n" + text


def var_name(text: str, object_name: str) -> str:
    m = re.search(r"OnBefore(?:Save|Create)Async\(\s*\w+\s+(\w+)\s*,", text)
    if m:
        return m.group(1)
    if f"TypedDocumentEventHandler<{object_name}>" in text or "TypedDocumentEventHandler<" in text:
        return "header"
    return "record"


def patch(text: str, object_name: str, fields: list[str]) -> str:
    if MARKER in text:
        return text
    var = var_name(text, object_name)
    body = stamp_body(var, object_name, fields)
    type_m = re.search(rf"OnBeforeCreateAsync\(\s*(\w+)\s+{var}\s*,", text)
    type_name = type_m.group(1) if type_m else object_name

    expr = re.compile(
        rf"(    public override Task<EventResult> OnBeforeCreateAsync\({type_name} {var}, EventContext context\)\s*\r?\n        => next\({var}, context\);)"
    )
    m = expr.search(text)
    if m:
        block = (
            f"    public override async Task<EventResult> OnBeforeCreateAsync({type_name} {var}, EventContext context)\n"
            "    {\n"
            f"        var prior = await next({var}, context);\n"
            "        if (!prior.Success) return prior;\n"
            f"{body}"
            "        return EventResult.Ok();\n"
            "    }"
        )
        return ensure_system(ensure_using(text[: m.start()] + block + text[m.end() :]))

    # Existing block: insert after the success check, or after the opening brace.
    method = re.search(
        rf"public override async Task<EventResult> OnBeforeCreateAsync\({type_name} {var}, EventContext context\)\s*\{{",
        text,
    )
    if method:
        check = re.search(
            r"if \(!prior\.Success\) return prior;\s*\r?\n",
            text[method.end() :],
        )
        if check:
            at = method.end() + check.end()
        else:
            at = method.end()
            if text[at : at + 1] == "\n":
                at += 1
        return ensure_system(ensure_using(text[:at] + body + text[at:]))

    # No OnBeforeCreate: insert after the class opening brace.
    cls = re.search(r"public partial class \w+[^{]*\{", text)
    if not cls:
        raise SystemExit(f"no class for {object_name}")
    # Parameter type from the class base if we guessed wrong.
    base = re.search(r"Typed(?:Document|Dictionary)EventHandler<(\w+)>", text)
    type_name = base.group(1) if base else object_name
    if "TypedDocumentEventHandler" in text:
        var = "header"
    elif "TypedDictionaryEventHandler" in text:
        var = "record"
    body = stamp_body(var, object_name, fields)
    block = (
        f"\n    public override async Task<EventResult> OnBeforeCreateAsync({type_name} {var}, EventContext context)\n"
        "    {\n"
        f"        var prior = await next({var}, context);\n"
        "        if (!prior.Success) return prior;\n"
        f"{body}"
        "        return EventResult.Ok();\n"
        "    }\n"
    )
    at = cls.end()
    return ensure_system(ensure_using(text[:at] + block + text[at:]))


def bump_version(path: Path, version: str) -> None:
    text = path.read_text(encoding="utf-8")
    new, n = re.subn(r'"modelVersion": "[^"]+"', f'"modelVersion": "{version}"', text, count=1)
    if n != 1:
        raise SystemExit(f"version bump failed {path}")
    path.write_text(new, encoding="utf-8")


def bump_common_min(path: Path) -> None:
    text = path.read_text(encoding="utf-8")
    needle = '"dependsOnModelMetaId": "ee2cf537-a2c6-459e-9041-f221de8439c8"'
    i = text.find(needle)
    if i < 0:
        return
    window = text[i : i + 400]
    new_window, n = re.subn(r'"minVersion": "[^"]+"', '"minVersion": "1.10.0"', window, count=1)
    if n != 1:
        raise SystemExit(f"minVersion failed {path}")
    path.write_text(text[:i] + new_window + text[i + 400 :], encoding="utf-8")


def main() -> None:
    for rel, (name, fields) in TARGETS.items():
        path = ROOT / rel
        if not path.exists():
            print("MISSING", rel)
            continue
        original = path.read_text(encoding="utf-8")
        updated = patch(original, name, fields)
        if updated != original:
            path.write_text(updated, encoding="utf-8")
            print("patched", rel)
        else:
            print("unchanged", rel)
    for rel, version in VERSIONS.items():
        path = ROOT / rel
        if version:
            bump_version(path, version)
        bump_common_min(path)
        print("version", rel)


if __name__ == "__main__":
    main()
