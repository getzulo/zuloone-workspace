param(
    [datetime]$AsOfUtc = [datetime]::SpecifyKind([datetime]'2026-09-24', [DateTimeKind]::Utc),
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\DemoData\DataPackages\showcase.json')
)

$ErrorActionPreference = 'Stop'

function New-Ref([string]$Entity, $Key) {
    [ordered]@{ '$ref' = $Entity; key = $Key }
}

function New-Entity([string]$Name, [string[]]$BusinessKey, [int]$RowCount, [string]$Kind = 'Dictionary') {
    [ordered]@{
        entity = $Name
        table = $Name
        kind = $Kind
        businessKey = $BusinessKey
        mode = 'insert'
        rowCount = $RowCount
    }
}

$legalEntityName = 'Northstar Trading Company'
$divisionName = 'Riyadh Distribution'
$storeName = 'Riyadh Central Warehouse'

$legalEntityRef = New-Ref 'LegalEntity' ([ordered]@{ RegistrationNumber = '1010999999' })
$divisionRef = New-Ref 'Division' ([ordered]@{ LegalEntity = $legalEntityRef; Name = $divisionName })
$storeRef = New-Ref 'Store' ([ordered]@{ Division = $divisionRef; Name = $storeName })
$currencyRef = New-Ref 'Currency' ([ordered]@{ Code = 'SAR' })
$unitRef = New-Ref 'UnitOfMeasure' ([ordered]@{ Code = 'PCS' })
$zatcaRef = New-Ref 'TaxAuthority' ([ordered]@{ Code = 'ZATCA' })
$saTaxJurisdictionRef = New-Ref 'TaxJurisdiction' ([ordered]@{ Code = 'SA' })
$vatSaRef = New-Ref 'Tax' ([ordered]@{ Code = 'VAT-SA' })
$standardVatCategoryRef = New-Ref 'TaxCategory' ([ordered]@{ Tax = $vatSaRef; Code = 'STANDARD' })
$vat15Ref = New-Ref 'TaxRate' ([ordered]@{ Tax = $vatSaRef; Code = 'VAT15' })

$zones = @(
    [ordered]@{ Name = 'Receiving'; IsBarcodeTracking = $true },
    [ordered]@{ Name = 'Reserve storage'; IsBarcodeTracking = $true },
    [ordered]@{ Name = 'Picking'; IsBarcodeTracking = $true }
)

$storeZones = @()
$storeCells = @()
$receivingCellRefs = @()
$reserveCellRefs = @()
$pickingCellRefs = @()
foreach ($zone in $zones) {
    $zoneRef = New-Ref 'StoreZone' ([ordered]@{ Store = $storeRef; Name = $zone.Name })
    $storeZones += [ordered]@{
        Name = $zone.Name
        Store = $storeRef
        IsBarcodeTracking = $zone.IsBarcodeTracking
    }

    $cellCount = if ($zone.Name -eq 'Receiving') { 4 } else { 10 }
    $typeCode = if ($zone.Name -eq 'Picking') { 'PICKING' } elseif ($zone.Name -eq 'Receiving') { 'RECEIVING' } else { 'STORAGE' }
    $prefix = if ($zone.Name -eq 'Picking') { 'P' } elseif ($zone.Name -eq 'Receiving') { 'R' } else { 'A' }

    for ($i = 0; $i -lt $cellCount; $i++) {
        $line = [int](1 + [math]::Floor($i / 5))
        $rack = [int](1 + ($i % 5))
        $shelf = [int](1 + (($i * 2) % 3))
        $cell = [int](1 + ($i % 2))
        $cellName = ('{0}-{1:D2}-{2:D2}-{3:D2}' -f $prefix, $line, $rack, $cell)
        $cellRef = New-Ref 'StoreCell' ([ordered]@{ StoreZone = $zoneRef; Name = $cellName })

        $storeCells += [ordered]@{
            Name = $cellName
            StoreZone = $zoneRef
            Type = New-Ref 'StoreCellType' ([ordered]@{ Code = $typeCode })
            LineNumber = $line
            RackNumber = $rack
            ShelfNumber = $shelf
            CellNumber = $cell
            IsPickable = $zone.Name -eq 'Picking'
            MaxQty = if ($zone.Name -eq 'Receiving') { 5000 } elseif ($zone.Name -eq 'Picking') { 2500 } else { 15000 }
        }

        if ($zone.Name -eq 'Receiving') {
            $receivingCellRefs += $cellRef
        }
        elseif ($zone.Name -eq 'Picking') {
            $pickingCellRefs += $cellRef
        }
        else {
            $reserveCellRefs += $cellRef
        }
    }
}

$groups = @(
    [ordered]@{ Code = 'BEV'; Name = 'Beverages'; Name_ru = 'Напитки' },
    [ordered]@{ Code = 'SNK'; Name = 'Snacks'; Name_ru = 'Снеки' },
    [ordered]@{ Code = 'HOM'; Name = 'Home care'; Name_ru = 'Для дома' },
    [ordered]@{ Code = 'PER'; Name = 'Personal care'; Name_ru = 'Личная гигиена' },
    [ordered]@{ Code = 'OFF'; Name = 'Office supplies'; Name_ru = 'Офисные товары' },
    [ordered]@{ Code = 'ELE'; Name = 'Small electronics'; Name_ru = 'Мелкая электроника' }
)

$itemNouns = @(
    'Mineral Water 330 ml', 'Mineral Water 1.5 l', 'Sparkling Water', 'Orange Juice', 'Apple Juice',
    'Cola', 'Diet Cola', 'Energy Drink', 'Black Tea', 'Ground Coffee',
    'Potato Chips', 'Corn Chips', 'Salted Nuts', 'Mixed Nuts', 'Chocolate Bar',
    'Oat Cookies', 'Crackers', 'Protein Bar', 'Dried Fruit', 'Popcorn',
    'Dish Soap', 'Laundry Gel', 'Surface Cleaner', 'Glass Cleaner', 'Paper Towels',
    'Trash Bags', 'Sponges', 'Air Freshener', 'Hand Soap', 'Tissues',
    'Shampoo', 'Conditioner', 'Toothpaste', 'Toothbrush', 'Body Wash',
    'Hand Cream', 'Deodorant', 'Wet Wipes', 'Cotton Pads', 'Sanitizer',
    'A4 Paper', 'Notebook', 'Ballpoint Pens', 'Permanent Markers', 'Sticky Notes',
    'Packing Tape', 'Envelopes', 'Stapler', 'Folders', 'Shipping Labels',
    'USB-C Cable', 'Phone Charger', 'Power Bank', 'Wireless Mouse', 'Keyboard',
    'LED Desk Lamp', 'Extension Cord', 'HDMI Cable', 'USB Hub', 'Bluetooth Speaker'
)

$items = @()
for ($i = 0; $i -lt $itemNouns.Count; $i++) {
    $group = $groups[[math]::Floor($i / 10)]
    $purchase = [math]::Round(4.50 + ($i * 1.37), 2)
    $sale = [math]::Round($purchase * (1.35 + (($i % 4) * 0.05)), 2)
    $items += [ordered]@{
        Name = $itemNouns[$i]
        Barcode = ('628100{0:D7}' -f ($i + 1))
        ItemGroup = New-Ref 'ItemGroup' ([ordered]@{ Code = $group.Code })
        UnitOfMeasure = $unitRef
        DefaultPurchasePrice = $purchase
        DefaultSalePrice = $sale
        IsRawMaterial = $false
        IsSellable = $true
        Description = 'Fictional product for the ZuloOne public showcase.'
    }
}

$customerNames = @(
    'Riyadh Corner Market', 'Palm District Grocery', 'North Gate Supplies', 'Olaya Daily Mart',
    'Desert Bloom Retail', 'Crescent Family Store', 'Kingdom Office Hub', 'Wadi Fresh Market',
    'Sunrise Mini Market', 'Al Noor Convenience', 'Blue Dune Trading', 'Capital Pantry',
    'Green Basket Retail', 'Red Sea Office Supply', 'Najd Home Store', 'Pearl Coast Market',
    'Oasis Essentials', 'Metro Business Supplies'
)
$customers = @()
$outlets = @()
$contracts = @()
for ($i = 0; $i -lt $customerNames.Count; $i++) {
    $name = $customerNames[$i]
    $customerRef = New-Ref 'Customer' ([ordered]@{ Name = $name })
    $outletName = "$name Main Outlet"
    $outletRef = New-Ref 'CustomerOutlet' ([ordered]@{ Customer = $customerRef; Name = $outletName })
    $customers += [ordered]@{
        Name = $name
        CustomerType = if (($i % 3) -eq 0) { 'B2B' } else { 'Retail' }
        Email = ('buyer{0:D2}@example.test' -f ($i + 1))
        Phone = ('+96655000{0:D4}' -f ($i + 1))
        Address = "Riyadh, demo district $($i + 1)"
        CreditLimit = 25000 + ($i * 5000)
        TaxRegistrationNumber = ('3100000000{0:D4}3' -f ($i + 1))
    }
    $outlets += [ordered]@{
        Customer = $customerRef
        Name = $outletName
        Address = "Riyadh, demo district $($i + 1)"
        Phone = ('+96655000{0:D4}' -f ($i + 1))
        IsDisabled = $false
    }
    $contracts += [ordered]@{
        Name = "$name Standard Contract"
        Customer = $customerRef
        Outlet = $outletRef
        Currency = $currencyRef
        LegalEntity = $legalEntityRef
        SettlementKind = 1
        EffectiveFrom = $AsOfUtc.AddYears(-2).ToString("yyyy-MM-ddTHH:mm:ss'Z'")
        CreditLimit = 25000 + ($i * 5000)
        IsDisabled = $false
    }
}

$supplierNames = @(
    'Gulf Beverage Wholesale', 'Arabian Foods Distribution', 'Riyadh Home Products',
    'Careline Consumer Goods', 'Peninsula Office Trade', 'Digital Oasis Wholesale'
)
$suppliers = @()
for ($i = 0; $i -lt $supplierNames.Count; $i++) {
    $suppliers += [ordered]@{
        Name = $supplierNames[$i]
        Email = ('sales{0:D2}@supplier.example.test' -f ($i + 1))
        Phone = ('+96656000{0:D4}' -f ($i + 1))
        TaxRegistrationNumber = ('3101111111{0:D4}3' -f ($i + 1))
    }
}

$stockAdjustments = @()
for ($doc = 0; $doc -lt 3; $doc++) {
    $stockLines = @()
    for ($i = $doc; $i -lt $items.Count; $i += 3) {
        $stockLines += [ordered]@{
            Item = New-Ref 'Item' ([ordered]@{ Name = $items[$i].Name })
            Quantity = 80 + (($i * 17) % 160)
            Unit = $unitRef
        }
    }
    $stockAdjustments += [ordered]@{
        DocumentDate = $AsOfUtc.AddDays(-370 + $doc).ToString("yyyy-MM-ddTHH:mm:ss'Z'")
        Cell = $reserveCellRefs[$doc]
        Reason = 'Opening balance for the public showcase'
        '$tableParts' = [ordered]@{ Lines = $stockLines }
    }
}

$purchaseOrders = @()
for ($i = 0; $i -lt 40; $i++) {
    $supplier = $supplierNames[$i % $supplierNames.Count]
    $date = $AsOfUtc.AddDays(-360 + ($i * 9)).AddHours(7 + ($i % 5))
    $lines = @()
    for ($line = 0; $line -lt 4; $line++) {
        $itemIndex = (($i * 7) + ($line * 11)) % $items.Count
        $lines += [ordered]@{
            Item = New-Ref 'Item' ([ordered]@{ Name = $items[$itemIndex].Name })
            Quantity = 12 + (($i + $line * 3) % 36)
            UnitPrice = $items[$itemIndex].DefaultPurchasePrice
            Unit = $unitRef
        }
    }
    $purchaseOrders += [ordered]@{
        DocumentDate = $date.ToString("yyyy-MM-ddTHH:mm:ss'Z'")
        DueDate = $date.AddDays(14).ToString("yyyy-MM-ddTHH:mm:ss'Z'")
        Supplier = New-Ref 'Supplier' ([ordered]@{ Name = $supplier })
        Location = $receivingCellRefs[$i % $receivingCellRefs.Count]
        LegalEntity = $legalEntityRef
        NonRecoverableVat = 0
        '$tableParts' = [ordered]@{ Lines = $lines }
    }
}

$sales = @()
for ($i = 0; $i -lt 200; $i++) {
    $customer = $customerNames[$i % $customerNames.Count]
    $customerRef = New-Ref 'Customer' ([ordered]@{ Name = $customer })
    $outletRef = New-Ref 'CustomerOutlet' ([ordered]@{ Customer = $customerRef; Name = "$customer Main Outlet" })
    $contractRef = New-Ref 'SalesContract' ([ordered]@{ Customer = $customerRef; Name = "$customer Standard Contract" })
    $date = $AsOfUtc.AddDays(-360 + [math]::Floor($i * 1.8)).AddHours(8 + ($i % 9)).AddMinutes($i % 60)
    $lines = @()
    $lineCount = 2 + ($i % 3)
    for ($line = 0; $line -lt $lineCount; $line++) {
        $itemIndex = (($i * 13) + ($line * 17)) % $items.Count
        $lines += [ordered]@{
            Item = New-Ref 'Item' ([ordered]@{ Name = $items[$itemIndex].Name })
            Quantity = 1 + (($i + $line * 2) % 8)
            UnitPrice = $items[$itemIndex].DefaultSalePrice
            Unit = $unitRef
            PriceExplanation = 'Demo catalogue price'
        }
    }
    $sales += [ordered]@{
        DocumentDate = $date.ToString("yyyy-MM-ddTHH:mm:ss'Z'")
        DueDate = $date.AddDays(14).ToString("yyyy-MM-ddTHH:mm:ss'Z'")
        Customer = $customerRef
        Outlet = $outletRef
        Contract = $contractRef
        Location = $pickingCellRefs[$i % $pickingCellRefs.Count]
        LegalEntity = $legalEntityRef
        TaxRateApplied = 0.15
        Notes = ('SHOWCASE-SALE-{0:D4}' -f ($i + 1))
        DiscountPercent = if (($i % 10) -eq 0) { 5 } else { 0 }
        '$tableParts' = [ordered]@{ Lines = $lines }
    }
}

$data = [ordered]@{
    LegalEntity = @([ordered]@{
        Name = $legalEntityName
        RegistrationNumber = '1010999999'
        TaxRegistrationNumber = '310123456700003'
        Country = New-Ref 'Country' ([ordered]@{ CodeISO2 = 'SA' })
        Currency = $currencyRef
        LegalAddress = 'King Fahd Road, Riyadh, Saudi Arabia'
    })
    Division = @([ordered]@{
        Name = $divisionName
        LegalEntity = $legalEntityRef
        DivisionType = New-Ref 'DivisionType' ([ordered]@{ Code = 'WAREHOUSE' })
    })
    Store = @([ordered]@{ Name = $storeName; Division = $divisionRef; IsSimple = $false })
    StoreZone = $storeZones
    StoreCell = $storeCells
    TaxAuthority = @([ordered]@{
        Code = 'ZATCA'
        Name = 'Zakat, Tax and Customs Authority'
        CountryCode = 'SA'
        IsActive = $true
        Name_ru = 'Управление закята, налогов и таможни'
        Name_ar = 'هيئة الزكاة والضريبة والجمارك'
        Name_uk = 'Управління закяту, податків і митниці'
    })
    TaxJurisdiction = @([ordered]@{
        Code = 'SA'
        Name = 'Saudi Arabia'
        CountryCode = 'SA'
        Level = 0
        Name_ru = 'Саудовская Аравия'
        Name_ar = 'المملكة العربية السعودية'
        Name_uk = 'Саудівська Аравія'
    })
    Tax = @([ordered]@{
        Code = 'VAT-SA'
        Name = 'Value Added Tax (Saudi Arabia)'
        EffectiveFrom = '2018-01-01T00:00:00Z'
        Authority = $zatcaRef
        Jurisdiction = $saTaxJurisdictionRef
        Name_ru = 'Налог на добавленную стоимость (Саудовская Аравия)'
        Name_ar = 'ضريبة القيمة المضافة (السعودية)'
        Name_uk = 'Податок на додану вартість (Саудівська Аравія)'
    })
    TaxCategory = @(
        [ordered]@{ Code = 'STANDARD'; Name = 'Standard'; Treatment = 'STANDARD'; Tax = $vatSaRef; Name_ru = 'Стандартная'; Name_ar = 'قياسي'; Name_uk = 'Стандартна' },
        [ordered]@{ Code = 'ZERO_RATED'; Name = 'Zero-rated'; Treatment = 'ZERO_RATED'; Tax = $vatSaRef; Name_ru = 'Нулевая ставка'; Name_ar = 'صفرية'; Name_uk = 'Нульова ставка' },
        [ordered]@{ Code = 'EXEMPT'; Name = 'Exempt'; Treatment = 'EXEMPT'; Tax = $vatSaRef; Name_ru = 'Освобождённая'; Name_ar = 'معفاة'; Name_uk = 'Звільнена' },
        [ordered]@{ Code = 'OUT_OF_SCOPE'; Name = 'Out of scope'; Treatment = 'OUT_OF_SCOPE'; Tax = $vatSaRef; Name_ru = 'Вне сферы'; Name_ar = 'خارج النطاق'; Name_uk = 'Поза сферою' }
    )
    TaxRate = @(
        [ordered]@{ Code = 'VAT5'; Rate = 0.05; EffectiveFrom = '2018-01-01T00:00:00Z'; EffectiveTo = '2020-06-30T23:59:59Z'; Tax = $vatSaRef; TaxCategory = $standardVatCategoryRef },
        [ordered]@{ Code = 'VAT15'; Rate = 0.15; EffectiveFrom = '2020-07-01T00:00:00Z'; Tax = $vatSaRef; TaxCategory = $standardVatCategoryRef }
    )
    TaxCode = @(
        [ordered]@{ Code = 'S'; Name = 'Standard (ZATCA)'; EffectiveFrom = '2018-01-01T00:00:00Z'; Tax = $vatSaRef; TaxCategory = $standardVatCategoryRef; TaxRate = $vat15Ref; Name_ru = 'Стандартный (ZATCA)'; Name_ar = 'قياسي (ZATCA)'; Name_uk = 'Стандартний (ZATCA)' },
        [ordered]@{ Code = 'Z'; Name = 'Zero-rated (ZATCA)'; EffectiveFrom = '2018-01-01T00:00:00Z'; Tax = $vatSaRef; TaxCategory = New-Ref 'TaxCategory' ([ordered]@{ Tax = $vatSaRef; Code = 'ZERO_RATED' }); Name_ru = 'Нулевая ставка (ZATCA)'; Name_ar = 'صفرية (ZATCA)'; Name_uk = 'Нульова ставка (ZATCA)' },
        [ordered]@{ Code = 'E'; Name = 'Exempt (ZATCA)'; EffectiveFrom = '2018-01-01T00:00:00Z'; Tax = $vatSaRef; TaxCategory = New-Ref 'TaxCategory' ([ordered]@{ Tax = $vatSaRef; Code = 'EXEMPT' }); Name_ru = 'Освобождён (ZATCA)'; Name_ar = 'معفى (ZATCA)'; Name_uk = 'Звільнений (ZATCA)' },
        [ordered]@{ Code = 'O'; Name = 'Out of scope (ZATCA)'; EffectiveFrom = '2018-01-01T00:00:00Z'; Tax = $vatSaRef; TaxCategory = New-Ref 'TaxCategory' ([ordered]@{ Tax = $vatSaRef; Code = 'OUT_OF_SCOPE' }); Name_ru = 'Вне области (ZATCA)'; Name_ar = 'خارج النطاق (ZATCA)'; Name_uk = 'Поза сферою (ZATCA)' }
    )
    ItemGroup = $groups
    Item = $items
    Customer = $customers
    CustomerOutlet = $outlets
    Supplier = $suppliers
    SalesContract = $contracts
    StockAdjustment = $stockAdjustments
    PurchaseOrder = $purchaseOrders
    SalesRealization = $sales
}

$entities = @(
    (New-Entity 'LegalEntity' @('RegistrationNumber') $data.LegalEntity.Count),
    (New-Entity 'Division' @('LegalEntity', 'Name') $data.Division.Count),
    (New-Entity 'Store' @('Division', 'Name') $data.Store.Count),
    (New-Entity 'StoreZone' @('Store', 'Name') $data.StoreZone.Count),
    (New-Entity 'StoreCell' @('StoreZone', 'Name') $data.StoreCell.Count),
    (New-Entity 'TaxAuthority' @('Code') $data.TaxAuthority.Count),
    (New-Entity 'TaxJurisdiction' @('Code') $data.TaxJurisdiction.Count),
    (New-Entity 'Tax' @('Code') $data.Tax.Count),
    (New-Entity 'TaxCategory' @('Tax', 'Code') $data.TaxCategory.Count),
    (New-Entity 'TaxRate' @('Tax', 'Code') $data.TaxRate.Count),
    (New-Entity 'TaxCode' @('Code') $data.TaxCode.Count),
    (New-Entity 'ItemGroup' @('Code') $data.ItemGroup.Count),
    (New-Entity 'Item' @('Name') $data.Item.Count),
    (New-Entity 'Customer' @('Name') $data.Customer.Count),
    (New-Entity 'CustomerOutlet' @('Customer', 'Name') $data.CustomerOutlet.Count),
    (New-Entity 'Supplier' @('Name') $data.Supplier.Count),
    (New-Entity 'SalesContract' @('Customer', 'Name') $data.SalesContract.Count),
    (New-Entity 'StockAdjustment' @('Cell', 'DocumentDate') $data.StockAdjustment.Count 'Document'),
    (New-Entity 'PurchaseOrder' @('Supplier', 'DocumentDate') $data.PurchaseOrder.Count 'Document'),
    (New-Entity 'SalesRealization' @('Customer', 'DocumentDate') $data.SalesRealization.Count 'Document')
)

$package = [ordered]@{
    manifest = [ordered]@{
        formatVersion = 1
        profile = 'showcase'
        exportedAt = $AsOfUtc.ToString("yyyy-MM-ddTHH:mm:ss'Z'")
        entities = $entities
    }
    data = $data
}

$directory = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Force -Path $directory | Out-Null
$json = $package | ConvertTo-Json -Depth 30
Set-Content -LiteralPath $OutputPath -Value $json -Encoding utf8NoBOM
Write-Output "Wrote $OutputPath"
