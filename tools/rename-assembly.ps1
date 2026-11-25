# Writes a copy of a mod DLL with a new assembly name so it can load next to another mod
# that uses the same internal assembly name. Only names change, not the code.
# -Namespace also renames the mod's top namespace, for mods that also share class names
# (Any Radiance 3.0 and ShatteredQuasar both have AnyRadiance.LocalSettings, which crashes the game).
# Run with Windows PowerShell:
#   powershell -File tools\rename-assembly.ps1 -Path in.dll -Out out.dll -Name NewName [-Namespace NewNs] [-ModName "Shown Name"]
param(
    [Parameter(Mandatory)] [string]$Path,
    [Parameter(Mandatory)] [string]$Out,
    [string]$Name,
    [string]$Namespace,
    # the name the mod list shows; the API refuses two mods with the same one (Any Radiance 1.0 and 3.0)
    [string]$ModName,
    [string]$Managed
)
$ErrorActionPreference = 'Stop'

if (-not $Managed) {
    $envFile = Join-Path $PSScriptRoot '..\.env'
    $game = (Get-Content $envFile | Where-Object { $_ -match '^\s*HK_GAME\s*=' }) -replace '^\s*HK_GAME\s*=\s*', ''
    $Managed = Join-Path $game.Trim() 'Hollow Knight_Data\Managed'
}
if (-not $Name) { $Name = [IO.Path]::GetFileNameWithoutExtension($Path) -replace '[^A-Za-z0-9_]', '' }
if ((Resolve-Path $Path).Path -eq [IO.Path]::GetFullPath($Out)) { throw 'Out must be a different file than Path.' }

Add-Type -Path (Join-Path $Managed 'Mono.Cecil.dll')

$resolver = New-Object Mono.Cecil.DefaultAssemblyResolver
$resolver.AddSearchDirectory($Managed)
Get-ChildItem (Join-Path $Managed 'Mods') -Directory -Recurse | ForEach-Object { $resolver.AddSearchDirectory($_.FullName) }

$params = New-Object Mono.Cecil.ReaderParameters
$params.AssemblyResolver = $resolver
$params.InMemory = $true
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path $Path).Path, $params)

$old = $asm.Name.Name
$asm.Name.Name = $Name
$asm.MainModule.Name = "$Name.dll"

if ($Namespace) {
    # the namespace every type of the mod lives in, e.g. AnyRadiance
    $oldNs = ($asm.MainModule.Types | Where-Object { $_.BaseType -and $_.BaseType.FullName -eq 'Modding.Mod' } | Select-Object -First 1).Namespace
    if (-not $oldNs) { throw 'Could not find the namespace of the mod class.' }
    $count = 0
    foreach ($t in $asm.MainModule.Types) {
        if ($t.Namespace -eq $oldNs) { $t.Namespace = $Namespace; $count++ }
        elseif ($t.Namespace.StartsWith("$oldNs.")) { $t.Namespace = $Namespace + $t.Namespace.Substring($oldNs.Length); $count++ }
    }
    "namespace $oldNs -> $Namespace ($count types)"
}

if ($ModName) {
    # the string the mod class passes to Mod's constructor
    $done = $false
    foreach ($t in $asm.MainModule.Types | Where-Object { $_.BaseType -and $_.BaseType.FullName -eq 'Modding.Mod' }) {
        foreach ($ctor in $t.Methods | Where-Object { $_.Name -eq '.ctor' -and $_.HasBody }) {
            $ins = $ctor.Body.Instructions
            for ($i = 0; $i -lt $ins.Count; $i++) {
                if ($ins[$i].OpCode.Name -eq 'call' -and "$($ins[$i].Operand)" -eq 'System.Void Modding.Mod::.ctor(System.String)' -and $i -gt 0 -and $ins[$i - 1].OpCode.Name -eq 'ldstr') {
                    "mod name '$($ins[$i - 1].Operand)' -> '$ModName'"
                    $ins[$i - 1].Operand = $ModName
                    $done = $true
                }
            }
        }
    }
    if (-not $done) { throw 'Could not find the mod name in the mod constructor.' }
}

New-Item -ItemType Directory -Force (Split-Path ([IO.Path]::GetFullPath($Out))) | Out-Null
$asm.Write([IO.Path]::GetFullPath($Out))
"$old -> $Name : $Out"
