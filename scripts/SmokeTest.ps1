param([string]$BaseUrl = 'http://localhost:5088')
$ErrorActionPreference = 'Stop'
$suffix = [guid]::NewGuid().ToString('N').Substring(0,8)
function Send-Json($Method,$Path,$Data) {
    Invoke-RestMethod -Uri "$BaseUrl/api$Path" -Method $Method -ContentType 'application/json; charset=utf-8' -Body ([System.Text.Encoding]::UTF8.GetBytes(($Data | ConvertTo-Json)))
}
$category = $null; $location = $null; $product = $null
try {
    $info = Invoke-RestMethod "$BaseUrl/api/info"
    $category = Send-Json Post '/categories' @{name="Проверка $suffix"}
    $location = Send-Json Post '/locations' @{name="Полка $suffix"}
    $expiration = ([datetime]::Parse($info.today)).AddDays(2).ToString('yyyy-MM-dd')
    $data = @{name="Продукт $suffix";quantity=1.5;unit='кг';expirationDate=$expiration;categoryId=$category.id;storageLocationId=$location.id}
    $product = Send-Json Post '/products' $data
    $item = Invoke-RestMethod "$BaseUrl/api/products/$($product.id)"
    if ($item.daysLeft -ne 2 -or $item.status -ne 'soon') { throw 'Неправильный расчёт срока' }
    $expiring = Invoke-RestMethod "$BaseUrl/api/products/expiring?days=3"
    if ($product.id -notin $expiring.id) { throw 'Продукт не найден среди истекающих' }
    $data.quantity = 2
    $null = Send-Json Put "/products/$($product.id)" $data
    $updated = Invoke-RestMethod "$BaseUrl/api/products/$($product.id)"
    if ($updated.quantity -ne 2) { throw 'Изменение не сохранилось' }
    Write-Host "OK: создание, чтение, изменение, сроки и endpoint истекающих продуктов. Demo=$($info.demo)"
} finally {
    if ($product) { Invoke-RestMethod "$BaseUrl/api/products/$($product.id)" -Method Delete | Out-Null }
    if ($category) { Invoke-RestMethod "$BaseUrl/api/categories/$($category.id)" -Method Delete | Out-Null }
    if ($location) { Invoke-RestMethod "$BaseUrl/api/locations/$($location.id)" -Method Delete | Out-Null }
}
