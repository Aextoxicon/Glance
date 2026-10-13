// 性能基线测parse_code全链路（黑盒）。
// 必须显式指定profile：
//   cargo test --release --test perf -- --ignored --nocapture
//   cargo test --test perf -- --ignored --nocapture   # debug对照（C代码opt-level=0，只作参照）
// 样本自包含（内联基片段重复生成），不再依赖已移除的 shared/ 目录。
use std::time::Instant;
use uniffi_code_parser::parse_code;

// 各语言最小合法基片段，重复到目标字节构成固定输入
const BASES: &[(&str, &str, &str)] = &[
    (".rs", "rust", "// comment\nfn main() {\n    let x: i32 = 1;\n    println!(\"{}\", x);\n}\n"),
    (".py", "python", "# comment\ndef main():\n    x = 1\n    print(x)\n"),
    (".kt", "kotlin", "// comment\nfun main() {\n    val x = 1\n    println(x)\n}\n"),
    (".html", "html", "<!-- comment -->\n<div class=\"a\">hi</div>\n"),
    (".json", "json", "{\n  \"key\": \"value\",\n  \"n\": 1\n}\n"),
];

fn synth(base: &str, target_bytes: usize) -> String {
    let mut out = String::with_capacity(target_bytes + base.len());
    while out.len() < target_bytes {
        out.push_str(base);
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
    let sizes = [1024usize, 32 * 1024, 256 * 1024];
    for (ext, _name, base) in BASES {
        for size in sizes {
            let src = synth(base, size);
            measure(_name, ext, &src);
        }
    }
}
