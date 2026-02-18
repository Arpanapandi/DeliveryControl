
$path = "c:\DeliveryControl\DeliveryControl\Controllers\PreparationScheduleController.cs"
$lines = Get-Content $path
$newLines = @()
$skip = $false

for ($i = 0; $i -lt $lines.Count; $i++) {
    $line = $lines[$i]
    
    # Start of messy section
    if ($line -like "*if (matchedItem == null)*" -and $i -ge 595 -and $i -le 610) {
        if (-not $skip) {
            $newLines += '                            if (matchedItem == null)'
            $newLines += '                            {'
            $newLines += '                                // v11.0: AUTO-CREATE Master Item to ensure sync (Draft Mode)'
            $newLines += '                                matchedItem = new Item'
            $newLines += '                                {'
            $newLines += '                                    ItemCode = targetVin, // Use VIN as Code'
            $newLines += '                                    VIN = targetVin,'
            $newLines += '                                    CustomerPartNumber = !string.IsNullOrEmpty(excelPartNo) ? excelPartNo : ((itemMap != null) ? itemMap.CustomerPartNumber : (targetVin != itemCode ? itemCode : null)),'
            $newLines += '                                    Customer = customer.CustomerName,'
            $newLines += '                                    ItemName = "Imported (" + itemCode + ")",'
            $newLines += '                                    Description = "Auto-created from Schedule Import",'
            $newLines += '                                    IsActive = true,'
            $newLines += '                                    CreatedDate = DateTime.Now,'
            $newLines += '                                    QtyLot = 1 // Default QPC'
            $newLines += '                                };'
            $newLines += '                                _context.Items.Add(matchedItem);'
            $newLines += '                                allItems.Add(matchedItem); '
            $newLines += '                            }'
            $skip = $true
        }
        continue
    }

    # Stop skipping after the mess
    if ($skip -and ($line -like "*else*" -or $line -like "*if (string.IsNullOrEmpty(matchedItem.CustomerPartNumber))*")) {
        $skip = $false
    }

    if (-not $skip) {
        $newLines += $line
    }
}

$newLines | Out-File $path -Encoding utf8
