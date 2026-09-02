use anyhow::{Context, Result};
use rpf_archive::{RpfBuilder, RpfEncryption};
use std::{env, fs, path::{Path, PathBuf}};

fn main() -> Result<()> {
    let args: Vec<String> = env::args().collect();
    if args.len() != 3 {
        anyhow::bail!("usage: vox-rpf-pack <vox_rdr2_bridge.ycd> <output dlc.rpf>");
    }

    let ycd_path = PathBuf::from(&args[1]);
    let output = PathBuf::from(&args[2]);
    let ycd = fs::read(&ycd_path).with_context(|| format!("reading {}", ycd_path.display()))?;
    if ycd.len() < 16 || &ycd[0..4] != b"RSC7" {
        anyhow::bail!("input is not a GTA V RSC7/YCD resource");
    }

    let mut animations = RpfBuilder::new(RpfEncryption::None);
    animations.add_file("vox_rdr2_bridge.ycd", ycd);
    let animations_rpf = animations.build(None).context("building animations.rpf")?;

    let content_xml = r#"<?xml version="1.0" encoding="UTF-8"?>
<CDataFileMgr__ContentsOfDataFileXml>
  <disabledFiles />
  <includedXmlFiles />
  <includedDataFiles />
  <dataFiles>
    <Item>
      <filename>dlc_voxrdr2bridge:/animations.rpf</filename>
      <fileType>PEDSTREAM_FILE</fileType>
      <overlay value="true" />
      <disabled value="true" />
      <persistent value="true" />
    </Item>
  </dataFiles>
  <contentChangeSets>
    <Item>
      <changeSetName>VOXRDR2BRIDGE_AUTOGEN</changeSetName>
      <filesToDisable />
      <filesToEnable>
        <Item>dlc_voxrdr2bridge:/animations.rpf</Item>
      </filesToEnable>
      <txdToLoad />
      <txdToUnload />
      <residentResources />
      <unregisterResources />
    </Item>
  </contentChangeSets>
  <patchFiles />
</CDataFileMgr__ContentsOfDataFileXml>
"#;

    let setup_xml = r#"<?xml version="1.0" encoding="UTF-8"?>
<SSetupData>
  <deviceName>dlc_voxrdr2bridge</deviceName>
  <datFile>content.xml</datFile>
  <timeStamp>09/02/2026 00:00:00</timeStamp>
  <nameHash>voxrdr2bridge</nameHash>
  <contentChangeSetGroups>
    <Item>
      <NameHash>GROUP_STARTUP</NameHash>
      <ContentChangeSets>
        <Item>VOXRDR2BRIDGE_AUTOGEN</Item>
      </ContentChangeSets>
    </Item>
  </contentChangeSetGroups>
  <type>EXTRACONTENT_COMPAT_PACK</type>
  <order value="9000" />
</SSetupData>
"#;

    let mut dlc = RpfBuilder::new(RpfEncryption::None);
    dlc.add_file("content.xml", content_xml.as_bytes().to_vec());
    dlc.add_file("setup2.xml", setup_xml.as_bytes().to_vec());
    dlc.add_file("animations.rpf", animations_rpf.clone());
    let dlc_bytes = dlc.build(None).context("building dlc.rpf")?;

    if let Some(parent) = output.parent() { fs::create_dir_all(parent)?; }
    fs::write(&output, dlc_bytes).with_context(|| format!("writing {}", output.display()))?;

    // Keep the inner archive next to the DLC as a debugging/recovery aid.
    let inner = output.parent().unwrap_or_else(|| Path::new(".")).join("animations.rpf");
    fs::write(&inner, animations_rpf)?;
    println!("[PACK-OK] {}", output.display());
    Ok(())
}
