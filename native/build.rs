fn main() {
    println!("cargo:rerun-if-changed=src/lib.rs");
    println!("cargo:rerun-if-changed=src/flutter_api.rs");
    println!("cargo:rerun-if-changed=flutter_rust_bridge.yaml");
    let config_path = std::fs::canonicalize("flutter_rust_bridge.yaml")
        .expect("cannot canonicalize flutter_rust_bridge.yaml");
    let config_path = config_path.to_str().expect("config path is not valid utf-8");
    let config = lib_flutter_rust_bridge_codegen::codegen::Config::from_config_file(config_path)
        .expect("failed to load flutter_rust_bridge.yaml")
        .expect("flutter_rust_bridge.yaml not found");
    lib_flutter_rust_bridge_codegen::codegen::generate(config, lib_flutter_rust_bridge_codegen::codegen::MetaConfig::default())
        .expect("flutter_rust_bridge codegen failed");
}
