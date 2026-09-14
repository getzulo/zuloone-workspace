#!/usr/bin/env bash
set -euo pipefail
cd /d/Sources/zuloone-workspace
git add \
  .claude/skills/zuloone-new-dictionary/SKILL.md \
  Common/DataPackages \
  Common/Dictionaries/City/City.object.json \
  Common/Dictionaries/Country/Country.object.json \
  Common/Dictionaries/Currency/Currency.object.json \
  Common/Dictionaries/Gender/Gender.object.json \
  Common/Dictionaries/PaymentTerm/PaymentTerm.object.json \
  Common/Dictionaries/Region/Region.object.json \
  Common/Dictionaries/UnitClass/UnitClass.object.json \
  Inventory/DataPackages \
  Inventory/Dictionaries/PriceType/PriceType.object.json \
  Inventory/Dictionaries/StoreCellType/StoreCellType.object.json \
  Organization/DataPackages \
  Organization/Dictionaries/DivisionType/DivisionType.object.json \
  LocalizationSaudiArabia/DataPackages \
  Tax/DataPackages \
  Tax/Dictionaries/Tax/Tax.object.json \
  Tax/Dictionaries/TaxAuthority/TaxAuthority.object.json \
  Tax/Dictionaries/TaxCategory/TaxCategory.object.json \
  Tax/Dictionaries/TaxCode/TaxCode.object.json \
  Tax/Dictionaries/TaxDirection/TaxDirection.object.json \
  Tax/Dictionaries/TaxJurisdiction/TaxJurisdiction.object.json
git commit -m "$(cat <<'EOF'
feat: seed classifiers and split Saudi VAT from common tax

Name is translatable on catalog dictionaries; country packs stay in their
models so vat-SA only adds ZATCA, SA rates, and S/Z/E/O codes.
EOF
)"
git status -sb
