param(
    [Parameter(Mandatory=$true)][string]$NestedCsv,
    [Parameter(Mandatory=$true)][string]$CitizenFxHeader,
    [Parameter(Mandatory=$true)][string]$OutDir
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

Add-Type -TypeDefinition @'
using System;
public static class VoxRageHash {
    public static uint AtStringHash(string s) {
        unchecked {
            uint h = 0;
            foreach (char ch0 in s) {
                char ch = ch0 == '\\' ? '/' : char.ToLowerInvariant(ch0);
                h += (byte)ch;
                h += h << 10;
                h ^= h >> 6;
            }
            h += h << 3;
            h ^= h >> 11;
            h += h << 15;
            return h;
        }
    }
}
'@

function Classify-Archive([string]$name) {
    $n = $name.ToLowerInvariant()
    $systems = New-Object System.Collections.Generic.List[string]
    if ($n -match 'gesture|interact|scenario|script|ambient|amb_|hostage|conversation|greet|antagon|react') { $systems.Add('interaction') }
    if ($n -match 'melee|combat|grapple|fight|takedown|busted|butcher|revive') { $systems.Add('melee') }
    if ($n -match 'loco|strafe|ledge|getup|avoid|move|vault|climb') { $systems.Add('locomotion') }
    if ($n -match 'door') { $systems.Add('doors') }
    if ($n -match 'react|injur|damage|ragdoll|fall') { $systems.Add('reactions') }
    if ($n -match 'weapon|gun|rifle|pistol|bow|throw') { $systems.Add('weapons') }
    if ($n -match 'animal|creature|horse') { $systems.Add('wildlife') }
    if ($n -match 'veh_|vehicle|train|wagon|cart') { $systems.Add('vehicle') }
    if ($systems.Count -eq 0) { $systems.Add('other') }
    return (($systems | Select-Object -Unique) -join ';')
}
function Score-Archive([string]$name,[int]$ycdCount) {
    $n=$name.ToLowerInvariant(); $s=[Math]::Min(30,$ycdCount)
    if($n -match 'ai_gesture|interact|scenario|script@common|script_common'){ $s+=100 }
    if($n -match 'melee|combat|grapple|fight|takedown'){ $s+=95 }
    if($n -match 'loco|strafe|ledge|getup|avoid|vault|climb'){ $s+=85 }
    if($n -match 'door'){ $s+=90 }
    if($n -match 'react|injur|damage|ragdoll|revive|busted'){ $s+=80 }
    if($n -match 'ambient|amb_|hostage|conversation'){ $s+=70 }
    if($n -match 'weapon|gun|rifle|pistol'){ $s+=55 }
    if($n -match 'cuts@|cuts_'){ $s-=35 }
    return $s
}

$nested = Import-Csv -LiteralPath $NestedCsv
if(-not $nested -or -not ($nested[0].PSObject.Properties.Name -contains 'outer_name')) { throw 'Nested CSV schema is not the expected v0.6/v0.7 schema.' }

$header = Get-Content -LiteralPath $CitizenFxHeader
$inAnim = $false
$publicNames = New-Object System.Collections.Generic.List[string]
foreach($line in $header) {
    if($line -match '^\s*//\s*anim_0\.rpf\s*$') { $inAnim=$true; continue }
    if($inAnim -and $line -match '^\s*//\s*common_0\.rpf\s*$') { break }
    if(-not $inAnim) { continue }
    if($line -match '//\s*([^/]+\.rpf)\s*$') {
        $nm=$Matches[1].Trim()
        if($nm -ne 'anim_0.rpf') { $publicNames.Add($nm) }
    }
}
if($publicNames.Count -lt 100) { throw "CitizenFX anim_0 section parsing returned only $($publicNames.Count) names." }

$byHash=@{}
foreach($nm in $publicNames) {
    $base=[IO.Path]::GetFileNameWithoutExtension($nm)
    $variants=@(
        @{kind='basename_no_ext'; text=$base},
        @{kind='filename_with_ext'; text=$nm}
    )
    foreach($v in $variants) {
        $h=[VoxRageHash]::AtStringHash([string]$v.text)
        $hx=('{0:X8}' -f $h)
        if(-not $byHash.ContainsKey($hx)) { $byHash[$hx]=New-Object System.Collections.Generic.List[object] }
        $byHash[$hx].Add([pscustomobject]@{name=$nm;variant=$v.kind})
    }
}

$outerGroups = $nested | Group-Object outer_index,outer_name | Sort-Object { [int](($_.Name -split ',')[0]) }
$mapRows=New-Object System.Collections.Generic.List[object]
$mappedByIndex=@{}
$orderNames=@($publicNames)
$exactCount=0
foreach($g in $outerGroups) {
    $r=$g.Group[0]; $outerName=[string]$r.outer_name; $idx=[int]$r.outer_index
    $hx=''; if($outerName -match '^([0-9A-Fa-f]{8})\.rpf$'){ $hx=$Matches[1].ToUpperInvariant() }
    $public='';$confidence='unresolved';$variant='';$orderCandidate=''
    if($hx -and $byHash.ContainsKey($hx)) {
        $m=@($byHash[$hx] | Sort-Object name -Unique)
        if($m.Count -eq 1){$public=$m[0].name;$variant=$m[0].variant;$confidence='exact_rage_hash';$exactCount++}
        elseif($m.Count -gt 1){$public=($m.name -join ';');$variant='collision';$confidence='ambiguous_hash'}
    }
    if($idx -ge 0 -and $idx -lt $orderNames.Count){$orderCandidate=$orderNames[$idx]}
    if(-not $public -and $outerGroups.Count -eq $orderNames.Count -and $orderCandidate){$public=$orderCandidate;$confidence='citizenfx_order_candidate'}
    $ycd=(@($g.Group | Where-Object extension -eq 'ycd')).Count
    $yas=(@($g.Group | Where-Object extension -eq 'yas')).Count
    $ymt=(@($g.Group | Where-Object extension -eq 'ymt')).Count
    $systems=if($public){Classify-Archive $public}else{'unknown'}
    $score=if($public){Score-Archive $public $ycd}else{[Math]::Min(30,$ycd)}
    $row=[pscustomobject]@{outer_index=$idx;outer_hash=$hx;outer_generated_name=$outerName;public_archive_name=$public;confidence=$confidence;hash_variant=$variant;citizenfx_order_candidate=$orderCandidate;systems=$systems;ycd_count=$ycd;yas_count=$yas;ymt_count=$ymt;nested_entries=$g.Count;priority_score=$score}
    $mapRows.Add($row);$mappedByIndex[$idx]=$row
}
$mapRows | Sort-Object outer_index | Export-Csv -NoTypeInformation -Encoding UTF8 -LiteralPath (Join-Path $OutDir 'ANIM0-archive-map.csv')
$mapRows | Sort-Object priority_score -Descending,ycd_count -Descending | Export-Csv -NoTypeInformation -Encoding UTF8 -LiteralPath (Join-Path $OutDir 'ANIM0-priority-archives.csv')

$ycdRows = foreach($r in $nested | Where-Object extension -eq 'ycd') {
    $idx=[int]$r.outer_index; $m=$mappedByIndex[$idx]
    [pscustomobject]@{outer_index=$idx;outer_hash=$m.outer_hash;public_archive_name=$m.public_archive_name;archive_confidence=$m.confidence;systems=$m.systems;nested_index=$r.nested_index;ycd_hash=$r.hash_hex;generated_name=$r.generated_name;enc_key=$r.enc_key;enc_config=$r.enc_config;compressor=$r.compressor;logical_size=$r.logical_size;on_disk_size=$r.on_disk_size;is_resource=$r.is_resource;archive_priority=$m.priority_score}
}
$ycdRows | Sort-Object archive_priority -Descending,outer_index,nested_index | Export-Csv -NoTypeInformation -Encoding UTF8 -LiteralPath (Join-Path $OutDir 'ANIM0-ycd-candidates.csv')

$sysCounts=@{}
foreach($m in $mapRows){foreach($s in ([string]$m.systems -split ';')){if(-not $sysCounts.ContainsKey($s)){$sysCounts[$s]=0};$sysCounts[$s]+=[int]$m.ycd_count}}
$report=New-Object System.Collections.Generic.List[string]
$report.Add('VOX RDR2 anim_0 public-name mapper v0.7.0')
$report.Add('=============================================')
$report.Add("Nested archive groups: $($outerGroups.Count)")
$report.Add("CitizenFX names in anim_0 section: $($publicNames.Count)")
$report.Add("Exact RAGE-hash archive matches: $exactCount")
$report.Add("Archives with a public/candidate name: $((@($mapRows | Where-Object public_archive_name)).Count)")
$report.Add("YCD entries: $($ycdRows.Count)")
$report.Add('')
$report.Add('YCD BY PROJECT SYSTEM')
foreach($kv in $sysCounts.GetEnumerator() | Sort-Object Value -Descending){$report.Add("$($kv.Key): $($kv.Value)")}
$report.Add('')
$report.Add('TOP ARCHIVES')
foreach($m in $mapRows | Sort-Object priority_score -Descending,ycd_count -Descending | Select-Object -First 60){$report.Add(('[{0,3}] {1} | {2} | ycd={3} yas={4} ymt={5} | {6}' -f $m.priority_score,$m.public_archive_name,$m.systems,$m.ycd_count,$m.yas_count,$m.ymt_count,$m.confidence))}
$report | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $OutDir 'ANIM0-report.txt')

[pscustomobject]@{
    tool='VOX RDR2 anim_0 public-name mapper';version='0.7.0';nested_archive_groups=$outerGroups.Count;citizenfx_names=$publicNames.Count;exact_hash_matches=$exactCount;named_or_order_candidates=(@($mapRows|Where-Object public_archive_name)).Count;ycd_entries=$ycdRows.Count;raw_assets_written=$false
} | ConvertTo-Json -Depth 4 | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $OutDir 'ANIM0-summary.json')

Write-Host "[MAP] nested archives=$($outerGroups.Count) public names=$($publicNames.Count) exact hash matches=$exactCount ycd=$($ycdRows.Count)"
