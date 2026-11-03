use crate::{parse_cad, parse_cad_geometry, parse_code, CadGeometry, CadParseResult, CodeParseResult};
use flutter_rust_bridge::frb;

#[frb(sync)]
pub fn parse_code_frb(source: String, filename: String) -> CodeParseResult {
    parse_code(source, filename)
}

#[frb(sync)]
pub fn parse_cad_geometry_frb(file_bytes: Vec<u8>, filename: String) -> CadGeometry {
    parse_cad_geometry(file_bytes, filename)
}

#[frb(sync)]
pub fn parse_cad_frb(file_bytes: Vec<u8>, filename: String) -> CadParseResult {
    parse_cad(file_bytes, filename)
}
