# Vérification des ressources linguistiques embarquées (batch 27, lot E).
# Les empreintes font foi : APPROVISIONNEMENT.md, section « Les six fichiers
# de dictionnaire ». LE BUILD ÉCHOUE EN CAS D'ÉCART — un dictionnaire modifié
# ou corrompu ne part jamais en silence. Amont unique : grammalecte.net
# (jamais dicollecte.org, domaine squatté).
$root = Split-Path -Parent $PSScriptRoot
$expected = @{
    'dict\fr-toutesvariantes.dic' = '140c5296b514bc55af7433f8913a73245b9b18116830a7db48e2aabfa85fcbed'
    'dict\fr-toutesvariantes.aff' = '254756d4fd286a923b3f51dae65965bfc8619aa96db8528326bf31fcdc0840e4'
}
$failed = $false
foreach ($entry in $expected.GetEnumerator()) {
    $path = Join-Path $root $entry.Key
    if (-not (Test-Path $path)) {
        Write-Host ("ABSENT : " + $entry.Key)
        $failed = $true
        continue
    }
    $hash = (Get-FileHash $path -Algorithm SHA256).Hash.ToLower()
    if ($hash -ne $entry.Value) {
        Write-Host ("EMPREINTE INATTENDUE : " + $entry.Key)
        Write-Host ("  attendu " + $entry.Value)
        Write-Host ("  obtenu  " + $hash)
        $failed = $true
    }
}
if ($failed) {
    Write-Host "*** Dictionnaire absent ou corrompu - voir APPROVISIONNEMENT.md ***"
    exit 1
}
Write-Host "Dictionnaire fr-toutesvariantes 7.7 : empreintes conformes."
exit 0
