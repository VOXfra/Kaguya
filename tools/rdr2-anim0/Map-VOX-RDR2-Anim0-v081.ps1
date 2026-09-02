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
$archiveProps=@($archives[0].PSObject.Properties.Name)
if(-not $archives -or -not ($archiveProps -contains 'toc_sha256_raw') -or -not ($archiveProps -contains 'toc_sha256_decrypted')) { throw 'Dual raw/decrypted nested archive SHA columns are required.' }

# CitizenFX RDR3 pure mode hashes the 24-byte entry array supplied by fiPackfile::ReInit.
# v0.8.1 tests both plausible representations available to our local reader:
# raw on-disk nested TOC bytes and our locally decrypted nested TOC bytes.
$shaToName=@{}
$publicRows=New-Object System.Collections.Generic.List[object]
$header = Get-Content -LiteralPath $CitizenFxHeader
$inAnim=$false
foreach($line in $header) {
    if($line -match '^\s*//\s*anim_0\.rpf\s*$') { $inAnim=$true; continue }
    if($inAnim -and $line -match '^\s*//\s*common_0\.rpf\s*$') { break }
    if(-not $inAnim) { continue }
    if($line -match 'ShaUnpack\("([0-9a-fA-F]{64})"\),\s*//\s*(.+?\.rpf)\s*$') {
        $sha=$Matches[1].ToLowerInvariant(); $name=$Matches[2].Trim()
        if($name -ne 'anim_0.rpf') {
            if(-not $shaToName.ContainsKey($sha)) { $shaToName[$sha]=New-Object System.Collections.Generic.List[string] }
            $shaToName[$sha].Add($name)
            $publicRows.Add([pscustomobject]@{ordinal=$publicRows.Count;sha256=$sha;name=$name})
        }
    }
}
$publicCount=$publicRows.Count
if($publicCount -lt 100) { throw "CitizenFX anim_0 parser returned only $publicCount subarchives." }

function Resolve-PublicSha([string]$sha) {
    if([string]::IsNullOrWhiteSpace($sha)){ return @() }
    $k=$sha.ToLowerInvariant()
    if(-not $shaToName.ContainsKey($k)){ return @() }
    return @($shaToName[$k] | Sort-Object -Unique)
}

$groupsByIndex=@{}
foreach($g in ($nested | Group-Object outer_index)) { $groupsByIndex[[int]$g.Name]=$g.Group }

$mapRows=New-Object System.Collections.Generic.List[object]
$exactRaw=0;$exactDec=0;$exactBoth=0;$conflicts=0;$ambiguous=0;$unresolved=0;$orderConsistent=0
$orderedArchives=@($archives | Sort-Object {[int]$_.outer_index})
$orderComparable=($orderedArchives.Count -eq $publicRows.Count)

foreach($a in $orderedArchives) {
    $idx=[int]$a.outer_index
    $rawSha=([string]$a.toc_sha256_raw).ToLowerInvariant()
    $decSha=([string]$a.toc_sha256_decrypted).ToLowerInvariant()
    $rawMatches=@(Resolve-PublicSha $rawSha)
    $decMatches=@(Resolve-PublicSha $decSha)
    $name='';$confidence='unresolved';$candidates='';$hashMode='none'

    if($rawMatches.Count -gt 1 -or $decMatches.Count -gt 1) {
        $all=@($rawMatches+$decMatches|Sort-Object -Unique)
        $candidates=($all -join ';')
        $name=$candidates
        $confidence='ambiguous_toc_sha256'
        $hashMode='ambiguous'
        $ambiguous++
    } elseif($rawMatches.Count -eq 1 -and $decMatches.Count -eq 1) {
        if($rawMatches[0] -eq $decMatches[0]) {
            $name=$rawMatches[0];$confidence='exact_both_toc_sha256';$hashMode='raw+decrypted';$exactBoth++
        } else {
            $candidates=($rawMatches[0]+';'+$decMatches[0]);$name=$candidates;$confidence='conflict_dual_sha256';$hashMode='conflict';$conflicts++
        }
    } elseif($rawMatches.Count -eq 1) {
        $name=$rawMatches[0];$confidence='exact_raw_toc_sha256';$hashMode='raw';$exactRaw++
    } elseif($decMatches.Count -eq 1) {
        $name=$decMatches[0];$confidence='exact_decrypted_toc_sha256';$hashMode='decrypted';$exactDec++
    } else {
        $unresolved++
    }

    $orderName=''
    if($orderComparable -and $idx -ge 0 -and $idx -lt $publicRows.Count){$orderName=$publicRows[$idx].name}
    $isExact=($confidence -like 'exact_*')
    $orderOk=($isExact -and $orderName -and $name -eq $orderName)
    if($orderOk){$orderConsistent++}

    $g=@(); if($groupsByIndex.ContainsKey($idx)){$g=@($groupsByIndex[$idx])}
    $ycd=@($g|Where-Object extension -eq 'ycd').Count
    $yas=@($g|Where-Object extension -eq 'yas').Count
    $ymt=@($g|Where-Object extension -eq 'ymt').Count
    $systems=if($isExact){Classify-Archive $name}else{'unknown'}
    $score=if($isExact){Score-Archive $name $ycd $yas}else{[Math]::Min(35,$ycd)+[Math]::Min(20,$yas)}
    $orderSystems=if($orderName){Classify-Archive $orderName}else{'unknown'}
    $orderScore=if($orderName){Score-Archive $orderName $ycd $yas}else{$score}

    $mapRows.Add([pscustomobject]@{
        outer_index=$idx;outer_hash=$a.outer_hash;outer_generated_name=$a.outer_generated_name;
        toc_sha256_raw=$rawSha;toc_sha256_decrypted=$decSha;public_archive_name=$name;confidence=$confidence;hash_mode=$hashMode;
        collision_candidates=$candidates;public_order_candidate=$orderName;order_consistent_with_exact=$orderOk;
        systems=$systems;order_candidate_systems=$orderSystems;ycd_count=$ycd;yas_count=$yas;ymt_count=$ymt;
        nested_entries=$g.Count;priority_score=$score;order_candidate_priority=$orderScore
    })
}

$mapRows | Sort-Object outer_index | Export-Csv -NoTypeInformation -Encoding UTF8 -LiteralPath (Join-Path $OutDir 'ANIM0-dual-archive-map.csv')
$mapRows | Where-Object {$_.confidence -like 'exact_*'} | Sort-Object -Property @{Expression={[int]$_.priority_score};Descending=$true}, @{Expression={[int]$_.ycd_count};Descending=$true} | Export-Csv -NoTypeInformation -Encoding UTF8 -LiteralPath (Join-Path $OutDir 'ANIM0-dual-exact-priority-archives.csv')
$mapRows | Sort-Object -Property @{Expression={[int]$_.order_candidate_priority};Descending=$true}, @{Expression={[int]$_.ycd_count};Descending=$true} | Export-Csv -NoTypeInformation -Encoding UTF8 -LiteralPath (Join-Path $OutDir 'ANIM0-order-audit.csv')

$mapByIndex=@{};foreach($m in $mapRows){$mapByIndex[[int]$m.outer_index]=$m}
$ycdRows=foreach($r in $nested|Where-Object extension -eq 'ycd'){
    $m=$mapByIndex[[int]$r.outer_index]
    [pscustomobject]@{outer_index=$m.outer_index;public_archive_name=$m.public_archive_name;archive_confidence=$m.confidence;hash_mode=$m.hash_mode;public_order_candidate=$m.public_order_candidate;systems=$m.systems;order_candidate_systems=$m.order_candidate_systems;nested_index=$r.nested_index;ycd_hash=$r.hash_hex;generated_name=$r.generated_name;enc_key=$r.enc_key;enc_config=$r.enc_config;compressor=$r.compressor;logical_size=$r.logical_size;on_disk_size=$r.on_disk_size;is_resource=$r.is_resource;archive_priority=$m.priority_score;order_candidate_priority=$m.order_candidate_priority}
}
$ycdRows | Sort-Object -Property @{Expression={[int]$_.order_candidate_priority};Descending=$true}, @{Expression={[int]$_.outer_index};Descending=$false}, @{Expression={[int]$_.nested_index};Descending=$false} | Export-Csv -NoTypeInformation -Encoding UTF8 -LiteralPath (Join-Path $OutDir 'ANIM0-dual-ycd-candidates.csv')

$yasRows=foreach($r in $nested|Where-Object extension -eq 'yas'){
    $m=$mapByIndex[[int]$r.outer_index]
    [pscustomobject]@{outer_index=$m.outer_index;public_archive_name=$m.public_archive_name;archive_confidence=$m.confidence;hash_mode=$m.hash_mode;public_order_candidate=$m.public_order_candidate;systems=$m.systems;order_candidate_systems=$m.order_candidate_systems;nested_index=$r.nested_index;yas_hash=$r.hash_hex;generated_name=$r.generated_name;enc_key=$r.enc_key;enc_config=$r.enc_config;compressor=$r.compressor;logical_size=$r.logical_size;on_disk_size=$r.on_disk_size;archive_priority=$m.priority_score;order_candidate_priority=$m.order_candidate_priority}
}
$yasRows | Sort-Object -Property @{Expression={[int]$_.order_candidate_priority};Descending=$true}, @{Expression={[int]$_.outer_index};Descending=$false}, @{Expression={[int]$_.nested_index};Descending=$false} | Export-Csv -NoTypeInformation -Encoding UTF8 -LiteralPath (Join-Path $OutDir 'ANIM0-dual-yas-candidates.csv')

$report=New-Object System.Collections.Generic.List[string]
$report.Add('VOX RDR2 anim_0 dual fingerprint mapper v0.8.1')
$report.Add('================================================')
$report.Add("Nested archives: $($archives.Count)")
$report.Add("CitizenFX anim_0 subarchives: $publicCount")
$report.Add("Order comparable: $orderComparable")
$report.Add("Exact RAW TOC SHA-256 matches: $exactRaw")
$report.Add("Exact DECRYPTED TOC SHA-256 matches: $exactDec")
$report.Add("Exact BOTH matches: $exactBoth")
$report.Add("Dual-hash conflicts: $conflicts")
$report.Add("Ambiguous SHA matches: $ambiguous")
$report.Add("Unresolved: $unresolved")
$report.Add("Exact matches consistent with CitizenFX order: $orderConsistent")
$report.Add("YCD entries: $($ycdRows.Count)")
$report.Add("YAS entries: $($yasRows.Count)")
$report.Add('')
$report.Add('TOP EXACT ARCHIVES')
$topExact=$mapRows|Where-Object {$_.confidence -like 'exact_*'}|Sort-Object -Property @{Expression={[int]$_.priority_score};Descending=$true},@{Expression={[int]$_.ycd_count};Descending=$true}|Select-Object -First 80
foreach($m in $topExact){$report.Add(('[{0,3}] {1} | {2} | {3} | ycd={4} yas={5} | order_ok={6}' -f $m.priority_score,$m.public_archive_name,$m.confidence,$m.systems,$m.ycd_count,$m.yas_count,$m.order_consistent_with_exact))}
$report.Add('')
$report.Add('TOP ORDER-ONLY CANDIDATES (NOT AUTHORITATIVE NAMES)')
$topOrder=$mapRows|Sort-Object -Property @{Expression={[int]$_.order_candidate_priority};Descending=$true},@{Expression={[int]$_.ycd_count};Descending=$true}|Select-Object -First 80
foreach($m in $topOrder){$report.Add(('[{0,3}] index={1} candidate={2} | {3} | ycd={4} yas={5}' -f $m.order_candidate_priority,$m.outer_index,$m.public_order_candidate,$m.order_candidate_systems,$m.ycd_count,$m.yas_count))}
$report|Set-Content -Encoding UTF8 -LiteralPath (Join-Path $OutDir 'ANIM0-dual-report.txt')

$exactTotal=$exactRaw+$exactDec+$exactBoth
[pscustomobject]@{
    tool='VOX RDR2 anim_0 dual fingerprint mapper';version='0.8.1';nested_archives=$archives.Count;citizenfx_subarchives=$publicCount;
    order_comparable=$orderComparable;exact_raw_toc_sha256_matches=$exactRaw;exact_decrypted_toc_sha256_matches=$exactDec;exact_both_matches=$exactBoth;
    exact_total=$exactTotal;dual_hash_conflicts=$conflicts;ambiguous_matches=$ambiguous;unresolved=$unresolved;order_consistent_exact=$orderConsistent;
    ycd_entries=$ycdRows.Count;yas_entries=$yasRows.Count;raw_assets_written=$false
} | ConvertTo-Json -Depth 4 | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $OutDir 'ANIM0-dual-summary.json')
Write-Host "[MAP] archives=$($archives.Count) raw=$exactRaw decrypted=$exactDec both=$exactBoth unresolved=$unresolved ycd=$($ycdRows.Count) yas=$($yasRows.Count)"
exit 0
