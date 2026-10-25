use uniffi_code_parser::parse_cad;

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
