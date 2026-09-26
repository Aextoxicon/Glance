// 性能基线测 parse_code 全链路
// 必须显式指定profile：
//   cargo test --test perf -- --ignored --nocapture
//   cargo test --release --test perf -- --ignored --nocapture
// debug下26个grammar的C代码是opt-level=0，数字只作对照
use std::time::Instant;
use uniffi_code_parser::parse_code;

const TEMPLATE_DIR: &str = "../shared/src/commonTest/resources/templates";

fn synth(file: &str, target_bytes: usize) -> String {
    let base = std::fs::read_to_string(format!("{TEMPLATE_DIR}/{file}")).unwrap();
    let mut out = String::new();
    while out.len() < target_bytes {
        out.push_str(&base);
        out.push('\n');
    }
    out
}

fn measure(label: &str, ext: &str, source: &str) {
    // 预热
    for _ in 0..3 {
        std::hint::black_box(parse_code(source.to_string(), ext.to_string()));
    }
    let mut samples = Vec::with_capacity(30);
    for _ in 0..30 {
        let owned = source.to_string();
        let t = Instant::now();
        std::hint::black_box(parse_code(owned, ext.to_string()));
        samples.push(t.elapsed().as_secs_f64() * 1000.0);
    }
    samples.sort_by(|a, b| a.partial_cmp(b).unwrap());
    let median = samples[samples.len() / 2];
    let p90 = samples[samples.len() * 9 / 10];
    println!(
        "{:>10} {:>7}  {:>9} bytes   median {:>9.3} ms   p90 {:>9.3} ms",
        label,
        ext,
        source.len(),
        median,
        p90
    );
}

#[test]
#[ignore = "性能基线，不在常规测试里跑"]
fn parse_code_blackbox() {
    let cases: &[(&str, &str)] = &[
        ("rust", ".rs"),
        ("python", ".py"),
        ("kotlin", ".kt"),
        ("html", ".html"),
        ("json", ".json"),
    ];
    let sizes = [1024usize, 32 * 1024, 256 * 1024];
    for (name, ext) in cases {
        let file = match *ext {
            ".kt" => "App.kt",
            ".html" => "index.html",
            ".json" => "config.json",
            ".rs" => "lib.rs",
            ".py" => "hello.py",
            _ => unreachable!(),
        };
        for size in sizes {
            let src = synth(file, size);
            measure(name, ext, &src);
        }
    }
}
