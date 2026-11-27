# Optional developer formatter using the Roslyn libraries bundled with PowerShell 7.
$ErrorActionPreference = 'Stop'
if (!(Test-Path -LiteralPath (Join-Path $PSHOME 'Microsoft.CodeAnalysis.CSharp.dll'))) { throw 'Run this optional script with PowerShell 7 (pwsh).' }
if (!('DeskPillSourceFormat' -as [type])) {
    Add-Type -TypeDefinition 'using Microsoft.CodeAnalysis; public static class DeskPillSourceFormat { public static string Format(string text) { return Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseCompilationUnit(text).NormalizeWhitespace().ToFullString(); } }' -ReferencedAssemblies @((Join-Path $PSHOME 'Microsoft.CodeAnalysis.dll'), (Join-Path $PSHOME 'Microsoft.CodeAnalysis.CSharp.dll'), (Join-Path $PSHOME 'System.Collections.Immutable.dll')) -CompilerOptions '/nowarn:1701'
}
foreach ($directory in @('src/DeskPill','tests')) {
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot ('../' + $directory)) -Filter '*.cs') {
        $formatted = [DeskPillSourceFormat]::Format([IO.File]::ReadAllText($file.FullName))
        [IO.File]::WriteAllText($file.FullName, $formatted + [Environment]::NewLine, (New-Object Text.UTF8Encoding($false)))
    }
}
