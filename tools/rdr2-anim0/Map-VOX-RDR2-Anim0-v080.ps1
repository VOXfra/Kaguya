param(
    [Parameter(Mandatory=$true)][string]$NestedCsv,
    [Parameter(Mandatory=$true)][string]$NestedArchivesCsv,
    [Parameter(Mandatory=$true)][string]$CitizenFxHeader,
    [Parameter(Mandatory=$true)][string]$OutDir
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

function Classify-Archive([string]$name) {
    $n = $name.ToLowerInvariant()
    $systems = New-Object System.Collections.Generic.List[string]
    if ($n -match 'gesture|interact|scenario|script|ambient|amb_|hostage|conversation|greet|antagon|react') { $systems.Add('interaction') }
    if ($n -match 'melee|combat|grapple|fight|takedown|busted|butcher|revive') { $systems.Add('melee') }
    if ($n -match 'loco|strafe|ledge|getup|avoid|move|vault|climb') { $systems.Add('locomotion') }
    if ($n -match 'door') { $systems.Add('doors') }
    if ($n -match 'react|injur|damage|ragdoll|fall|getup') { $systems.Add('reactions') }
    if ($n -match 'weapon|gun|rifle|pistol|bow|throw') { $systems.Add('weapons') }
    if ($n -match 'animal|creature|horse') { $systems.Add('wildlife') }
    if ($n -match 'veh_|vehicle|train|wagon|cart') { $systems.Add('vehicle') }
    if ($systems.Count -eq 0) { $systems.Add('other') }
    return (($systems | Select-Object -Unique) -join ';')
}
function Score-Archive([string]$name,[int]$ycdCount,[int]$yasCount) {
    $n=$name.ToLowerInvariant(); $s=[Math]::Min(35,$ycdCount)+[Math]::Min(20,$yasCount)
    if($n -match 'ai_gesture|interact|scenario|script@common|script_common'){ $s+=115 }
    if($n -match 'melee|combat|grapple|fight|takedown'){ $s+=105 }
    if($n -match 'loco|strafe|ledge|getup|avoid|vault|climb'){ $s+=95 }
    if($n -match 'door'){ $s+=100 }
    if($n -match 'react|injur|damage|ragdoll|revive|busted'){ $s+=90 }
    if($n -match 'ambient|amb_|hostage|conversation'){ $s+=65 }
    if($n -match 'weapon|gun|rifle|pistol'){ $s+=55 }
    if($n -match 'cuts@|cuts_'){ $s-=45 }
    return $s
}

$nested = Import-Csv -LiteralPath $NestedCsv
$archives = Import-Csv -LiteralPath $NestedArchivesCsv
if(-not $nested -or -not ($nested[0].PSObject.Properties.Name -contains 'outer_index')) { throw 'Nested entry CSV schema is not expected.' }
if(-not $archives -or -not ($archives[0].PSObject.Properties.Name -contains 'toc_sha256')) { throw 'Nested archive CSV with toc_sha256 is required.' }

# CitizenFX hashes exactly the decrypted RPF8 entry table (EntryCount * 24 bytes) for RDR3 pure-mode validation.
$shaToName=@{}
$header = Get-Content -LiteralPath $CitizenFxHeader
$inAnim=$false
$publicCount=0
foreach($line in $header) {
    if($line -match '^\s*//\s*anim_0\.rpf\s*$') { $inAnim=$true; continue }
    if($inAnim -and $line -match '^\s*//\s*common_0\.rpf\s*$') { break }
    if(-not $inAnim) { continue }
    if($line -match 'ShaUnpack\("([0-9a-fA-F]{64})"\),\s*//\s*(.+?\.rpf)\s*$') {
        $sha=$Matches[1].ToLowerInvariant(); $name=$Matches[2].Trim()
        if($name -ne 'anim_0.rpf') {
            if(-not $shaToName.ContainsKey($sha)) { $shaToName[$sha]=New-Object System.Collections.Generic.List[string] }
            $shaToName[$sha].Add($name); $publicCount++
        }
    }
}
if($publicCount -lt 100) { throw "CitizenFX anim_0 parser returned only $publicCount subarchives." }

$groupsByIndex=@{}
foreach($g in ($nested | Group-Object outer_index)) { $groupsByIndex[[int]$g.Name]=$g.Group }
$mapRows=New-Object System.Collections.Generic.List[object]
$exact=0;$ambiguous=0;$unresolved=0
foreach($a in $archives | Sort-Object {[int]$_.outer_index}) {
    $idx=[int]$a.outer_index; $sha=([string]$a.toc_sha256).ToLowerInvariant()
    $name='';$confidence='unresolved';$candidates=''
    if($shaToName.ContainsKey($sha)) {
        $m=@($shaToName[$sha] | Sort-Object -Unique)
        if($m.Count -eq 1){$name=$m[0];$confidence='exact_toc_sha256';$exact++}
        else{$candidates=($m -join ';');$name=$candidates;$confidence='ambiguous_toc_sha256';$ambiguous++}
    } else {$unresolved++}
    $g=@(); if($groupsByIndex.ContainsKey($idx)){$g=@($groupsByIndex[$idx])}
    $ycd=@($g|Where-Object extension -eq 'ycd').Count
    $yas=@($g|Where-Object extension -eq 'yas').Count
    $ymt=@($g|Where-Object extension -eq 'ymt').Count
    $systems=if($name){Classify-Archive $name}else{'unknown'}
    $score=if($name){Score-Archive $name $ycd $yas}else{[Math]::Min(35,$ycd)+[Math]::Min(20,$yas)}
    $mapRows.Add([pscustomobject]@{
        outer_index=$idx;outer_hash=$a.outer_hash;outer_generated_name=$a.outer_generated_name;
        toc_sha256=$sha;public_archive_name=$name;confidence=$confidence;collision_candidates=$candidates;
        systems=$systems;ycd_count=$ycd;yas_count=$yas;ymt_count=$ymt;nested_entries=$g.Count;priority_score=$score
    })
}
$mapRows | Sort-Object outer_index | Export-Csv -NoTypeInformation -Encoding UTF8 -LiteralPath (Join-Path $OutDir 'ANIM0-exact-archive-map.csv')
$mapRows | Sort-Object -Property @{Expression={[int]$_.priority_score};Descending=$true}, @{Expression={[int]$_.ycd_count};Descending=$true} | Export-Csv -NoTypeInformation -Encoding UTF8 -LiteralPath (Join-Path $OutDir 'ANIM0-exact-priority-archives.csv')

$mapByIndex=@{};foreach($m in $mapRows){$mapByIndex[[int]$m.outer_index]=$m}
$ycdRows=foreach($r in $nested|Where-Object extension -eq 'ycd'){
    $m=$mapByIndex[[int]$r.outer_index]
    [pscustomobject]@{outer_index=$m.outer_index;public_archive_name=$m.public_archive_name;archive_confidence=$m.confidence;systems=$m.systems;nested_index=$r.nested_index;ycd_hash=$r.hash_hex;generated_name=$r.generated_name;enc_key=$r.enc_key;enc_config=$r.enc_config;compressor=$r.compressor;logical_size=$r.logical_size;on_disk_size=$r.on_disk_size;is_resource=$r.is_resource;archive_priority=$m.priority_score}
}
$ycdRows | Sort-Object -Property @{Expression={[int]$_.archive_priority};Descending=$true}, @{Expression={[int]$_.outer_index};Descending=$false}, @{Expression={[int]$_.nested_index};Descending=$false} | Export-Csv -NoTypeInformation -Encoding UTF8 -LiteralPath (Join-Path $OutDir 'ANIM0-exact-ycd-candidates.csv')

$yasRows=foreach($r in $nested|Where-Object extension -eq 'yas'){
    $m=$mapByIndex[[int]$r.outer_index]
    [pscustomobject]@{outer_index=$m.outer_index;public_archive_name=$m.public_archive_name;archive_confidence=$m.confidence;systems=$m.systems;nested_index=$r.nested_index;yas_hash=$r.hash_hex;generated_name=$r.generated_name;enc_key=$r.enc_key;enc_config=$r.enc_config;compressor=$r.compressor;logical_size=$r.logical_size;on_disk_size=$r.on_disk_size;archive_priority=$m.priority_score}
}
$yasRows | Sort-Object -Property @{Expression={[int]$_.archive_priority};Descending=$true}, @{Expression={[int]$_.outer_index};Descending=$false}, @{Expression={[int]$_.nested_index};Descending=$false} | Export-Csv -NoTypeInformation -Encoding UTF8 -LiteralPath (Join-Path $OutDir 'ANIM0-exact-yas-candidates.csv')

$sysCounts=@{}
foreach($m in $mapRows){foreach($s in ([string]$m.systems -split ';')){if(-not $sysCounts.ContainsKey($s)){$sysCounts[$s]=[ordered]@{archives=0;ycd=0;yas=0}};$sysCounts[$s].archives++;$sysCounts[$s].ycd+=[int]$m.ycd_count;$sysCounts[$s].yas+=[int]$m.yas_count}}
$report=New-Object System.Collections.Generic.List[string]
$report.Add('VOX RDR2 anim_0 exact mapper v0.8.0')
$report.Add('=========================================')
$report.Add("Nested archives: $($archives.Count)")
$report.Add("CitizenFX anim_0 subarchives: $publicCount")
$report.Add("Exact TOC SHA-256 matches: $exact")
$report.Add("Ambiguous SHA matches: $ambiguous")
$report.Add("Unresolved: $unresolved")
$report.Add("YCD entries: $($ycdRows.Count)")
$report.Add("YAS entries: $($yasRows.Count)")
$report.Add('')
$report.Add('PROJECT SYSTEM COUNTS')
foreach($k in $sysCounts.Keys|Sort-Object){$v=$sysCounts[$k];$report.Add("${k}: archives=$($v.archives) ycd=$($v.ycd) yas=$($v.yas)")}
$report.Add('')
$report.Add('TOP EXACT ARCHIVES')
$topExact = $mapRows | Where-Object confidence -eq 'exact_toc_sha256' | Sort-Object -Property @{Expression={[int]$_.priority_score};Descending=$true}, @{Expression={[int]$_.ycd_count};Descending=$true} | Select-Object -First 80
foreach($m in $topExact){$report.Add(('[{0,3}] {1} | {2} | ycd={3} yas={4} ymt={5} | sha={6}' -f $m.priority_score,$m.public_archive_name,$m.systems,$m.ycd_count,$m.yas_count,$m.ymt_count,$m.toc_sha256))}
$report|Set-Content -Encoding UTF8 -LiteralPath (Join-Path $OutDir 'ANIM0-exact-report.txt')

[pscustomobject]@{tool='VOX RDR2 anim_0 exact mapper';version='0.8.0';nested_archives=$archives.Count;citizenfx_subarchives=$publicCount;exact_toc_sha256_matches=$exact;ambiguous_matches=$ambiguous;unresolved=$unresolved;ycd_entries=$ycdRows.Count;yas_entries=$yasRows.Count;raw_assets_written=$false} | ConvertTo-Json -Depth 4 | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $OutDir 'ANIM0-exact-summary.json')
Write-Host "[MAP] archives=$($archives.Count) exact=$exact ambiguous=$ambiguous unresolved=$unresolved ycd=$($ycdRows.Count) yas=$($yasRows.Count)"
if($exact -eq 0){exit 4}
