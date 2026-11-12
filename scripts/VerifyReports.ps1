param([string]$BaseUrl='http://localhost:5088')
$ErrorActionPreference='Stop'
function Send($method,$path,$body){Invoke-RestMethod "$BaseUrl/api$path" -Method $method -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes(($body|ConvertTo-Json)))}
$customer=$null;$orders=@()
try{
 $customer=Send Post '/customers' @{firstName='Проверка';lastName='SQL';birthDate='2000-11-12';email='verify@example.com';address='Брянск'}
 foreach($item in @(@{amount=100;orderedAt='2025-11-12T12:00:00+03:00';status='Completed'},@{amount=250;orderedAt='2026-11-12T12:30:00+03:00';status='Completed'},@{amount=999;orderedAt='2026-11-12T12:45:00+03:00';status='Cancelled'})){
  $item.customerId=$customer.id;$orders+=(Send Post '/orders' $item).id
 }
 $birthdays=Invoke-RestMethod "$BaseUrl/api/reports/birthday-orders"
 $row=$birthdays|Where-Object customerId -eq $customer.id
 if($row.total -ne 350){throw "Birthday procedure returned $($row.total), expected 350"}
 $hours=Invoke-RestMethod "$BaseUrl/api/reports/hourly-orders"
 if($hours.Count -ne 24){throw 'Hourly procedure must return 24 hours'}
 $hour=$hours|Where-Object hour -eq 12
 if($hour.count -lt 2){throw 'Hourly procedure lost completed orders'}
 for($i=1;$i -lt $hours.Count;$i++){if($hours[$i].average -gt $hours[$i-1].average){throw 'Hourly averages are not descending'}}
 $null=Invoke-RestMethod "$BaseUrl/api/reports/inventory"
 Write-Host 'OK: birthday total=350; cancelled excluded; 24 hourly intervals; descending averages; inventory report.'
}finally{
 foreach($id in $orders){Invoke-RestMethod "$BaseUrl/api/orders/$id" -Method Delete | Out-Null}
 if($customer){Invoke-RestMethod "$BaseUrl/api/customers/$($customer.id)" -Method Delete | Out-Null}
}
