### This script only works inside the Package Manager Console in VS
### Make sure the /license/ folder exists

$output = @()

# Find all unique packages across all projects
@( Get-Project -All | Where-Object { $_.ProjectName } | ForEach-Object {
    Get-Package -ProjectName $_.ProjectName # | ? { $_.LicenseUrl }
  } ) | Sort-Object Id -Unique | ForEach-Object {
  $pkg = $_;
  Try {
     
    #Write-Host $_.Id
    #Write-Host $_.LicenseUrl
    #Write-Host $_.Version

    $licenseUrl = $_.LicenseUrl

    if ($licenseUrl -eq $null) {
      $licenseUrl = ""
    }

    if ($licenseUrl.contains('github.com')) {
      $licenseUrl = $licenseUrl.replace("/blob/", "/raw/")
    }

    $extension = ".html"
    if ($licenseUrl.EndsWith(".md")) {
      $extension = ".md"
    }
    if ($licenseUrl.EndsWith(".html")) {
      $extension = ".html"
    }

    $filePath = (Join-Path (Get-Location) 'wwwroot\licenses\') + $pkg.Id + $extension;

    # Only download if it doesn't already exist
    if ((Test-Path -Path $filePath) -eq $false -and $licenseUrl.Trim() -ne "") {
      try {
        #Write-Host("Downloading... " + $licenseUrl)
        (New-Object System.Net.WebClient).DownloadFile($licenseUrl, $filePath);           
      }
      Catch [system.exception] {
        Write-Host($pkg.Id + " FAILED");
      }
    }

    # Don't try to parse the HTML files, the first line doesn't include the license name anyways
    if ($extension -ne ".html" -and (Test-Path -Path $filePath) -eq $true) {
      $textLicence = Get-Content $filePath | Select-Object -First 1

      # Extensionless URLs might be HTML after all, so let's handle that
      if ($textLicence.Contains("html")) {
        $textLicence = "-"
      }
    }
    else {
      $textLicence = "-"
    }

    # Build a better output format for CSV
    $output += [pscustomobject]@{
      Name    = $pkg.Id;
      Version = $pkg.Version;
      Url     = $licenseUrl;
      License = $textLicence.Trim()
    }

    Write-Host($pkg.Id + " processed");
  }
  Catch [system.exception] {
    Write-Host ($error[0].Exception);
    Write-Host ("Could not read license for " + $pkg.Id)
  }
}

$output | Export-Csv -Path licenseoutput.csv