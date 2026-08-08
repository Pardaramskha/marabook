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

# — Grammalecte et le runtime Python embarqués (batch 29, lot E) : le
# manifeste tools\embedded-hashes.txt fait foi, généré depuis des sources
# VÉRIFIÉES (zip Grammalecte 2.3.0 recoupé par son SHA256 d'archive, Python
# embeddable 3.13.15 par sa signature GPG Steve Dower). Même règle que le
# dictionnaire : un écart = pas de compilation. Les arbres ABSENTS en bloc
# sont tolérés (clone sans les embarqués : la grammaire se taira) — un arbre
# PARTIEL ou ALTÉRÉ, jamais.
$manifest = Join-Path $PSScriptRoot 'embedded-hashes.txt'
if (Test-Path $manifest) {
    $checked = 0
    $missing = 0
    $trees = @{}
    foreach ($line in Get-Content $manifest) {
        if ($line.Trim().Length -eq 0) { continue }
        $sha = $line.Substring(0, 64)
        $rel = $line.Substring(66).Replace('/', '\')
        $tree = $rel.Split('\')[0]
        if (-not $trees.ContainsKey($tree)) { $trees[$tree] = (Test-Path (Join-Path $root $tree)) }
        $path = Join-Path $root $rel
        if (-not (Test-Path $path)) {
            if ($trees[$tree]) {
                Write-Host ("ABSENT : " + $rel)
                $failed = $true
            } else { $missing++ }
            continue
        }
        $hash = (Get-FileHash $path -Algorithm SHA256).Hash.ToLower()
        if ($hash -ne $sha) {
            Write-Host ("EMPREINTE INATTENDUE : " + $rel)
            $failed = $true
        }
        $checked++
    }
    if ($failed) {
        Write-Host "*** Ressource embarquee alteree - voir APPROVISIONNEMENT.md ***"
        exit 1
    }
    if ($missing -gt 0) {
        Write-Host ("Embarques : " + $checked + " fichiers conformes, " + $missing + " absents (arbre non deploye - la grammaire se taira).")
    } else {
        Write-Host ("Grammalecte 2.3.0 + Python embeddable : " + $checked + " empreintes conformes.")
    }
}
Write-Host "Dictionnaire fr-toutesvariantes 7.7 : empreintes conformes."
exit 0
