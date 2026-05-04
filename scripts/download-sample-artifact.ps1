# Download a sample artifact (7-Zip MSI) for testing artifact upload
$Url = "https://www.7-zip.org/a/7z2409-x64.msi"
$OutFile = "C:\temp\7-Zip_24.09.msi"

Write-Host "Downloading 7-Zip MSI to $OutFile ..."
Invoke-WebRequest -Uri $Url -OutFile $OutFile -UseBasicParsing
Write-Host "Download complete: $OutFile"
Write-Host ""
Write-Host "You can now upload this artifact using:"
Write-Host "  curl.exe -X POST http://localhost:5000/api/artifacts/upload "
Write-Host "    -F `"packageId=7-Zip`" "
Write-Host "    -F `"version=24.09`" "
Write-Host "    -F `"packageName=7-Zip`" "
Write-Host "    -F `"file=@$OutFile`" "
