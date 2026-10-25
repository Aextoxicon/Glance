use std::path::PathBuf;
use uniffi_code_parser::parse_cad;

// fixtures来自acadrust 0.6.7测试集(MPL-2.0)，覆盖多版本DWG/DXF解析

const MINIMAL_DXF: &str = "0\nSECTION\n2\nHEADER\n9\n$ACADVER\n1\nAC1027\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n0\nLINE\n8\n0\n10\n0.0\n20\n0.0\n30\n0.0\n11\n1.0\n21\n1.0\n31\n0.0\n0\nENDSEC\n0\nEOF\n";

#[test]
fn parse_minimal_dxf_returns_format_and_entity() {
    let bytes = MINIMAL_DXF.as_bytes().to_vec();
    let res = parse_cad(bytes, "sample.dxf".to_string());
    assert_eq!(res.format, "DXF", "format should be DXF after successful parse");
    let total: u32 = res.entity_counts.iter().sum();
    assert!(total >= 1, "expected at least one entity, got {}", total);
    assert_eq!(res.version, "AC1027");
}

#[test]
fn parse_unsupported_returns_default() {
    let res = parse_cad(b"not a cad".to_vec(), "x.txt".to_string());
    assert_eq!(res.format, "");
    assert_eq!(res.entity_counts.iter().sum::<u32>(), 0);
}

fn collect_cad_files(dir: &std::path::Path, out: &mut Vec<PathBuf>) {
    if let Ok(entries) = std::fs::read_dir(dir) {
        for entry in entries.flatten() {
            let path = entry.path();
            if path.is_dir() {
                collect_cad_files(&path, out);
            } else if let Some(ext) = path.extension().and_then(|e| e.to_str()) {
                if ext.eq_ignore_ascii_case("dxf") || ext.eq_ignore_ascii_case("dwg") {
                    out.push(path);
                }
            }
        }
    }
}

#[test]
fn 所有自带fixture都能解析() {
    let manifest = env!("CARGO_MANIFEST_DIR");
    let fixtures = std::path::Path::new(manifest).join("tests").join("fixtures");
    let mut files = Vec::new();
    collect_cad_files(&fixtures, &mut files);
    assert!(!files.is_empty(), "fixtures目录不应为空: {}", fixtures.display());
    let mut failed = Vec::new();
    for f in &files {
        let name = f.file_name().unwrap().to_string_lossy().to_string();
        let bytes = std::fs::read(f).expect("读取 fixture 失败");
        let res = parse_cad(bytes, name.clone());
        if res.format.is_empty() {
            failed.push(format!("{}: 解析失败format为空", name));
        }
    }
    assert!(failed.is_empty(), "以下fixture解析失败:\n{}", failed.join("\n"));
}
