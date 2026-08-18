this one worked

powershell -ExecutionPolicy Bypass -File .\print-mixed-test.ps1 -Printer "SHARP BP-71C65 PCL6"
-TabNumber 3 -NudgeXIn -0.625


powershell -ExecutionPolicy Bypass -File .\print-mixed-test.ps1 -Printer "SHARP BP-71C65 PCL6" -TabNumber 3 -TabTrayPattern "(?i)bypass" -BodyTrayPattern "(?i)tray\s*1" -FlipTabY -NudgeXIn 0 -Text "EMAIL CORRESPONDENCE"